using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;

namespace MyClinic.Models
{
    public class AppointmentEntry
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string PatientName { get; set; } = string.Empty;

        [Required]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required]
        public string Reason { get; set; } = string.Empty;

        public DateTime AppointmentDateTime { get; set; }

        public DateTime? CreatedAt { get; set; }

        public int? PatientId { get; set; }

        public bool IsAutomaticallyGenerated { get; set; }

        // Display-only fallback entry created from a saved visit when older
        // data does not have a matching row in Appointments.
        [NotMapped]
        public bool IsVisitRecord { get; set; }

        [NotMapped]
        public string AttendanceStatus { get; set; } = string.Empty;

        [NotMapped]
        public string AttendanceStatusText { get; set; } = string.Empty;

        [NotMapped]
        public bool ShowAttendanceStatus => !IsVisitRecord
            && !IsAutomaticallyGenerated
            && !string.IsNullOrWhiteSpace(AttendanceStatusText);

        [NotMapped]
        public string DisplayTime => AppointmentDateTime.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    }
}
