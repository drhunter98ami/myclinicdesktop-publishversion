using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class Shortage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Item { get; set; } = string.Empty;

        [Required]
        public bool IsUrgent { get; set; }

        public decimal Price { get; set; }

        /// <summary>Number of units needed. Existing records default to one.</summary>
        public int Quantity { get; set; } = 1;

        /// <summary>"USD" or "SYP"</summary>
        [Required]
        public string Currency { get; set; } = "SYP";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
