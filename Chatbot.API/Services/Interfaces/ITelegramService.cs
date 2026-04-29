namespace Chatbot.API.Services.Interfaces
{
    public interface ITelegramService
    {
        Task SendMessageAsync(long chatId, string text);
        Task SendMessageAsync(long chatId, string text, object? replyMarkup = null, string? parseMode = null);
        Task SendPhotoAsync(long chatId, string photoUrl, string caption, object? replyMarkup = null, string? parseMode = null);
        Task SetWebhookAsync(string webhookUrl, string secretToken);
        Task SendTypingAsync(long chatId);
    }
}