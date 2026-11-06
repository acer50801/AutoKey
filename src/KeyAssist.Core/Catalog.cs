using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KeyAssist.Core;

public enum SourceKind { Explicit, Implicit, Crafted, Enchant, Other }

public sealed record AffixEvidence(string Category, string Url, string? OriginalId = null, string? TierText = null);
public sealed record SourceCoverage(string Category, string Url, int Included, int Rejected, string Status, bool Required = true);
public sealed record AffixEntry(string Id, string Template, SourceKind Source, string[] Metrics,
    bool CanEvaluate, AffixEvidence[] Evidence)
{
    [JsonIgnore]
    public string SourceLabel => Source switch
    {
        SourceKind.Explicit => "顯性", SourceKind.Implicit => "固定", SourceKind.Crafted => "工藝",
        SourceKind.Enchant => "附魔", _ => "來源待確認"
    };
    [JsonIgnore]
    public string Display => $"{Template}（{SourceLabel}）";
}

public sealed record CatalogSnapshot(string Version, bool IsComplete, AffixEntry[] Entries, SourceCoverage[] Coverage);

public sealed class AffixIndex(CatalogSnapshot snapshot)
{
    private readonly (AffixEntry Entry, string Text, string Source)[] entries = snapshot.Entries
        .Select(entry => (entry, AffixText.Key(entry.Template), AffixText.Key(entry.SourceLabel))).ToArray();

    public IReadOnlyList<AffixEntry> Search(string query, int limit = 25)
    {
        var terms = query.Normalize(NormalizationForm.FormKC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var combined = AffixText.Key(query);
        if (combined.Length == 0)
            return [];
        return entries.Select(candidate => (candidate.Entry, Rank(candidate.Text, candidate.Source)))
            .Where(candidate => candidate.Item2 > 0)
            .OrderByDescending(candidate => candidate.Item2)
            .ThenBy(candidate => candidate.Entry.Template.Length)
            .ThenBy(candidate => candidate.Entry.Source)
            .ThenBy(candidate => candidate.Entry.Id, StringComparer.Ordinal)
            .Take(limit).Select(candidate => candidate.Entry).ToArray();

        int Rank(string text, string source)
        {
            if (text == combined)
                return 3;
            if (text.Contains(combined, StringComparison.Ordinal))
                return 2;
            return terms.All(term => text.Contains(AffixText.Key(term), StringComparison.Ordinal) ||
                source.Contains(AffixText.Key(term), StringComparison.Ordinal)) ? 1 : 0;
        }
    }
}

public static partial class AffixText
{
    public static string Key(string text) =>
        string.Concat(text.Normalize(NormalizationForm.FormKC).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static string Id(string template, SourceKind source) =>
        $"{source.ToString().ToLowerInvariant()}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key(template))))[..24].ToLowerInvariant()}";

    public static string[] Metrics(string template)
    {
        var count = template.Count(character => character == '#');
        if (count == 2 && RangeFields().IsMatch(template))
            return ["區間下限", "區間上限"];
        return Enumerable.Range(1, count).Select(index => count == 1 ? "數值" : $"第 {index} 個數值").ToArray();
    }

    [GeneratedRegex(@"#\s*至\s*#")]
    private static partial Regex RangeFields();
}

public sealed record CatalogLoad(CatalogSnapshot Snapshot, string? Error = null);

public sealed class CatalogStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public CatalogLoad Load(CatalogSnapshot fallback) => Read(path, fallback, requireComplete: true);

    public static CatalogLoad LoadBundled(string path, CatalogSnapshot fallback) =>
        Read(path, fallback, requireComplete: false);

    private static CatalogLoad Read(string path, CatalogSnapshot fallback, bool requireComplete)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(stream, Json);
            return Validate(snapshot, requireComplete) is { } error ? new(fallback, error) : new(snapshot!);
        }
        catch (FileNotFoundException) { return new(fallback); }
        catch (DirectoryNotFoundException) { return new(fallback); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(fallback, error.Message);
        }
    }

    public string? Replace(CatalogSnapshot snapshot)
    {
        if (Validate(snapshot, requireComplete: true) is { } error)
            return error;
        var previous = Load(snapshot).Snapshot;
        foreach (var source in previous.Coverage.Where(source => source.Included > 0))
        {
            var matching = snapshot.Coverage.FirstOrDefault(current => current.Category == source.Category && current.Url == source.Url);
            if (matching is null || matching.Included == 0 || matching.Included * 2L < source.Included)
                return $"「{source.Category}」的可用列明顯減少，已保留先前的詞綴快照。";
        }
        return AtomicFile.Write(path, snapshot, Json);
    }

    private static string? Validate(CatalogSnapshot? snapshot, bool requireComplete)
    {
        if (snapshot is not { Entries.Length: > 0, Coverage.Length: > 0 } || string.IsNullOrWhiteSpace(snapshot.Version) ||
            snapshot.Entries.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.Template) || !Enum.IsDefined(entry.Source) ||
                entry.Metrics is null || entry.Metrics.Length != entry.Template.Count(c => c == '#') ||
                entry.Evidence is not { Length: > 0 } || entry.Evidence.Any(evidence => evidence is null ||
                    string.IsNullOrWhiteSpace(evidence.Url))) ||
            snapshot.Coverage.Any(source => source is null || string.IsNullOrWhiteSpace(source.Category) ||
                string.IsNullOrWhiteSpace(source.Status) || source.Included < 0 || source.Rejected < 0) ||
            snapshot.Entries.Select(entry => entry.Id).Distinct().Count() != snapshot.Entries.Length ||
            snapshot.Entries.Select(entry => (entry.Source, AffixText.Key(entry.Template))).Distinct().Count() != snapshot.Entries.Length)
            return "詞綴快照格式錯誤。";
        if (requireComplete && (!snapshot.IsComplete || snapshot.Coverage.Any(source => source.Required && source.Included == 0)))
            return "有必要來源缺漏，保留先前的詞綴快照。";
        return null;
    }
}
