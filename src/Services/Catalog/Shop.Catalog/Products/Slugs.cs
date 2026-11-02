using System.Globalization;
using System.Text;

namespace Shop.Catalog.Products;

public static class Slugs
{
    private static readonly Dictionary<char, string> Turkish = new()
    {
        ['ı'] = "i", ['İ'] = "i", ['ş'] = "s", ['Ş'] = "s", ['ğ'] = "g", ['Ğ'] = "g",
        ['ü'] = "u", ['Ü'] = "u", ['ö'] = "o", ['Ö'] = "o", ['ç'] = "c", ['Ç'] = "c",
    };

    /// <summary>"Filtre Kahve Öğütücü" → "filtre-kahve-ogutucu".</summary>
    public static string From(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in (text ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            if (Turkish.TryGetValue(c, out var replacement))
            {
                builder.Append(replacement);
                continue;
            }

            foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.IsAsciiLetterOrDigit(d) ? char.ToLowerInvariant(d) : '-');
                }
            }
        }

        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length <= 80 ? slug : slug[..80].TrimEnd('-');
    }
}
