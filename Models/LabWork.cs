using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class LabWork
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string PatientName { get; set; } = string.Empty;

        public string? LabName { get; set; }

        [Required]
        public decimal Cost { get; set; }

        public string? Teeth { get; set; }

        /// <summary>"تم الإرسال", "تم الإستلام", "تم الدفع"</summary>
        [Required]
        public string Status { get; set; } = "تم الإرسال";

        public DateTime DateSent { get; set; } = AppClock.Now;

        public DateTime? DateReceived { get; set; }

        public DateTime? DatePaid { get; set; }

        public decimal AmountPaid { get; set; } = 0;

        public string? Notes { get; set; }
    }
}
