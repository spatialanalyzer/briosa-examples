using System.Security;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Identity = Inspection.Bootstrap.BootstrapIdentity;

namespace Inspection.Bootstrap;

internal static class InstalledServerDiscovery
{
    private const string RegistryPath = @"Software\Briosa\Installations";
    private static readonly string[] RequiredFiles = ["manifest.json", "Briosa.Server.exe", "Briosa.Worker.exe"];

    internal static BriosaDiscoveryReport Discover(BriosaServerSelection options)
    {
        options.Validate();
        if (!OperatingSystem.IsWindows())
            return new([], [], null, "server-platform-unsupported");
        var candidates = new List<BriosaInstallation>();
        var diagnostics = new List<BriosaDiscoveryDiagnostic>();
        var elevated = IsElevated();
        var explicitPath = options.ExecutablePath;
        if (explicitPath is null && options.InstallationId is null && options.UseLegacyEnvironmentOverride)
            explicitPath = Environment.GetEnvironmentVariable("BRIOSA_SERVER_PATH");
        if (explicitPath is not null)
        {
            try
            {
                candidates.Add(ReadExecutable(explicitPath, "portable"));
                return Report(candidates, diagnostics, options with { ExecutablePath = explicitPath });
            }
            catch (Exception exception) when (ReadFailure(exception) || exception is IOException)
            {
                return new([], [new(explicitPath, "server-installation-invalid")], null, "server-installation-invalid");
            }
        }

        foreach (var (directory, scope, hint) in Registered(diagnostics, elevated))
            Add(Path.Combine(directory, "payload", "Briosa.Server.exe"), scope, hint);

        var user = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var machine = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        foreach (var (store, scope) in new[]
        {
            (Path.Combine(user, "Briosa", "Packages"), "user"),
            (Path.Combine(machine, "Briosa", "Packages"), "machine")
        }.Concat(options.SearchRoots.Select(path => (path, "portable"))))
        {
            if (!Path.IsPathFullyQualified(store) || elevated && scope != "machine" || !options.AllowedScopes.Contains(scope))
                continue;
            try
            {
                NoLinks(store);
                var products = Path.Combine(store, "products");
                if (!Directory.Exists(products)) continue;
                NoLinks(products);
                foreach (var product in Directory.EnumerateDirectories(products).Take(1000))
                    Add(Path.Combine(product, "payload", "Briosa.Server.exe"), scope, null);
            }
            catch (Exception exception) when (ReadFailure(exception))
            {
                diagnostics.Add(new(store, "server-store-unavailable"));
            }
        }

        if (!elevated && options.AllowedScopes.Contains("portable"))
        {
            AddIfPresent(Path.Combine(AppContext.BaseDirectory, "briosa-server", "Briosa.Server.exe"));
            if (Path.IsPathFullyQualified(user))
                AddIfPresent(Path.Combine(user, "Briosa", "servers", ServerSelectionPolicy.LegacyVersion,
                    "sa-" + Identity.SpatialAnalyzerTarget, "Briosa.Server.exe"));
        }
        return Report(candidates, diagnostics, options);

        void AddIfPresent(string path)
        {
            if (File.Exists(path)) Add(path, "portable", null);
        }

        void Add(string path, string scope, string? hint)
        {
            if (!options.AllowedScopes.Contains(scope) || elevated && scope != "machine") return;
            try
            {
                var candidate = ReadExecutable(path, scope);
                if (hint is not null)
                {
                    using var document = JsonDocument.Parse(hint);
                    var value = document.RootElement;
                    if (Text(value, "installationId") != candidate.InstallationId ||
                        Text(value, "serverVersion") != candidate.Version ||
                        Text(value, "spatialAnalyzerTarget") != candidate.SpatialAnalyzerTarget ||
                        Text(value, "runtimeIdentifier") != candidate.RuntimeIdentifier ||
                        Text(value, "packageId") != ArtifactName(candidate.Version, candidate.SpatialAnalyzerTarget, candidate.RuntimeIdentifier))
                        throw new IOException("Invalid installation registration.");
                }
                if (elevated && OperatingSystem.IsWindows() && !ProtectedInstallation(candidate.ExecutablePath)) return;
                if (!candidates.Any(c => ServerSelectionPolicy.PathKey(c.ExecutablePath) == ServerSelectionPolicy.PathKey(candidate.ExecutablePath)))
                    candidates.Add(candidate);
            }
            catch (Exception exception) when (ReadFailure(exception) || exception is IOException)
            {
                diagnostics.Add(new(path, "server-installation-invalid"));
            }
        }
    }

