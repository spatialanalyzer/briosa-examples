namespace Inspection.Bootstrap;

/// <summary>Constrains server discovery for one application startup.</summary>
public sealed record BriosaServerSelection
{
    /// <summary>Gets an absolute explicit server executable; invalid choices never fall back.</summary>
    public string? ExecutablePath { get; init; }
    /// <summary>Gets an explicit installation ID, mutually exclusive with ExecutablePath.</summary>
    public string? InstallationId { get; init; }
    /// <summary>Gets an exact server release constraint.</summary>
    public string? Version { get; init; }
    /// <summary>Gets an inclusive minimum server version.</summary>
    public string? MinimumVersion { get; init; }
    /// <summary>Gets an exclusive maximum server version.</summary>
    public string? MaximumVersionExclusive { get; init; }
    /// <summary>Gets explicitly excluded server versions.</summary>
    public IReadOnlyList<string> ExcludedVersions { get; init; } = [];
    /// <summary>Gets additional absolute package-store roots.</summary>
    public IReadOnlyList<string> SearchRoots { get; init; } = [];
    /// <summary>Gets allowed scopes: machine, user, and portable.</summary>
    public IReadOnlyList<string> AllowedScopes { get; init; } = ["machine", "user", "portable"];
    /// <summary>Gets whether prerelease servers are eligible.</summary>
    public bool AllowPrerelease { get; init; }
    /// <summary>Gets whether to honor BRIOSA_SERVER_PATH when no direct selector is set.</summary>
    public bool UseLegacyEnvironmentOverride { get; init; }
    /// <summary>Gets an optional absolute exact-target SA executable path.</summary>
    public string? SpatialAnalyzerExecutablePath { get; init; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(SearchRoots);
        ArgumentNullException.ThrowIfNull(ExcludedVersions);
        ArgumentNullException.ThrowIfNull(AllowedScopes);
        if (ExecutablePath is not null && InstallationId is not null)
            throw new ArgumentException("ExecutablePath and InstallationId are mutually exclusive.");
        foreach (var path in new[] { ExecutablePath, SpatialAnalyzerExecutablePath }.OfType<string>().Concat(SearchRoots))
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
                throw new ArgumentException("Selection paths must be absolute local paths.");
        foreach (var version in new[] { Version, MinimumVersion, MaximumVersionExclusive }.OfType<string>().Concat(ExcludedVersions))
            if (!ServerReleaseVersion.IsValid(version)) throw new ArgumentException("Invalid server version constraint.");
        if (InstallationId is not null && string.IsNullOrWhiteSpace(InstallationId))
            throw new ArgumentException("InstallationId must not be empty.");
        if (AllowedScopes.Count == 0 || AllowedScopes.Any(scope => scope is not ("machine" or "user" or "portable")))
            throw new ArgumentException("Invalid installation scope.");
    }

    internal BriosaServerSelection Snapshot() => this with
    {
        SearchRoots = SearchRoots.ToArray(),
        ExcludedVersions = ExcludedVersions.ToArray(),
        AllowedScopes = AllowedScopes.ToArray(),
    };
}

/// <summary>Describes validated local installation evidence, not SA runtime readiness.</summary>
/// <param name="InstallationId">Stable installation identity.</param>
/// <param name="ExecutablePath">Absolute server path.</param>
/// <param name="Version">Server release.</param>
/// <param name="SourceRevision">Server source revision.</param>
/// <param name="SpatialAnalyzerTarget">Exact SA target.</param>
/// <param name="RuntimeIdentifier">Executable architecture.</param>
/// <param name="ContractMajor">Behavioral major, or zero for legacy metadata.</param>
/// <param name="ContractRevision">Behavioral revision.</param>
/// <param name="ManifestSha256">Manifest content identity.</param>
/// <param name="Scope">Installation scope.</param>
public sealed record BriosaInstallation(
    string InstallationId, string ExecutablePath, string Version, string SourceRevision,
    string SpatialAnalyzerTarget, string RuntimeIdentifier, uint ContractMajor,
    uint ContractRevision, string ManifestSha256, string Scope);

/// <summary>Explains one rejected local discovery location; paths are explicitly requested data.</summary>
/// <param name="Path">Candidate location.</param>
/// <param name="Code">Value-free rejection reason.</param>
public sealed record BriosaDiscoveryDiagnostic(string Path, string Code);

/// <summary>Contains inert installation discovery results.</summary>
/// <param name="Installations">Structurally valid discovered installations.</param>
/// <param name="Diagnostics">Rejected or inaccessible locations.</param>
/// <param name="Selected">Eligible selected installation, if any.</param>
/// <param name="DiagnosticCode">Selection failure, if any.</param>
public sealed record BriosaDiscoveryReport(
    IReadOnlyList<BriosaInstallation> Installations,
    IReadOnlyList<BriosaDiscoveryDiagnostic> Diagnostics,
    BriosaInstallation? Selected, string? DiagnosticCode);

/// <summary>Discovers installed target-specific products without launching or contacting SA.</summary>
public static class BriosaInstallations
{
    /// <summary>Returns installation evidence and explains the current selection.</summary>
    public static BriosaDiscoveryReport Discover(BriosaServerSelection? selection = null) =>
        InstalledServerDiscovery.Discover(selection ?? new());

    /// <summary>Resolves a compatible installation or throws a typed startup failure.</summary>
    public static BriosaInstallation Resolve(BriosaServerSelection? selection = null)
    {
        var report = Discover(selection);
        return report.Selected ?? throw new BriosaStartupException(report.DiagnosticCode ?? "server-distribution-not-found");
    }
}
