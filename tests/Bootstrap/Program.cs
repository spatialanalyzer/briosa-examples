using System.Security.AccessControl;
using System.Text.Json;
using Inspection.Bootstrap;

using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "selection-cases.json")));
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var count = 0;
foreach (var test in document.RootElement.GetProperty("cases").EnumerateArray())
{
    var options = test.GetProperty("options").Deserialize<BriosaServerSelection>(jsonOptions)!;
    options.Validate();
    var candidates = test.GetProperty("candidates").EnumerateArray().Select(c => new BriosaInstallation(
        Text(c, "id")!, Text(c, "path")!, Text(c, "version")!, Text(c, "sourceRevision")!,
        Text(c, "target")!, Text(c, "rid")!, c.GetProperty("major").GetUInt32(),
        c.GetProperty("revision").GetUInt32(), Text(c, "manifestSha256")!, Text(c, "scope")!)).ToArray();
    var (selected, code) = ServerSelectionPolicy.Select(candidates, options, Text(test, "target")!,
        test.GetProperty("requiredMajor").GetUInt32(), test.GetProperty("minimumRevision").GetUInt32());
    if (selected?.InstallationId != Text(test, "selectedId") || code != Text(test, "error"))
        throw new Exception($"Selection vector failed: {Text(test, "name")}");
    count++;
}
var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Briosa.Server.exe");
var report = BriosaInstallations.Discover(new() { ExecutablePath = missing });
if (report.Selected is not null || report.DiagnosticCode != "server-installation-invalid")
    throw new Exception("An invalid explicit selection must not fall back.");
if (OperatingSystem.IsWindows())
{
    (string Sddl, bool IncludeWrite, bool Expected)[] permissions =
    [
        ("O:SYG:SY", true, false),
        ("O:SYG:SYD:(A;;FA;;;SY)(A;;FA;;;BA)", true, true),
        ("O:SYG:SYD:(A;;FA;;;SY)(A;;FW;;;BU)", false, true),
        ("O:SYG:SYD:(A;;FA;;;SY)(A;;FW;;;BU)", true, false),
        ("O:SYG:SYD:(A;;FA;;;SY)(A;;0x40;;;BU)", false, false),
        ("O:SYG:SYD:(A;;GA;;;BU)", false, false),
        ("O:SYG:SYD:(A;CIIO;GA;;;BU)", true, true),
        ("O:S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464G:SYD:(A;;FA;;;SY)", true, true),
        ("O:BUG:SYD:(A;;FA;;;SY)", true, false),
    ];
    foreach (var permission in permissions)
    {
        var acl = new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm(permission.Sddl);
        if (InstallationProtection.Allows(acl, permission.IncludeWrite) != permission.Expected)
            throw new Exception("Windows installation permission policy mismatch.");
    }
}
Console.WriteLine($"Passed {count} shared selection vectors, Windows permission checks, and explicit missing-path isolation.");
static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.GetString() : null;
