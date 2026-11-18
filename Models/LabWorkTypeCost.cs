using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class LabWorkTypeCost
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int LabNameId { get; set; }

        [Required]
        public string WorkTypeName { get; set; } = string.Empty;

        [Required]
        public decimal Cost { get; set; }

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "SYP";
    }
}
