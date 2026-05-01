using System.Text.Json;

namespace RobloxAssetChecker;

internal static partial class RobloxAssetChecker
{
    private enum ReturnType
    {
        Public,
        PublicArchived,
        Moderated
    }

    private static readonly HttpClient _http = new HttpClient();
    private const int BatchSize = 100;

    private static void EnsureFile()
    {
        const string file = "audio.txt";

        if (!File.Exists(file))
        {
            File.WriteAllText(file,
@"# Audio Scan List
# Format: ID - optional name
# Example:
123456789 - cool song
987654321 - background music
");
            Console.WriteLine("Created audio.txt template");
        }
    }

    private record AssetEntry(long Id, string? Label);

    private static List<AssetEntry> ParseFile(IEnumerable<string> lines)
    {
        var seen = new HashSet<long>();
        var results = new List<AssetEntry>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var parts = line.Split(['-', ':'], 2);

            if (!long.TryParse(parts[0].Trim(), out var id))
                continue;

            if (!seen.Add(id))
                continue;

            string? label = parts.Length > 1 ? parts[1].Trim() : null;
            if (string.IsNullOrWhiteSpace(label))
                label = null;

            results.Add(new AssetEntry(id, label));
        }

        return results;
    }

    private static async Task Main()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("Audio Asset Checker (API Version)");
        Console.WriteLine(new string('-', 50));

        EnsureFile();

        var rawLines = await File.ReadAllLinesAsync("audio.txt");
        var entries = ParseFile(rawLines);

        Console.WriteLine($"Loaded {entries.Count} audio IDs\n");

        var publicList = new List<AssetEntry>();
        var archivedList = new List<AssetEntry>();
        var missingList = new List<AssetEntry>();

        for (int i = 0; i < entries.Count; i += BatchSize)
        {
            var batch = entries.Skip(i).Take(BatchSize).ToList();
            var idList = string.Join(",", batch.Select(x => x.Id));

            var url =
                $"https://apis.roblox.com/toolbox-service/v1/items/details?assetIds={idList}";

            string json;

            try
            {
                json = await _http.GetStringAsync(url);
            }
            catch
            {
                Console.WriteLine("Request failed, skipping batch.");
                continue;
            }

            using var doc = JsonDocument.Parse(json);
            var returned = new HashSet<long>();

            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var asset = item.GetProperty("asset");
                    var id = asset.GetProperty("id").GetInt64();
                    returned.Add(id);

                    var name = asset.GetProperty("name").GetString();

                    bool published = false;

                    if (item.TryGetProperty("fiatProduct", out var fiat) &&
                        fiat.TryGetProperty("published", out var pub))
                    {
                        published = pub.GetBoolean();
                    }

                    var entry = batch.FirstOrDefault(x => x.Id == id);
                    var label = entry.Label ?? name;

                    if (published)
                        publicList.Add(new AssetEntry(id, label));
                    else
                        archivedList.Add(new AssetEntry(id, label));

                    Console.WriteLine($"{id} -> {(published ? "Public" : "Archived")}");
                }
            }

            foreach (var item in batch)
            {
                if (!returned.Contains(item.Id))
                {
                    missingList.Add(item);
                    Console.WriteLine($"{item.Id} -> Moderated / Missing");
                }
            }
        }

        // =========================
        // SAFE JSON BUILDER (FIX)
        // =========================

        string Escape(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "");
        }

        string BuildJson(List<AssetEntry> list)
        {
            return "[\n" + string.Join(",\n",
                list.Select(x =>
                    x.Label is null
                        ? $"  {{ \"id\": {x.Id} }}"
                        : $"  {{ \"id\": {x.Id}, \"label\": \"{Escape(x.Label)}\" }}"
                )
            ) + "\n]";
        }

        var jsonOutput =
        $@"{{
  ""public"": {BuildJson(publicList)},
  ""archived"": {BuildJson(archivedList)},
  ""missing"": {BuildJson(missingList)}
}}";

        await File.WriteAllTextAsync("audio - Sorted.json", jsonOutput);

        Console.WriteLine("\nDone! Saved to audio - Sorted.json");
    }

    private static string Format(AssetEntry e)
        => e.Label is null ? $"{e.Id}" : $"{e.Id} - {e.Label}";
}
