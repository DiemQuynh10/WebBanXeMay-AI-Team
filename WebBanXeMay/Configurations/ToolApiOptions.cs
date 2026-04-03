using System.ComponentModel.DataAnnotations;

namespace WebBanXeMay.Configurations
{
    public class ToolApiOptions
    {
        [Required]
        public string ApiKey {  get; set; } = string.Empty;
    }
}