    private static BriosaDiscoveryReport Report(List<BriosaInstallation> candidates,
        List<BriosaDiscoveryDiagnostic> diagnostics, BriosaServerSelection options)
    {
        var (selected, code) = ServerSelectionPolicy.Select(candidates, options);
        foreach (var candidate in candidates)
        {
            var (_, rejected) = ServerSelectionPolicy.Select([candidate], options);
            if (rejected is not null) diagnostics.Add(new(candidate.ExecutablePath, rejected));
        }
        if (selected is null && candidates.Count == 0 && diagnostics.Count > 0)
            code = "server-installation-invalid";
        return new(candidates.AsReadOnly(), diagnostics.AsReadOnly(), selected, code);
    }

    internal static BriosaInstallation ReadExecutable(string path, string scope)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(path), "Briosa.Server.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid executable path.");
        path = Path.GetFullPath(path);
        NoLinks(path);
        if (!File.Exists(path)) throw new FileNotFoundException();
        var payload = Path.GetDirectoryName(path)!;
        var product = Path.GetDirectoryName(payload)!;
        var receiptPath = Path.Combine(product, "receipt.json");
        var managed = string.Equals(Path.GetFileName(payload), "payload", StringComparison.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(payload, "manifest.json");
        var manifestBytes = ReadBounded(manifestPath, 1024 * 1024);
        using var manifestDocument = Parse(manifestBytes);
        var manifest = manifestDocument.RootElement;
        var schema = UInt(manifest, "schemaVersion");
        var version = Text(manifest, "briosaVersion");
        var target = Text(manifest, "spatialAnalyzerTarget");
        var rid = Text(manifest, "runtimeIdentifier");
        var source = Text(manifest, "sourceRevision");
        var artifact = ArtifactName(version, target, rid);
        if (schema is not (2 or 3) || !ServerReleaseVersion.IsValid(version) || !Hex(source, 40) ||
            target.Length is < 1 or > 64 || rid.Length is < 1 or > 64 ||
            Text(manifest, "artifactName") != artifact || Text(manifest, "protocolPackage") != "briosa" ||
            Property(manifest, "spatialAnalyzerBundled").ValueKind != JsonValueKind.False)
            throw new IOException("Invalid server manifest.");
        uint major = 0, revision = 0;
        if (schema == 3)
        {
            var contract = Property(manifest, "compatibility");
            major = UInt(contract, "major");
            revision = UInt(contract, "revision");
            if (major == 0) throw new IOException("Missing compatibility major.");
        }
        var manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        foreach (var name in RequiredFiles)
        {
            var file = Path.Combine(payload, name);
            NoLinks(file);
            if (!File.Exists(file)) throw new FileNotFoundException();
        }
        if (managed)
        {
            if (Path.GetFileName(product) != artifact ||
                !string.Equals(Path.GetFileName(Path.GetDirectoryName(product)), "products", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Not a committed product location.");
            using var receiptDocument = Parse(ReadBounded(receiptPath, 8 * 1024 * 1024));
            var receipt = receiptDocument.RootElement;
            var package = Property(receipt, "package");
            if (UInt(receipt, "schemaVersion") != 1 || Text(package, "id") != artifact ||
                Text(package, "component") != "server" || Text(package, "version") != version ||
                Text(package, "spatialAnalyzerTarget") != target || Text(package, "runtimeIdentifier") != rid)
                throw new IOException("Receipt identity mismatch.");
            var files = Property(receipt, "files");
            foreach (var name in RequiredFiles)
                if (!Hex(Text(files, name), 64)) throw new IOException("Missing file evidence.");
            if (Text(files, "manifest.json") != manifestHash) throw new IOException("Manifest receipt digest mismatch.");
        }
        return new(IdFor(managed ? product : payload), path, version, source, target, rid,
            major, revision, manifestHash, scope);
    }

    internal static string IdFor(string directory)
    {
        var path = Path.GetFullPath(directory).Replace('\\', '/').TrimEnd('/');
        var normalized = new string(path.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c).ToArray());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static List<(string Directory, string Scope, string Json)> Registered(
        List<BriosaDiscoveryDiagnostic> diagnostics, bool elevated)
    {
        var result = new List<(string, string, string)>();
        if (!OperatingSystem.IsWindows()) return result;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            if (elevated && hive == RegistryHive.CurrentUser) continue;
            var scope = hive == RegistryHive.LocalMachine ? "machine" : "user";
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var root = baseKey.OpenSubKey(RegistryPath);
                if (root is null) continue;
                foreach (var id in root.GetSubKeyNames().Take(1000))
                {
                    try
                    {
                        using var key = root.OpenSubKey(id);
                        if (key is null || key.GetValueKind("Registration") != RegistryValueKind.String ||
                            key.GetValue("Registration", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string json ||
                            json.Length > 32768) continue;
                        using var document = Parse(Encoding.UTF8.GetBytes(json));
                        var value = document.RootElement;
                        var directory = Text(value, "productDirectory");
                        if (UInt(value, "schemaVersion") == 1 && Path.IsPathFullyQualified(directory) &&
                            id == Text(value, "installationId") && id == IdFor(directory))
                            result.Add((directory, scope, json));
                    }
                    catch (Exception exception) when (ReadFailure(exception))
                    {
                        diagnostics.Add(new(scope + ":" + id, "server-registration-invalid"));
                    }
                }
            }
            catch (Exception exception) when (ReadFailure(exception))
            {
                diagnostics.Add(new(scope, "server-registration-unavailable"));
            }
        }
        return result;
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return identity.IsSystem || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [SupportedOSPlatform("windows")]
    private static bool ProtectedInstallation(string executable)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var payload = Path.GetDirectoryName(executable)!;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(payload));
        var count = 0;
        while (pending.TryPop(out var current))
        {
            foreach (var item in current.EnumerateFileSystemInfos())
            {
                if (++count > 10000 || (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                var security = item is DirectoryInfo folder ? (FileSystemSecurity)folder.GetAccessControl() : ((FileInfo)item).GetAccessControl();
                if (!InstallationProtection.Allows(security, true)) return false;
                if (item is DirectoryInfo directory) pending.Push(directory);
            }
        }
        for (var directory = new DirectoryInfo(payload); directory.Parent is not null; directory = directory.Parent)
            if (!InstallationProtection.Allows(directory.GetAccessControl(), directory.FullName == payload)) return false;
        return true;
    }

    private static string ArtifactName(string version, string target, string rid) => $"briosa-{version}-sa-{target}-{rid}";
    private static bool Hex(string value, int length) => value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
    private static string Text(JsonElement value, string name) =>
        Property(value, name) is { ValueKind: JsonValueKind.String } property ? property.GetString()! : "";
    private static uint UInt(JsonElement value, string name)
    {
        var property = Property(value, name);
        return property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out var number)
            ? number : throw new IOException("Invalid numeric metadata.");
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        NoLinks(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new IOException("Metadata size limit exceeded.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try { Unique(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new IOException("Duplicate metadata property.");
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Unique(item);
    }

    private static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked installations are not eligible.");
        }
    }

    private static bool ReadFailure(Exception exception) => exception is IOException or
        UnauthorizedAccessException or SecurityException or JsonException or ArgumentException or NotSupportedException;
}
