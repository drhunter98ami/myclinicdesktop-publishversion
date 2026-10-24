using Microsoft.EntityFrameworkCore;
using System.Data;

namespace MyClinic;

public static class ClinicWorkspaceSchema
{
    public static void Ensure(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        bool opened = connection.State != ConnectionState.Open;
        if (opened) connection.Open();
        try
        {
            using var tx = connection.BeginTransaction();
            bool Add(string table, string column, string definition)
            {
                using var inspect = connection.CreateCommand(); inspect.Transaction = tx;
                inspect.CommandText = $"PRAGMA table_info({table})";
                using (var reader = inspect.ExecuteReader())
                    while (reader.Read()) if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase)) return false;
                using var alter = connection.CreateCommand(); alter.Transaction = tx;
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}"; alter.ExecuteNonQuery(); return true;
            }
            bool prices = Add("Shortages", "PriceKnown", "INTEGER NOT NULL DEFAULT 1");
            Add("Shortages", "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
            Add("LabNames", "Phone", "TEXT NOT NULL DEFAULT ''"); Add("LabNames", "IsArchived", "INTEGER NOT NULL DEFAULT 0");
            Add("LabWorks", "PatientId", "INTEGER NULL"); Add("LabWorks", "LabNameId", "INTEGER NULL");
            Add("LabWorks", "WorkType", "TEXT NOT NULL DEFAULT ''");
            bool backfill = Add("LabWorks", "WorkStatus", "TEXT NOT NULL DEFAULT 'تم الإرسال'");
            Add("LabWorks", "Currency", "TEXT NOT NULL DEFAULT 'USD'");
            Add("LabWorks", "ExpectedDelivery", "TEXT NULL");
            Add("LabWorks", "RateSnapshot", "TEXT NOT NULL DEFAULT '15000'"); Add("LabWorks", "Revision", "INTEGER NOT NULL DEFAULT 0");
            bool treatments = Add("TreatmentCosts", "RequiresCanals", "INTEGER NOT NULL DEFAULT 0");
            using var command = connection.CreateCommand(); command.Transaction = tx;
            command.CommandText = @"CREATE TABLE IF NOT EXISTS ClinicOperations (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, RequestKey TEXT NOT NULL,
                Kind TEXT NOT NULL, SourceId INTEGER NOT NULL, Description TEXT NOT NULL,
                Quantity INTEGER NOT NULL, Amount TEXT NOT NULL, Currency TEXT NOT NULL,
                RateSnapshot TEXT NOT NULL, AmountSyp TEXT NOT NULL, OccurredAt TEXT NOT NULL,
                ExpenseId INTEGER NULL, Actor TEXT NOT NULL);
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ClinicOperations_RequestKey ON ClinicOperations(RequestKey);
                CREATE INDEX IF NOT EXISTS IX_ClinicOperations_Kind_SourceId ON ClinicOperations(Kind, SourceId);";
            command.ExecuteNonQuery();
            if (prices) { command.CommandText = "UPDATE Shortages SET PriceKnown = 0 WHERE CAST(Price AS REAL) = 0"; command.ExecuteNonQuery(); }
            if (backfill)
            {
                command.CommandText = @"UPDATE LabWorks SET WorkStatus = CASE WHEN DateReceived IS NOT NULL OR Status IN ('تم الإستلام','تم الاستلام','تم الدفع') THEN 'تم الاستلام' ELSE 'تم الإرسال' END;
                    UPDATE LabWorks SET AmountPaid = Cost WHERE CAST(AmountPaid AS REAL) = 0 AND Status IN ('تم الدفع','تم الدفع ولم يتم الاستلام');
                    UPDATE LabWorks SET RateSnapshot = COALESCE((SELECT UsdToSypRate FROM AppSettings LIMIT 1),15000);";
                command.ExecuteNonQuery();
            }
            tx.Commit();
            if (treatments)
            {
                foreach (var treatment in db.TreatmentCosts) treatment.RequiresCanals = Models.TreatmentNameMatcher.IsRootCanal(treatment.TreatmentName);
                db.SaveChanges();
            }
        }
        finally { if (opened) connection.Close(); }
    }
}
