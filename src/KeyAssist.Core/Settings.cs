using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyAssist.Core;

public sealed record Calibration(int Width, int Height, int Dpi)
{
    [JsonIgnore]
    public bool IsValid => Width > 0 && Height > 0 && Dpi > 0;
}

public sealed record DetectionPoint(Guid Id, string Name, string X = "", string Y = "", string Color = "");
public sealed record FlaskSlot(bool Enabled = false, bool Refill = false, string Key = "", Guid? PointId = null);
public sealed record SkillStep(string Key = "", string Delay = "100");

public sealed class SettingsSnapshot
{
    public int Version { get; init; } = 1;
    public Dictionary<string, string> Values { get; init; } = [];
    public HashSet<string> Disabled { get; init; } = [];
    public Calibration Calibration { get; set; } = new(0, 0, 0);
    public Dictionary<string, Calibration> ConfirmedPositions { get; init; } = [];
    public List<DetectionPoint> Points { get; init; } = [];
    public List<FlaskSlot> Flasks { get; init; } = Enumerable.Repeat(new FlaskSlot(), 5).ToList();
    public List<SkillStep> Cycle { get; init; } = [new()];
    public HashSet<int> LockedCells { get; init; } = [];
    public RuleSet Rules { get; set; } = new();
    public string SelectedPage { get; set; } = "overview";

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; init; }

    public string Get(string key, string fallback = "") => Values.GetValueOrDefault(key, fallback);
}

public sealed record LoadResult(SettingsSnapshot Settings, string? Error = null);

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string file = Path.GetFullPath(path);

    public LoadResult Load()
    {
        try
        {
            using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var snapshot = JsonSerializer.Deserialize<SettingsSnapshot>(stream, Json);
            return Validate(snapshot) is { } error ? new(new(), error) : new(snapshot!);
        }
        catch (FileNotFoundException) { return new(new()); }
        catch (DirectoryNotFoundException) { return new(new()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(new(), error.Message);
        }
    }

    public string? Save(SettingsSnapshot snapshot)
    {
        if (Validate(snapshot) is { } error)
            return error;
        if (Load().Error is { } loadError)
            return $"原有設定無法讀取，請先備份並檢查原檔。{loadError}";
        return AtomicFile.Write(file, snapshot, Json);
    }

    private static string? Validate(SettingsSnapshot? settings)
    {
        if (settings is not { Version: 1, Values: not null, Calibration: not null, ConfirmedPositions: not null,
                Disabled: not null, Points: not null, Flasks: { Count: 5 }, Cycle: not null,
                LockedCells: not null, Rules.Groups: not null })
            return "設定版本或結構無法辨識。";
        if (settings.SelectedPage is null || settings.Rules.LegacyText is null ||
            settings.Values.Any(pair => pair.Value is null) ||
            settings.Points.Any(point => point is null || point.Id == Guid.Empty || point.Name is null ||
                point.X is null || point.Y is null || point.Color is null) ||
            settings.Points.Select(point => point.Id).Distinct().Count() != settings.Points.Count ||
            settings.Flasks.Any(slot => slot is null || slot.Key is null) ||
            settings.Cycle.Any(step => step is null || step.Key is null || step.Delay is null) ||
            settings.Rules.Groups.Any(group => group is null || !Enum.IsDefined(group.Kind) || group.Rows is null ||
                group.Rows.Any(row => row is null || string.IsNullOrWhiteSpace(row.AffixId))))
            return "設定包含無法辨識的點位、藥劑、循環或詞綴規則。";
        return null;
    }
}

public static class AtomicFile
{
    public static string? Write<T>(string path, T data, JsonSerializerOptions options)
    {
        var file = Path.GetFullPath(path);
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, file, overwrite: true);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return error.Message;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed record ReadinessIssue(string Message, string TargetFieldId);
public sealed record ReadinessCard(string FeatureId, string Title, string PageId, bool IsEnabled, IReadOnlyList<ReadinessIssue> Issues)
{
    public bool IsConfigured => Issues.Count == 0;
    public string TargetFieldId => Issues.FirstOrDefault()?.TargetFieldId ?? FeatureId;
}

