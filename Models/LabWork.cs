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

        public string Color { get; set; } = "";

        /// <summary>"لم يتم الإرسال", "تم الإرسال، جاري العمل", "تم الاستلام", "تم الدفع"</summary>
        [Required]
        public string Status { get; set; } = "لم يتم الإرسال";

        public DateTime DateSent { get; set; } = AppClock.Now;

        public DateTime? DateReceived { get; set; }

        public DateTime? DatePaid { get; set; }

        public decimal AmountPaid { get; set; } = 0;

        public string? Notes { get; set; }
        public int? PatientId { get; set; }
        public int? LabNameId { get; set; }
        public string WorkType { get; set; } = "";
        public string WorkStatus { get; set; } = "لم يتم الإرسال";
        public string Currency { get; set; } = "USD";
        public DateTime? ExpectedDelivery { get; set; }
        public decimal RateSnapshot { get; set; } = 15000;
        public int Revision { get; set; }
    }
}
