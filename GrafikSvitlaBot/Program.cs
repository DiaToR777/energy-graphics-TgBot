using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    static TelegramBotClient bot;
    static HttpClient http = new();

    const long LOG_CHAT_ID = 5738366583;

    const string BASE_URL = "https://raw.githubusercontent.com/DiaToR777/energy-graphics-bot/main/Parser/graphics/";
    static async Task Main()
    {
        // Токен з environment variable (для Railway)
        var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        bot = new TelegramBotClient(token!);



        var me = await bot.GetMe();
        Console.WriteLine($"✓ Бот @{me.Username} запущено");

        var cts = new CancellationTokenSource();

        bot.StartReceiving(
            HandleUpdate,
            HandleError,
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );

        Console.WriteLine("Бот працює. Ctrl+C для зупинки");

        // Чекаємо безкінечно
        await Task.Delay(-1, cts.Token);
    }

    static async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;

        var chatId = update.Message.Chat.Id;
        var userName = update.Message.Chat.FirstName ?? "User";

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {userName}: {text}");

        if (text == "/start" || text == "/grafik")
        {
            await SendGraphics(chatId, ct);
        }
    }

    static async Task SendGraphics(long chatId, CancellationToken ct)
    {
        try
        {
            await bot.SendMessage(chatId, "⏳ Завантажую графіки...", cancellationToken: ct);

            // Читаємо час оновлення
            string updateTime;
            try
            {
                updateTime = await http.GetStringAsync(BASE_URL + "last_update.txt", ct);

            }
            catch
            {
                updateTime = "невідомо";
            }

            int sentCount = 0;

            // Пробуємо завантажити до 3 графіків
            for (int i = 1; i <= 3; i++)
            {
                try
                {
                    var url = BASE_URL + $"grafic_{i}.png";
                    var bytes = await http.GetByteArrayAsync(url, ct);

                    using var stream = new MemoryStream(bytes);

                    await bot.SendPhoto(
                        chatId: chatId,
                        photo: InputFile.FromStream(stream, $"grafic_{i}.png"),
                        caption: $"📊 Графік відключень #{i}\n🕐 Оновлено: {updateTime}",
                        cancellationToken: ct
                    );

                    sentCount++;
                    Console.WriteLine($"[{chatId}] ✓ Відправлено графік {i}");
                }
                catch (HttpRequestException)
                {
                    // Файл не існує - це нормально
                    break;
                }
            }

            if (sentCount == 0)
            {
                await bot.SendMessage(
                    chatId,
                    "❌ Графіки поки недоступні. Спробуйте пізніше.",
                    cancellationToken: ct
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{chatId}] ❌ Критична помилка: {ex.Message}");

            // 1. Надсилаємо користувачу загальне повідомлення
            await bot.SendMessage(
                chatId,
                $"❌ Виникла критична помилка. Бот про неї знає і скоро її виправить.",
                cancellationToken: ct
            );

            // 2. Надсилаємо розробнику деталі збою
            var logMessage = $"🚨 КРИТИЧНИЙ ЗБІЙ у SendGraphics!\n" +
                             $"Користувач: {chatId}\n" +
                             $"Помилка: **{ex.Message}**\n" +
                             $"Стек: ```{ex.StackTrace?[..300]}...```"; // Обмежуємо стек для ТГ

            await bot.SendMessage(
                chatId: LOG_CHAT_ID,
                text: logMessage,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
        }
    }

    static async Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"❌ Помилка бота: {ex.Message}");

        // Надсилаємо повідомлення про помилку полінгу розробнику
        var logMessage = $"⛔️ ПОМИЛКА ПОЛІНГУ! Бот може бути нестабільним.\n" +
                         $"Помилка: **{ex.Message}**\n" +
                         $"Стек: ```{ex.StackTrace?[..300]}...```";

        await bot.SendMessage(
            chatId: LOG_CHAT_ID,
            text: logMessage,
            parseMode: ParseMode.Markdown,
            cancellationToken: ct
        );
    }
}