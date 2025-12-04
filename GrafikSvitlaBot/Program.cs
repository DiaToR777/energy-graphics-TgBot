using System.Collections.Concurrent;
using System.Linq.Expressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

class Program
{
    static TelegramBotClient bot;
    static HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    static long adminId;

    const string BASE_URL = "https://raw.githubusercontent.com/DiaToR777/energy-graphics-bot/main/Parser/graphics/";

    static readonly ConcurrentDictionary<long, DateTime> LastRequestTimes = new();
    const int RATE_LIMIT_SECONDS = 30;

    static string? CachedUpdateTime = null;
    static DateTime LastUpdateFetchTime = DateTime.MinValue;
    static readonly TimeSpan UpdateCacheLifetime = TimeSpan.FromSeconds(1200);
    static readonly SemaphoreSlim cacheLock = new(1, 1);  // ← додаємо семафор





    static async Task Main()
    {
        var AdminIdStr = Environment.GetEnvironmentVariable("ADMIN_TG_ID");

        adminId = long.Parse(AdminIdStr);

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

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Чекаємо безкінечно
        await Task.Delay(-1, cts.Token);
    }

    static async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;

        var chatId = update.Message.Chat.Id;
        var userName = update.Message.Chat.FirstName ?? "User";

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {userName}: {text}");
        if (text == "/help")
        {
            await bot.SendMessage(chatId, "Вас вітає GrafikSvitlaBot!\n" +
                "Команда /start або /grafik відправить актуальний графік відключення світла\n ");
            return;
        }



        // Оновлюємо час останнього запиту для цього користувача
        if (text == "/start" || text == "/grafik")
        {
            var currentTime = DateTime.UtcNow;
            if (LastRequestTimes.TryGetValue(chatId, out var lastTime) &&
                currentTime - lastTime < TimeSpan.FromSeconds(RATE_LIMIT_SECONDS))
            {
                await bot.SendMessage(chatId, "⏳ Зачекай... 30секунд перед відправкою наступного запиту");
                return;
            }

            LastRequestTimes[chatId] = currentTime;
            await SendGraphics(chatId, ct);
        }
    }
    static async Task SendGraphics(long chatId, CancellationToken ct)
    {
        try
        {
            await bot.SendMessage(chatId, "⏳ Завантажую графіки...", cancellationToken: ct);

            // Читаємо час оновлення
            string updateTime = await GetCachedUpdateTime(ct);

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
                             $"Стек: ```{ex.StackTrace ?? "Немає"}...```"; // Обмежуємо стек для ТГ

            await bot.SendMessage(
                chatId: adminId,
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
                         $"Стек: ```{ex.StackTrace ?? "Стеку немає"}...```";

        await bot.SendMessage(
            chatId: adminId,
            text: logMessage,
            parseMode: ParseMode.Markdown,
            cancellationToken: ct
        );
    }
    static async Task<string> GetCachedUpdateTime(CancellationToken ct)
    {
        // Швидка перевірка без блокування (double-checked locking)
        if (CachedUpdateTime != null &&
            DateTime.UtcNow - LastUpdateFetchTime < UpdateCacheLifetime)
        {
            return CachedUpdateTime;
        }

        // Блокуємо — тільки 1 потік може завантажувати
        await cacheLock.WaitAsync(ct);
        try
        {
            // Перевіряємо ще раз — може інший потік вже завантажив
            if (CachedUpdateTime != null &&
                DateTime.UtcNow - LastUpdateFetchTime < UpdateCacheLifetime)
            {
                return CachedUpdateTime;
            }

            // Завантажуємо
            try
            {
                CachedUpdateTime = await http.GetStringAsync(BASE_URL + "last_update.txt", ct);
            }
            catch
            {
                CachedUpdateTime = "невідомо";
            }

            LastUpdateFetchTime = DateTime.UtcNow;
            return CachedUpdateTime;
        }
        finally
        {
            cacheLock.Release();  // ← завжди звільняємо
        }
    }
}