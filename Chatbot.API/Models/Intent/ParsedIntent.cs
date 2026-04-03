namespace Chatbot.API.Models.Intent
{
    public class ParsedIntent
    {
        public string? Category { get; set; }     
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string? Brand { get; set; }
        public string? Target { get; set; }       
        public string? RawMessage { get; set; }
    }
}
