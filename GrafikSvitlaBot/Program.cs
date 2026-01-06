using GrafikSvitlaBot.Services;
class Program
{ 
    static async Task Main()
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
        TelegramService telegramService = new(http);

        await telegramService.Start();

        Console.WriteLine("Бот працює. Натисніть Ctrl+C для зупинки.");
        await Task.Delay(-1);
    }
}