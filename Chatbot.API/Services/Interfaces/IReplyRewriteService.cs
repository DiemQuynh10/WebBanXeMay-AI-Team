namespace Chatbot.API.Services.Interfaces
{
    public interface IReplyRewriteService
    {
        Task<string> RewriteAsync(
            string userMessage,
            string draftReply,
            CancellationToken cancellationToken = default);
    }
}