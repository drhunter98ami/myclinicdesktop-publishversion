using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyClinic.Models
{
    /// <summary>
    /// Represents a single clinic visit for a patient.
    /// All per-visit clinical data is stored here.
    /// </summary>
    public class Visit
    {
        [Key]
        public int Id { get; set; }

        public DateTime VisitDate { get; set; } = DateTime.Now;

        // ── FK to Patient ──────────────────────────────────────────────
        [ForeignKey(nameof(Patient))]
        public int PatientId { get; set; }
        public Patient Patient { get; set; } = null!;

        // ── Reproductive Health (females only) ────────────────────────
        public bool IsPregnant { get; set; }
        public bool IsNursing { get; set; }

        /// <summary>1-9 representing pregnancy month</summary>
        public int? PregnancyMonth { get; set; }

        // ── Vital Signs ────────────────────────────────────────────────
        public string? BloodPressure { get; set; }   // e.g. "120/80"
        public string? HeartRate { get; set; }        // e.g. "72"
        public string? Temperature { get; set; }      // e.g. "37.0"
        public string? RespiratoryRate { get; set; }  // e.g. "16"
        public string? Weight { get; set; }           // e.g. "70"
        public string? Height { get; set; }           // e.g. "170"
        public string? BloodSugar { get; set; }       // e.g. "95"

        // ── Clinical Notes ─────────────────────────────────────────────
        public string? Symptoms { get; set; }
        public string? Diagnosis { get; set; }
        public string? PrescriptionJson { get; set; }
        public string? TreatmentPlanNotes { get; set; }
        public string? FinalTreatment { get; set; }
        public string? AttachedImagePathsJson { get; set; }
        public string? SelectedTreatmentsJson { get; set; }

        // ── Financials ────────────────────────────────────────────────
        public double CurrentCost { get; set; }
        public double TodayPaid { get; set; }
        public double RemainingAmount { get; set; }

        /// <summary>
        /// The USD→SYP exchange rate in effect when this visit was saved.
        /// Snapshotted so later rate changes do not retroactively alter historic costs.
        /// </summary>
        public double UsdToSypRateSnapshot { get; set; } = 15000;

        // ── Dental Chart ──────────────────────────────────────────────
        /// <summary>"Adult" or "Child"</summary>
        public string ChartMode { get; set; } = "Adult";

        public ICollection<ToothRecord> ToothRecords { get; set; } = new List<ToothRecord>();
    }
}
