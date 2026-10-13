using System;
using System.ComponentModel.DataAnnotations;

namespace MyClinic.Models
{
    public class ExpenseEntry
    {
        [Key]
        public int Id { get; set; }

        public DateTime ExpenseDate { get; set; } = AppClock.Today;

        [Required]
        public string Description { get; set; } = string.Empty;

        public double Amount { get; set; }
    }
}
