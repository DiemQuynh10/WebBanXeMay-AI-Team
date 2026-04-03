using Chatbot.API.Models.Normalization;

namespace Chatbot.API.Services.Interfaces
{
    public interface IQueryNormalizationService
    {
        NormalizationResult Analyze(string input);
    }
}