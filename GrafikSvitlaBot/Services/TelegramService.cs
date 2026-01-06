using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace GrafikSvitlaBot.Services;

public class TelegramService
{
    private readonly RateLimiter _rateLimiter;
    private readonly GraphicsService graphicsService;

    private readonly int _rateLimitSeconds = 30;

    private readonly long _adminId;
    private TelegramBotClient _bot;

    public TelegramService(HttpClient http)
    {
        var token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        _bot = new TelegramBotClient(token!);
        var AdminIdStr = Environment.GetEnvironmentVariable("ADMIN_TG_ID!");
        _adminId = long.Parse(AdminIdStr!);

        graphicsService = new(http);

        _rateLimiter = new(_rateLimitSeconds);
    }

    public async Task Start()
    {
        var me = await _bot.GetMe();
        Console.WriteLine($"✓ Бот @{me.Username} запущено");

        var cts = new CancellationTokenSource();

        _bot.StartReceiving(
            HandleUpdate,
            HandleError,
            new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() },
            cancellationToken: cts.Token
        );
    }

    private async Task HandleUpdate(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;

        var chatId = update.Message.Chat.Id;

        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] {update.Message.Chat.FirstName ?? "User"}, userId {{ {update.Message.Chat.Id} }} username {{ {update.Message.Chat.Username ?? "null"} }} : {text}");
        if (text == "/help")
        {
            await bot.SendMessage(chatId, "Вас вітає GrafikSvitlaBot!\n" +
                "Команда /start або /grafik відправить актуальний графік відключення світла\n ");
            return;
        }

        // Оновлюємо час останнього запиту для цього користувача
        if (text == "/start" || text == "/grafik")
        {
            var rate = _rateLimiter.CheckRateLimit(chatId);
            if (!rate.IsAllowed)
            {
                await bot.SendMessage(chatId,
                    $"⏳ Зачекай... {_rateLimitSeconds}секунд перед відправкою наступного запиту");
                return;
            }
        }
        await SendGraphics(chatId, ct); return;
    }

    private async Task SendGraphics(long chatId, CancellationToken ct)
    {
        int sentCount = 0;
        try
        {
            await _bot.SendMessage(chatId, "⏳ Завантажую графіки...", cancellationToken: ct);
            var graphics = await graphicsService.GetGraphicsAsync(ct);
            var updateTime = graphicsService.GetCurrentUpdateText();

            foreach (var g in graphics)
            {
                sentCount++;
                // ОБОВ'ЯЗКОВО: використовуємо using для MemoryStream!
                using var ms = new MemoryStream(g.Bytes);

                await _bot.SendPhoto(
                    chatId: chatId,
                    photo: InputFile.FromStream(ms, g.FileName),
                    caption: $"📊 Графік відключень #{sentCount}\n🕐 Оновлено: {updateTime}",
                    cancellationToken: ct
                );
            }
            if (sentCount == 0)
            {
                await _bot.SendMessage(
                    chatId,
                    "❌ Графіки поки недоступні. Спробуйте пізніше.",
                    cancellationToken: ct
                );
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{chatId}] ❌ Критична помилка: {ex.Message}");

            await _bot.SendMessage(
                chatId,
                $"❌ Виникла критична помилка. Бот про неї знає і скоро її виправить.",
                cancellationToken: ct
            );

            var stackTrace = ex.StackTrace ?? "Немає стеку";

                var rawStack = ex.ToString();
                var safeStack = rawStack.Length > 2000 ? rawStack[..2000] + "\n... [обрізано]" : rawStack;

            var logMessage = $"🚨 **КРИТИЧНИЙ ЗБІЙ**\n" +
                             $"Помилка: `{ex.Message}`\n\n" +
                             $"Стек:\n`{safeStack}`";

            await _bot.SendMessage(
                chatId: _adminId,
                text: logMessage,
                parseMode: ParseMode.Markdown,
                cancellationToken: ct
            );
        }
    }

    public async Task HandleError(ITelegramBotClient bot, Exception ex, CancellationToken ct)
    {
        Console.WriteLine($"❌ Помилка бота: {ex.Message}");

        // Надсилаємо повідомлення про помилку полінгу розробнику
        var logMessage = $"⛔️ ПОМИЛКА ПОЛІНГУ! Бот може бути нестабільним.\n" +
                         $"Помилка: **{ex.Message}**\n" +
                         $"Стек: ```{ex.StackTrace ?? "Стеку немає"}...```";

        await bot.SendMessage(
            chatId: _adminId,
            text: logMessage,
            parseMode: ParseMode.Markdown,
            cancellationToken: ct
        );
    }
}
