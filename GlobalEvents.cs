using System;

namespace MyClinic
{
    public static class GlobalEvents
    {
        // حدث يتم إطلاقه عند إضافة أي دفعة مالية جديدة
        public static event Action? OnFinancialRecordAdded;
        public static void NotifyFinancialRecordAdded()
        {
            OnFinancialRecordAdded?.Invoke();
        }

        // --- الكود الجديد ---
        // حدث يتم إطلاقه عند إضافة مريض جديد أو زيارة جديدة
        public static event Action? OnPatientRecordAdded;
        public static void NotifyPatientRecordAdded()
        {
            OnPatientRecordAdded?.Invoke();
        }

        // حدث يتم إطلاقه عند تغيير سعر الصرف
        public static event Action? OnExchangeRateChanged;
        public static void NotifyExchangeRateChanged()
        {
            OnExchangeRateChanged?.Invoke();
        }

        public static event Action? OnFileKitCanalCountChanged;
        public static void NotifyFileKitCanalCountChanged() => OnFileKitCanalCountChanged?.Invoke();
    }
}
