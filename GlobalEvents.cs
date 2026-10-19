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

        // حدث يتم إطلاقه عند تبديل المظهر (الوضع الفاتح / الداكن)
        public static event Action<bool>? OnThemeChanged;
        public static void NotifyThemeChanged(bool isDark) => OnThemeChanged?.Invoke(isDark);
    }
}