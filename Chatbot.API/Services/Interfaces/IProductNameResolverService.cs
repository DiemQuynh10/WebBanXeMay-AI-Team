namespace Chatbot.API.Services.Interfaces
{
    public interface IProductNameResolverService
    {
        Task<List<string>> ResolveMentionedProductNamesAsync(string text, int take = 5);
    }
}