using System.Globalization;
using System.Text;

namespace NzbDrone.Core.Plugins.UnwatchSingles.Matching;

/// <summary>Normalizes punctuation and spacing while retaining descriptive title words.</summary>
public static class TrackTitleNormalizer
{
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var normalized = title.Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        var previousWasSeparator = true;

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var isWordCharacter =
                Rune.IsLetterOrDigit(rune) ||
                category is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark;

            if (isWordCharacter)
            {
                result.Append(rune.ToString().ToLowerInvariant());
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                result.Append(' ');
                previousWasSeparator = true;
            }
        }

        return result.ToString().Trim();
    }
}
