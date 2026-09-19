using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Inspection;

public sealed record Point(string Name, double X, double Y, double Z, double Tolerance);
public sealed record SpanCheck(string First, string Second, double NominalMm, double ToleranceMm);
public sealed record Scenario(string SaTarget, string Collection, string Group,
    string FrameCollection, string Frame, string LengthUnit, SpanCheck[] Distances);
public sealed record Context(string LengthUnit, string FrameCollection, string Frame);
public sealed record Row(string Kind, string Id, double? XMm, double? YMm, double? ZMm,
    double? ActualMm, double? NominalMm, double DeviationMm, double ToleranceMm, string Status);
public sealed record Report(int SchemaVersion, string Source, string SaTarget, Context Context, Row[] Rows);

public interface IMeasurements : IAsyncDisposable
{
    Task<Context> ReadContext();
    Task<(double X, double Y, double Z)> ReadPoint(string name);
    Task<double> ReadDistance(string first, string second);
}

public static class Workbench
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public static readonly string[] RequiredMethods = [
        "/briosa.UtilityOperations/GetActiveUnits",
        "/briosa.UtilityOperations/GetWorkingFrameProperties",
        "/briosa.AnalysisOperations/GetPointCoordinate",
        "/briosa.AnalysisOperations/GetPointToPointDistance"];

    public static Dictionary<string, string> Options(string[] args)
    {
        var result = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "--live" or "--discover" or "--allow-prerelease") result.Add(key, "true");
            else if (key is "--fixture" or "--output" or "--endpoint" or "--server-path" or
                "--installation-id" or "--server-version" or "--search-root" or "--sa-path" && i + 1 < args.Length)
                result.Add(key, args[++i]);
            else throw new InvalidDataException("Use --live, --discover, --fixture, --output, --server-path, --installation-id, --server-version, --search-root, --sa-path, --allow-prerelease; --endpoint is raw gRPC only.");
        }
        return result;
    }

    public static Point[] LoadPoints(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2 || lines[0] != "name,x_mm,y_mm,z_mm,tolerance_mm")
            throw new InvalidDataException("Invalid fixture CSV header or empty fixture.");
        var names = new HashSet<string>();
        return lines.Skip(1).Select(line =>
        {
            var fields = line.Split(',');
            if (fields.Length != 5 || !Regex.IsMatch(fields[0], "^[A-Za-z][A-Za-z0-9_]*$") || !names.Add(fields[0]))
                throw new InvalidDataException("Expected unique simple point names and five CSV columns.");
            var values = fields.Skip(1).Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            if (values.Any(x => !double.IsFinite(x)) || values[3] < 0)
                throw new InvalidDataException("Coordinates must be finite and tolerances nonnegative.");
            return new Point(fields[0], values[0], values[1], values[2], values[3]);
        }).ToArray();
    }

    public static Scenario LoadScenario(string fixture, Point[] points)
    {
        var s = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(Path.Combine(fixture, "scenario.json")), Json)
            ?? throw new InvalidDataException("Missing scenario.");
        if (s.SaTarget != "2026.1.0529.7" || s.LengthUnit != "millimeters" ||
            new[] { s.Collection, s.Group, s.FrameCollection, s.Frame }.Any(string.IsNullOrWhiteSpace) || s.Distances is null)
            throw new InvalidDataException("This example requires the configured SA target, millimeters and an explicit frame.");
        var names = points.Select(p => p.Name).ToHashSet();
        foreach (var span in s.Distances)
            if (!names.Contains(span.First) || !names.Contains(span.Second) ||
                !double.IsFinite(span.NominalMm) || !double.IsFinite(span.ToleranceMm) || span.NominalMm < 0 || span.ToleranceMm < 0)
                throw new InvalidDataException("Invalid distance check.");
        return s;
    }

    public static void CheckContext(Context actual, Scenario s)
    {
        if (actual != new Context(s.LengthUnit, s.FrameCollection, s.Frame))
            throw new InvalidDataException("SA units or working frame do not match the fixture. No report was produced.");
    }

    public static async Task<Report> Inspect(IMeasurements source, Scenario s, Point[] nominals, string mode)
    {
        var context = await source.ReadContext();
        CheckContext(context, s);
        var rows = new List<Row>();
        foreach (var p in nominals)
        {
            var v = await source.ReadPoint(p.Name);
            var deviation = Math.Sqrt(Math.Pow(v.X - p.X, 2) + Math.Pow(v.Y - p.Y, 2) + Math.Pow(v.Z - p.Z, 2));
            if (!double.IsFinite(deviation)) throw new InvalidDataException("Invalid coordinate result.");
            rows.Add(new("point", p.Name, v.X, v.Y, v.Z, null, null, deviation, p.Tolerance,
                deviation <= p.Tolerance ? "PASS" : "FAIL"));
        }
        foreach (var span in s.Distances)
        {
            // Live implementations obtain this distance from SA, not local coordinates.
            var actual = await source.ReadDistance(span.First, span.Second);
            if (!double.IsFinite(actual) || actual < 0) throw new InvalidDataException("Invalid distance result.");
            var deviation = Math.Abs(actual - span.NominalMm);
            rows.Add(new("distance", span.First + ":" + span.Second, null, null, null, actual,
                span.NominalMm, deviation, span.ToleranceMm, deviation <= span.ToleranceMm ? "PASS" : "FAIL"));
        }
        // Detect common context changes. This is not an atomic snapshot or a lease.
        CheckContext(await source.ReadContext(), s);
        return new(1, mode, s.SaTarget, context, rows.ToArray());
    }

    public static int Save(Report report, string directory)
    {
        // Never overwrite a previous result or leave a new partial success report.
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Output must be a new directory.");
        var full = Path.GetFullPath(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var staging = full + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "report.json"), JsonSerializer.Serialize(report, Json) + "\n");
        static string N(double? n) => n?.ToString("F6", CultureInfo.InvariantCulture) ?? "";
        var lines = new List<string> { "kind,id,x_mm,y_mm,z_mm,actual_mm,nominal_mm,deviation_mm,tolerance_mm,status" };
        lines.AddRange(report.Rows.Select(r => string.Join(",", r.Kind, r.Id, N(r.XMm), N(r.YMm), N(r.ZMm),
            N(r.ActualMm), N(r.NominalMm), N(r.DeviationMm), N(r.ToleranceMm), r.Status)));
        File.WriteAllText(Path.Combine(staging, "report.csv"), string.Join("\n", lines) + "\n");
        // Retry only local publication when a Windows scanner temporarily holds
        // a file. Never repeat any SA call or the inspection itself.
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Move(staging, full); break; }
            catch (IOException error) when (attempt < 4 && (error.HResult & 0xffff) is 5 or 32 or 33)
            { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 4)
            { Thread.Sleep(100); }
        }
        Console.WriteLine($"{report.Source} | {report.Context.LengthUnit} | frame={report.Context.FrameCollection}/{report.Context.Frame}");
        foreach (var row in report.Rows)
            Console.WriteLine($"{row.Id,-10} {row.Status,-4} deviation={N(row.DeviationMm)} mm");
        return report.Rows.Any(r => r.Status == "FAIL") ? 2 : 0;
    }
}

public sealed class SyntheticMeasurements(Scenario scenario, Point[] measured) : IMeasurements
{
    private readonly Dictionary<string, Point> _points = measured.ToDictionary(p => p.Name);
    public Task<Context> ReadContext() => Task.FromResult(new Context(scenario.LengthUnit, scenario.FrameCollection, scenario.Frame));
    public Task<(double X, double Y, double Z)> ReadPoint(string name)
    {
        if (!_points.TryGetValue(name, out var p)) throw new InvalidDataException("Missing synthetic point.");
        return Task.FromResult((p.X, p.Y, p.Z));
    }
    public async Task<double> ReadDistance(string first, string second)
    {
        var a = await ReadPoint(first); var b = await ReadPoint(second);
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
