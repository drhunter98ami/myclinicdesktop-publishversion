using MyClinic.Models;
using System.Collections.Generic;
using System.Linq;

namespace MyClinic
{
    public static class AppRuntimeCache
    {
        private static readonly Dictionary<int, Visit> RecentVisitsById = new();
        private static readonly object SyncRoot = new();

        public static IReadOnlyCollection<Visit> RecentVisits
        {
            get
            {
                lock (SyncRoot)
                    return RecentVisitsById.Values.ToList();
            }
        }

        public static void AddOrUpdateVisit(Visit visit)
        {
            Visit snapshot = CloneVisit(visit);
            lock (SyncRoot)
                RecentVisitsById[visit.Id] = snapshot;
        }

        public static void RemoveVisit(int visitId)
        {
            lock (SyncRoot)
                RecentVisitsById.Remove(visitId);
        }

        public static void RemovePatientVisits(int patientId)
        {
            lock (SyncRoot)
            {
                var ids = RecentVisitsById.Values.Where(v => v.PatientId == patientId).Select(v => v.Id).ToList();
                foreach (int id in ids)
                    RecentVisitsById.Remove(id);
            }
        }

        private static Visit CloneVisit(Visit source)
        {
            return new Visit
            {
                Id = source.Id,
                VisitDate = source.VisitDate,
                PatientId = source.PatientId,
                Patient = source.Patient is null
                    ? new Patient()
                    : new Patient
                    {
                        Id = source.Patient.Id,
                        PhoneNumber = source.Patient.PhoneNumber,
                        FullName = source.Patient.FullName,
                        Age = source.Patient.Age,
                        DateOfBirth = source.Patient.DateOfBirth,
                        BirthYear = source.Patient.BirthYear,
                        Gender = source.Patient.Gender,
                        BloodType = source.Patient.BloodType,
                        IsSmoker = source.Patient.IsSmoker,
                        SmokingType = source.Patient.SmokingType,
                        SmokingFrequency = source.Patient.SmokingFrequency,
                        Allergies = source.Patient.Allergies,
                        ChronicDiseases = source.Patient.ChronicDiseases,
                        IsDeleted = source.Patient.IsDeleted
                    },
                IsPregnant = source.IsPregnant,
                IsNursing = source.IsNursing,
                PregnancyMonth = source.PregnancyMonth,
                BloodPressure = source.BloodPressure,
                HeartRate = source.HeartRate,
                Temperature = source.Temperature,
                RespiratoryRate = source.RespiratoryRate,
                Weight = source.Weight,
                Height = source.Height,
                BloodSugar = source.BloodSugar,
                Symptoms = source.Symptoms,
                Diagnosis = source.Diagnosis,
                PrescriptionJson = source.PrescriptionJson,
                TreatmentPlanNotes = source.TreatmentPlanNotes,
                FinalTreatment = source.FinalTreatment,
                AttachedImagePathsJson = source.AttachedImagePathsJson,
                CurrentCost = source.CurrentCost,
                TodayPaid = source.TodayPaid,
                RemainingAmount = source.RemainingAmount,
                IsFinancialOnly = source.IsFinancialOnly,
                FinancialNote = source.FinancialNote,
                SelectedTreatmentsJson = source.SelectedTreatmentsJson,
                UsdToSypRateSnapshot = source.UsdToSypRateSnapshot,
                ChartMode = source.ChartMode,
                ToothRecords = source.ToothRecords?
                    .Select(record => new ToothRecord
                    {
                        Id = record.Id,
                        VisitId = record.VisitId,
                        ToothId = record.ToothId,
                        Condition = record.Condition,
                        Notes = record.Notes
                    })
                    .ToList()
                    ?? new List<ToothRecord>()
            };
        }
    }
}
