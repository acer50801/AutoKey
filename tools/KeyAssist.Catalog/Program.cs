using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyAssist.Catalog;
using KeyAssist.Core;

Console.OutputEncoding = Encoding.UTF8;
if (args is not ["import", "--input", var input, "--output", var output])
{
    Console.Error.WriteLine("用法：KeyAssist.Catalog import --input <已保存的繁中 HTML 目錄> --output <輸出目錄>");
    return 2;
}

try
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var pages = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(input, "*.html").Order(StringComparer.Ordinal))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var contents = File.ReadAllText(path);
        hash.AppendData(Encoding.UTF8.GetBytes(name + "\0" + contents + "\0"));
        pages.Add(name, contents);
    }
    var version = DateTimeOffset.UtcNow.ToString("yyyyMMdd") + "-" + Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
    var snapshot = PoedbImport.Build(pages, version);
    var report = new
    {
        snapshot.Version, ImportedAtUtc = DateTimeOffset.UtcNow,
        RequiredSourcesAvailable = snapshot.IsComplete, Candidates = snapshot.Entries.Length,
        SearchOnly = snapshot.Entries.Count(entry => !entry.CanEvaluate), snapshot.Coverage
    };
    var reportPath = Path.Combine(output, "catalog-coverage.json");
    var reportError = AtomicFile.Write(reportPath, report, new JsonSerializerOptions { WriteIndented = true });
    if (reportError is not null)
    {
        Console.Error.WriteLine(reportError);
        return 1;
    }
    if (!snapshot.IsComplete)
    {
        Console.Error.WriteLine($"必要來源未通過。已保留原快照，請查閱 {reportPath}");
        return 1;
    }
    var catalogPath = Path.Combine(output, "catalog.json");
    var error = new CatalogStore(catalogPath).Replace(snapshot);
    if (error is not null)
    {
        Console.Error.WriteLine(error);
        return 1;
    }
    Console.WriteLine($"{snapshot.Version} · {snapshot.Entries.Length:N0} 個候選 · {catalogPath}");
    Console.WriteLine($"逐類覆蓋及缺口 · {reportPath}");
    return 0;
}
catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
