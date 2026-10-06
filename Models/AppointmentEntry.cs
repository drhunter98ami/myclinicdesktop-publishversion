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

        // Display-only fallback entry created from a saved visit when older
        // data does not have a matching row in Appointments.
        [NotMapped]
        public bool IsVisitRecord { get; set; }

        [NotMapped]
        public string DisplayTime => AppointmentDateTime.ToString("hh:mm tt", CultureInfo.InvariantCulture);
    }
}
