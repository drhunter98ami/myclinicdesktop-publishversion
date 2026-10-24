using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class LabName
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = "";
        public bool IsArchived { get; set; }

        public DateTime CreatedAt { get; set; } = AppClock.Now;
    }
}
