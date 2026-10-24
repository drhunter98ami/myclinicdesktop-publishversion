namespace MyClinic.Models
{
    public class UpcomingPayment
    {
        public int Id { get; set; }
        
        /// <summary>
        /// تفاصيل أو وصف الدفعة (لمن أو لأي غرض)
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// قيمة الدفعة
        /// </summary>
        public double Amount { get; set; }
        public System.DateTime? DueDate { get; set; }
        public string Recipient { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public System.DateTime? CompletedAt { get; set; }
        public int? ExpenseId { get; set; }
    }
}
