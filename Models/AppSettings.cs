using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class AppSettings
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public decimal UsdToSypRate { get; set; } = 15000; // Default conversion rate

        [Required]
        [MaxLength(3)]
        public string DefaultCurrency { get; set; } = "SYP"; // Default currency: USD or SYP

        public int FileKitCanalCount { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
