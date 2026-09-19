namespace Inspection.Bootstrap;

// Example-specific bootstrap inputs. Public protobuf names remain target-neutral.
internal static class BootstrapIdentity
{
    internal const string SpatialAnalyzerTarget = "2026.1.0529.7";
    internal const uint CompatibilityMajor = 1;
    internal const uint CompatibilityRevision = 0;
}

internal sealed class BriosaStartupException(string code) : IOException(code);
