using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyClinic.Models
{
    public class VisitEditHistory
    {
        [Key]
        public int Id { get; set; }

        public int VisitId { get; set; }
        public DateTime EditedAt { get; set; }
        public string? EditedBy { get; set; }
        public string ChangesJson { get; set; } = "[]";

        [ForeignKey(nameof(VisitId))]
        public Visit Visit { get; set; } = null!;
    }

    public sealed class VisitEditChange
    {
        public string Field { get; set; } = string.Empty;
        public string Before { get; set; } = string.Empty;
        public string After { get; set; } = string.Empty;
    }
}
