using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    /// <summary>
    /// Represents a unique patient. One patient can have many visits.
    /// The phone number acts as the natural unique key.
    /// </summary>
    public class Patient
    {
        [Key]
        public int Id { get; set; }

        // ── Personal Info ──────────────────────────────────────────────
        [Required]
        public string PhoneNumber { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public int? Age { get; set; }

        /// <summary>"ذكر" or "أنثى"</summary>
        public string? Gender { get; set; }

        public string? BloodType { get; set; }

        // ── Medical Background (stable per patient) ────────────────────
        public bool IsSmoker { get; set; }
        public string? SmokingType { get; set; }
        public string? SmokingFrequency { get; set; }

        public string? Allergies { get; set; }
        public string? ChronicDiseases { get; set; }

        // ── Navigation ─────────────────────────────────────────────────
        public ICollection<Visit> Visits { get; set; } = new List<Visit>();
    }
}