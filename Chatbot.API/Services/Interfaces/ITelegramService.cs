namespace Chatbot.API.Services.Interfaces
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text);
        Task SendMessageAsync(long chatId, string text, object? replyMarkup = null, string? parseMode = null);
        Task SendTypingAsync(long chatId);
        Task SetWebhookAsync(string webhookUrl, string secretToken);
    }
}