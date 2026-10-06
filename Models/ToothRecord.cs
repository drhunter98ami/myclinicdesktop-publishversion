using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyClinic.Models
{
    /// <summary>
    /// Stores the status/condition selected for a single tooth in a visit's dental chart.
    /// </summary>
    public class ToothRecord
    {
        [Key]
        public int Id { get; set; }

        // ── FK to Visit ────────────────────────────────────────────────
        [ForeignKey(nameof(Visit))]
        public int VisitId { get; set; }
        public Visit Visit { get; set; } = null!;

        /// <summary>
        /// The tooth identifier as used by your dental chart controls.
        /// e.g. "18", "21", "55" (FDI notation) or whatever your control exposes.
        /// </summary>
        [Required]
        public string ToothId { get; set; } = string.Empty;

        /// <summary>
        /// The condition/status string selected on the chart,
        /// e.g. "Healthy", "Caries", "Missing", "Crowned", "RCT", etc.
        /// </summary>
        public string? Condition { get; set; }

        /// <summary>Optional free-text note for this specific tooth.</summary>
        public string? Notes { get; set; }

        public int? TreatmentId { get; set; }
        public string? TreatmentName { get; set; }
        public int CanalCount { get; set; }
        public string? CanalsJson { get; set; }
    }
}
