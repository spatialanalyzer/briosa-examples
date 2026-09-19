using System.Text;
using Identity = Inspection.Bootstrap.BootstrapIdentity;

namespace Inspection.Bootstrap;

internal static class ServerSelectionPolicy
{
    internal static string PathKey(string value)
    {
        var path = Path.GetFullPath(value).Replace('\\', '/').TrimEnd('/');
        return new string(path.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c).ToArray());
    }
    private static int ComparePaths(string left, string right) =>
        Encoding.UTF8.GetBytes(PathKey(left)).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(PathKey(right)));
    internal const string LegacyVersion = "0.6.1";
    internal const string LegacyRevision = "32a3b56ba4ae31ea5ec6ec3b2aa051eb61c866aa";

    internal static bool Compatible(uint major, uint revision, string version, string source,
        uint requiredMajor = Identity.CompatibilityMajor, uint minimumRevision = Identity.CompatibilityRevision) =>
        major == requiredMajor && major > 0 && revision >= minimumRevision ||
        major == 0 && revision == 0 && requiredMajor == 1 && minimumRevision == 0 &&
        version == LegacyVersion && source == LegacyRevision;

    internal static (BriosaInstallation? Selected, string? Code) Select(
        IReadOnlyList<BriosaInstallation> candidates, BriosaServerSelection options,
        string target = Identity.SpatialAnalyzerTarget,
        uint requiredMajor = Identity.CompatibilityMajor, uint minimumRevision = Identity.CompatibilityRevision)
    {
        var eligible = candidates.Where(c =>
            c.SpatialAnalyzerTarget == target && c.RuntimeIdentifier == "win-x64" &&
            Compatible(c.ContractMajor, c.ContractRevision, c.Version, c.SourceRevision, requiredMajor, minimumRevision) &&
            (options.Version is null || c.Version == options.Version) &&
            (options.InstallationId is null || c.InstallationId == options.InstallationId) &&
            (options.ExecutablePath is null || PathKey(c.ExecutablePath) == PathKey(options.ExecutablePath)) &&
            options.AllowedScopes.Contains(c.Scope) &&
            (options.AllowPrerelease || !c.Version.Split('+')[0].Contains('-', StringComparison.Ordinal)) &&
            !options.ExcludedVersions.Contains(c.Version) &&
            (options.MinimumVersion is null || ServerReleaseVersion.Compare(c.Version, options.MinimumVersion) >= 0) &&
            (options.MaximumVersionExclusive is null || ServerReleaseVersion.Compare(c.Version, options.MaximumVersionExclusive) < 0))
            .OrderByDescending(c => c.Version, Comparer<string>.Create(ServerReleaseVersion.Compare))
            .ThenBy(c => c.Scope == "machine" ? 0 : c.Scope == "user" ? 1 : 2)
            .ThenBy(c => c.ExecutablePath, Comparer<string>.Create(ComparePaths)).ToArray();
        if (eligible.Length == 0)
            return (null, candidates.Count == 0 ? "server-distribution-not-found" : "server-installation-incompatible");
        var selected = eligible[0];
        if (eligible.Any(c => ServerReleaseVersion.Compare(c.Version, selected.Version) == 0 &&
            (c.ManifestSha256 != selected.ManifestSha256 || c.SourceRevision != selected.SourceRevision)))
            return (null, "server-installation-ambiguous");
        return (selected, null);
    }
}
