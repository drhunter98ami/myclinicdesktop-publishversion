using Microsoft.EntityFrameworkCore;
using System.Data;

namespace MyClinic;

/// <summary>Idempotent upgrade for the installed app's EnsureCreated-based databases.</summary>
public static class FinancialSchema
{
    public static void Ensure(AppDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        bool opened = connection.State != ConnectionState.Open;
        if (opened) connection.Open();
        try
        {
            using var transaction = connection.BeginTransaction();
            void Add(string table, string column, string definition)
            {
                using var inspect = connection.CreateCommand();
                inspect.Transaction = transaction;
                inspect.CommandText = $"PRAGMA table_info({table})";
                bool exists = false;
                using (var reader = inspect.ExecuteReader())
                    while (reader.Read()) exists |= string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase);
                if (exists) return;
                using var alter = connection.CreateCommand();
                alter.Transaction = transaction;
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                alter.ExecuteNonQuery();
            }
            Add("Visits", "IsFinancialOnly", "INTEGER NOT NULL DEFAULT 0");
            Add("Visits", "FinancialNote", "TEXT NULL");
            Add("UpcomingPayments", "DueDate", "TEXT NULL");
            Add("UpcomingPayments", "Recipient", "TEXT NOT NULL DEFAULT ''");
            Add("UpcomingPayments", "Status", "TEXT NOT NULL DEFAULT 'Pending'");
            Add("UpcomingPayments", "CompletedAt", "TEXT NULL");
            Add("UpcomingPayments", "ExpenseId", "INTEGER NULL");
            using var create = connection.CreateCommand();
            create.Transaction = transaction;
            create.CommandText = @"CREATE TABLE IF NOT EXISTS FinancialAuditEntries (
                Id INTEGER NOT NULL CONSTRAINT PK_FinancialAuditEntries PRIMARY KEY AUTOINCREMENT,
                OccurredAt TEXT NOT NULL, Actor TEXT NOT NULL, Action TEXT NOT NULL,
                EntityType TEXT NOT NULL, EntityId INTEGER NOT NULL, Description TEXT NOT NULL,
                BeforeJson TEXT NOT NULL, AfterJson TEXT NOT NULL, Reason TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_FinancialAuditEntries_OccurredAt ON FinancialAuditEntries(OccurredAt);";
            create.ExecuteNonQuery();
            transaction.Commit();
        }
        finally { if (opened) connection.Close(); }
    }
}
