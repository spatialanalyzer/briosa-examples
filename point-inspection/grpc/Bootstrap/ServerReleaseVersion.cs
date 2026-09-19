using System.Text.RegularExpressions;

namespace Inspection.Bootstrap;

internal static partial class ServerReleaseVersion
{
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static bool IsValid(string value) => value.Length <= 128 && Pattern().IsMatch(value);

    internal static int Compare(string left, string right)
    {
        var a = left.Split('+')[0].Split('-', 2);
        var b = right.Split('+')[0].Split('-', 2);
        var ac = a[0].Split('.');
        var bc = b[0].Split('.');
        for (var i = 0; i < 3; i++)
        {
            var result = Number(ac[i], bc[i]);
            if (result != 0) return result;
        }
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        var ap = a[1].Split('.');
        var bp = b[1].Split('.');
        for (var i = 0; i < Math.Min(ap.Length, bp.Length); i++)
        {
            var an = ap[i].All(char.IsAsciiDigit);
            var bn = bp[i].All(char.IsAsciiDigit);
            var result = an && bn ? Number(ap[i], bp[i]) :
                an != bn ? (an ? -1 : 1) : string.CompareOrdinal(ap[i], bp[i]);
            if (result != 0) return result;
        }
        return ap.Length.CompareTo(bp.Length);
    }

    private static int Number(string left, string right) =>
        left.Length == right.Length ? string.CompareOrdinal(left, right) : left.Length.CompareTo(right.Length);
}
