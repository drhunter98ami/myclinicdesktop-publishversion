using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.IO;

namespace MyClinic
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<AppointmentEntry> Appointments { get; set; }
        public DbSet<Patient> Patients { get; set; }
        public DbSet<Visit> Visits { get; set; }
        public DbSet<ExpenseEntry> Expenses { get; set; }
        public DbSet<ToothRecord> ToothRecords { get; set; }
        
        // تمت إضافة الجدول الجديد هنا
        public DbSet<UpcomingPayment> UpcomingPayments { get; set; }
        public DbSet<LabWork> LabWorks { get; set; }
        public DbSet<Shortage> Shortages { get; set; }
        public DbSet<AppSettings> AppSettings { get; set; }
        public DbSet<LabName> LabNames { get; set; }
        public DbSet<TreatmentCost> TreatmentCosts { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            string dbPath;

            // Check if running in development mode (dotnet run)
#if DEBUG
            // Use the project DevData database regardless of the process working
            // directory. This keeps settings persistent when the app is reopened
            // from a different shortcut, IDE, or output folder.
            string projectFolder = FindDevelopmentProjectFolder(AppContext.BaseDirectory);
            string devDbFolder = Path.Combine(projectFolder, "DevData");
            if (!Directory.Exists(devDbFolder))
            {
                Directory.CreateDirectory(devDbFolder);
            }
            dbPath = Path.Combine(devDbFolder, "ClinicData_Dev.db");
            #else
            // Production: Use LocalAppData for installed app
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
            if (!Directory.Exists(clinicFolder))
            {
                Directory.CreateDirectory(clinicFolder);
            }
            dbPath = Path.Combine(clinicFolder, "ClinicData.db");
            #endif

            optionsBuilder.UseSqlite($"Data Source={dbPath};Foreign Keys=True");
        }

#if DEBUG
        private static string FindDevelopmentProjectFolder(string startDirectory)
        {
            DirectoryInfo? current = new DirectoryInfo(startDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "DevData")))
                    return current.FullName;
                current = current.Parent;
            }

            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyClinicApp", "DevData");
            Directory.CreateDirectory(fallback);
            return Directory.GetParent(fallback)!.FullName;
        }
#endif

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // التعديل هنا: تم تغيير IsUnique إلى false 
            // هذا يسمح لأكثر من مريض (مثل أفراد العائلة) بمشاركة نفس رقم الهاتف
            // مع إبقاء الفهرس (Index) لتسريع عملية البحث التلقائي (Auto-fill)
            modelBuilder.Entity<Patient>()
                .HasIndex(p => p.PhoneNumber)
                .IsUnique(false);

            // Patient → Visits  (cascade delete: removing a patient removes their visits)
            modelBuilder.Entity<Patient>()
                .HasMany(p => p.Visits)
                .WithOne(v => v.Patient)
                .HasForeignKey(v => v.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            // Visit → ToothRecords  (cascade delete)
            modelBuilder.Entity<Visit>()
                .HasMany(v => v.ToothRecords)
                .WithOne(t => t.Visit)
                .HasForeignKey(t => t.VisitId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
