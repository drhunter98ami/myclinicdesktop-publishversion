using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class TreatmentCost
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string TreatmentName { get; set; } = string.Empty;

        [Required]
        public decimal Cost { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "SYP";
        public bool RequiresCanals { get; set; }
    }
}
