namespace Chatbot.API.Models.Intent
{
    public enum PriceFilterType
    {
        None = 0,
        MaxOnly = 1,
        MinOnly = 2,
        Range = 3,
        Around = 4
    }

    public class PriceIntent
    {
        public PriceFilterType FilterType { get; set; } = PriceFilterType.None;
        public decimal? MinPrice { get; set; }
        public decimal? MaxPrice { get; set; }
        public decimal? TargetPrice { get; set; }
        public string RawText { get; set; } = string.Empty;
    }
}