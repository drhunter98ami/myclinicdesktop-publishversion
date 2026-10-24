namespace MyClinic;

public sealed record LabWorkDetails(string PatientName, int? PatientId, int LabId, string WorkType,
    decimal Cost, string Currency, string Teeth, string Notes, DateTime? ExpectedDelivery,
    string Reason, decimal ConversionRate);
