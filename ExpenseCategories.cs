using System;
using System.Collections.Generic;
using System.Linq;

namespace MyClinic
{
    internal static class ExpenseCategories
    {
        private static readonly (string Keyword, string Label)[] KeywordCategories =
        {
            ("مخبر", "مخبر"),
            ("نواقص", "نواقص"),
            ("خزان", "خزان"),
            ("صيانة", "صيانة"),
            ("أجار", "أجار"),
            ("أمبير", "أمبير"),
            ("كهربا", "كهربا"),
            ("ممرضة", "ممرضة")
        };

        internal const string OtherLabel = "أخرى";

        internal static IReadOnlyList<string> Labels { get; } =
            KeywordCategories.Select(category => category.Label).Append(OtherLabel).ToArray();

        internal static bool IsCategory(string category) =>
            Labels.Contains(category, StringComparer.Ordinal);

        internal static string FormatDescription(string category, string details)
        {
            string trimmedDetails = details.Trim();
            return trimmedDetails.Length == 0 ? category : $"{category} - {trimmedDetails}";
        }

        internal static string Classify(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return OtherLabel;

            string trimmedDescription = description.TrimStart();
            foreach (string category in Labels)
            {
                if (trimmedDescription.StartsWith(category, StringComparison.OrdinalIgnoreCase)
                    && (trimmedDescription.Length == category.Length
                        || char.IsWhiteSpace(trimmedDescription[category.Length])
                        || trimmedDescription[category.Length] is '-' or '–' or ':' or '،'))
                {
                    return category;
                }
            }

            foreach (var (keyword, label) in KeywordCategories)
            {
                if (description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return label;
            }

            return OtherLabel;
        }
    }
}
