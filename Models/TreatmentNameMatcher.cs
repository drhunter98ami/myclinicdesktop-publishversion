using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace MyClinic.Models
{
    public static class TreatmentNameMatcher
    {
        private static readonly Regex RootCanalAcronym = new(
            @"(?<![A-Za-z])RCT(?![A-Za-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsRootCanal(string? treatmentName)
        {
            if (string.IsNullOrWhiteSpace(treatmentName))
                return false;

            string normalized = new string(treatmentName
                .Where(character =>
                    !char.IsWhiteSpace(character) &&
                    !char.IsPunctuation(character) &&
                    character != '\u0640' &&
                    (character < '\u064B' || character > '\u065F') &&
                    character != '\u0670')
                .Select(char.ToLowerInvariant)
                .ToArray());

            return normalized.Contains("سحبعصب", StringComparison.Ordinal) ||
                   normalized.Contains("سحبالعصب", StringComparison.Ordinal) ||
                   normalized.Contains("معالجةلبية", StringComparison.Ordinal) ||
                   normalized.Contains("rootcanaltreatment", StringComparison.Ordinal) ||
                   RootCanalAcronym.IsMatch(treatmentName);
        }
    }
}
