using PuppeteerSharp;

namespace RobloxAssetChecker;

internal static partial class RobloxAssetChecker
{
    [Flags]
    private enum IdType
    {
        None    = 0,
        Audio   = 1 << 0,
        Decals  = 1 << 1,
        Clothes = 1 << 2,
        Models  = 1 << 3
    }
    private static IdType _toScan = IdType.None;

    private enum ReturnType
    {
        Public,
        PublicArchived,
        GroupOrRlyOldUnkown,
        Moderated,
    }

    private static IBrowser? _browser;

    private static void CheckFiles()
    {
        if (File.Exists("audio.txt"))
            _toScan |= IdType.Audio;

        if (File.Exists("clothes.txt"))
            _toScan |= IdType.Clothes;

        if (File.Exists("decals.txt"))
            _toScan |= IdType.Decals;

        if (File.Exists("models.txt"))
            _toScan |= IdType.Models;
    }

    private static async Task KillBrowser()
    {
        if (_browser is null) return;
        try { await _browser.CloseAsync(); }
        catch
        {
            // ignored
        }

        _browser = null;
    }

    private static async Task Main()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nInterrupted, closing browser...");
            Console.ResetColor();
            KillBrowser().GetAwaiter().GetResult();
            Environment.Exit(0);
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            KillBrowser().GetAwaiter().GetResult();
        };

        Console.Clear();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("Graze's Asset Checker");
        Console.WriteLine(new string('-', 50));

        Console.WriteLine("Downloading Headless Chrome if needed. Please wait...");
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
        var page = await _browser.NewPageAsync();

        Console.WriteLine("Checking what to scan.");
        CheckFiles();

        if (_toScan == IdType.None)
        {
            Console.WriteLine("Nothing to scan, returning.");
            await KillBrowser();
            return;
        }

        if ((_toScan & IdType.Audio) != 0)
        {
            Console.WriteLine("Scanning audio...");
            await ScanTxtFile("audio.txt", page, IdType.Audio);
        }
        if ((_toScan & IdType.Clothes) != 0)
        {
            Console.WriteLine("Scanning clothes...");
            await ScanTxtFile("clothes.txt", page, IdType.Clothes);
        }
        if ((_toScan & IdType.Decals) != 0)
        {
            Console.WriteLine("Scanning decals...");
            await ScanTxtFile("decals.txt", page, IdType.Decals);
        }
        if ((_toScan & IdType.Models) != 0)
        {
            Console.WriteLine("Scanning models...");
            await ScanTxtFile("models.txt", page, IdType.Models);
        }

        await KillBrowser();
    }

    private record AssetEntry(string Id, string? Label);

    private static List<AssetEntry> ParseAssetFile(IEnumerable<string> lines)
    {
        var seen    = new HashSet<string>();
        var results = new List<AssetEntry>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var m = AssetIdRegex().Match(line);
            if (!m.Success)
                continue;

            var id = m.Value;
            if (!seen.Add(id))
                continue;

            var after = line[(m.Index + m.Length)..];
            var label = LabelSeparatorRegex().Replace(after, "").Trim();

            results.Add(new AssetEntry(id, string.IsNullOrWhiteSpace(label) ? null : label));
        }

        return results;
    }

    private static string CensorId(string id) =>
        id.Length <= 5 ? new string('#', id.Length)
                       : id[..3] + new string('#', id.Length - 5) + id[^2..];

    private static async Task ScanTxtFile(string toScan, IPage page, IdType type)
    {
        var rawLines = await File.ReadAllLinesAsync(toScan);
        var entries  = ParseAssetFile(rawLines);

        Console.WriteLine($"Found {entries.Count}/{rawLines.Length} IDs for checking (duplicates/headers removed)\n");

        var ids            = new List<AssetEntry>();
        var publicArchived = new List<AssetEntry>();
        var groupOrOld     = new List<AssetEntry>();
        var moderated      = 0;
        var total          = entries.Count;
        var statusLine     = Console.CursorTop;

        void DrawCounter()
        {
            Console.SetCursorPosition(0, statusLine + 1);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"  Public: {ids.Count}");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  PublicArchived: {publicArchived.Count}");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"  Group/Old/Unknown(playable*): {groupOrOld.Count}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"  Moderated: {moderated}");
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write($"  Total: {ids.Count + publicArchived.Count + groupOrOld.Count + moderated}/{total}");
            Console.Write(string.Empty.PadRight(Console.WindowWidth - Console.CursorLeft));
            Console.ResetColor();
        }

        for (var i = 0; i < total; i++)
        {
            var entry = entries[i];
            var id    = entry.Id;

            Console.SetCursorPosition(0, statusLine);
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write($"Checking {CensorId(id)}: Checking... ({i + 1}/{total})".PadRight(Console.WindowWidth));
            DrawCounter();

            var url = $"https://create.roblox.com/store/asset/{id}";
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = [WaitUntilNavigation.Networkidle2],
                Timeout   = 30_000
            });

            await Task.Delay(1_200);

            var content = await page.GetContentAsync();

            var returnedStatus = type switch
            {
                IdType.Audio   => ScanAudio(content),
                IdType.Decals  => ScanDecals(content),
                IdType.Clothes => ScanClothes(content),
                IdType.Models  => ScanModels(content),
                _              => ReturnType.Moderated
            };

            switch (returnedStatus)
            {
                case ReturnType.Public:
                    ids.Add(entry);
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;

                case ReturnType.PublicArchived:
                    publicArchived.Add(entry);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;

                case ReturnType.GroupOrRlyOldUnkown:
                    groupOrOld.Add(entry);
                    Console.ForegroundColor = ConsoleColor.Blue;
                    break;

                case ReturnType.Moderated:
                default:
                    moderated++;
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
            }

            var statusLabel = returnedStatus.ToString();
            Console.SetCursorPosition(0, statusLine);
            Console.Write($"Checking {CensorId(id)}: {statusLabel} ({i + 1}/{total})".PadRight(Console.WindowWidth));
            DrawCounter();
            Console.ResetColor();

            await Task.Delay(300);
        }

        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        var outputFile = Path.GetFileNameWithoutExtension(toScan) + " - Sorted.txt";

        var output = ids.Aggregate(
            " # Assets AutoCheck by Graze # \n # Public IDs # \n",
            (current, e) => current + FormatEntry(e));

        var output2 = publicArchived.Aggregate(
            " # Public but Archived IDs # \n" +
            " # These play fine but wont show in search # \n",
            (current, e) => current + FormatEntry(e));

        var output3 = groupOrOld.Aggregate(
            " # Group/Arcive (?) IDs or Really Old (?) # \n" +
            " # Im not fully sure how to fully split every Archive type but most should play in boombox games #\n" +
            " # If they are really old, they probably won't play in new games Tho # \n",
            (current, e) => current + FormatEntry(e));

        var final = output;
        if (publicArchived.Count > 0) final += output2;
        if (groupOrOld.Count > 0)     final += output3;

        await File.WriteAllTextAsync(outputFile, final);

        Console.SetCursorPosition(0, statusLine + 3);
        Console.WriteLine($"Results saved to {outputFile}");
        return;

        static string FormatEntry(AssetEntry e) =>
            e.Label is null ? $"{e.Id}\n" : $"{e.Id} - {e.Label}\n";
    }

    private static ReturnType ScanAudio(string content)
    {
        if (content.Contains("data-testid=\"PLAYWRIGHT_audioPlayer\""))
            return ReturnType.Public;

        if (content.Contains("data-testid=\"PLAYWRIGHT_getAsset\"") || content.Contains("disabled=\"\"") || content.Contains("Audio preview is not available on your browser."))
            return content.Contains("@DistrokidOfficial") ? ReturnType.PublicArchived : ReturnType.GroupOrRlyOldUnkown;

        return ReturnType.Moderated;
    }

    private static ReturnType ScanDecals(string content)
    {
        return content.Contains("Mui-selected") ? ReturnType.Public : ReturnType.Moderated;
    }

    private static ReturnType ScanClothes(string content)
    {
        return content.Contains("shopping-cart-buy-button") ? ReturnType.Public : ReturnType.Moderated;
    }

    private static ReturnType ScanModels(string content)
    {
        return content.Contains("Mui-selected") ? ReturnType.Public : ReturnType.Moderated;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<!\d)\d{5,}(?!\d)")]
    private static partial System.Text.RegularExpressions.Regex AssetIdRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"^[\s\-\u2013\u2014\.,:]+")]
    private static partial System.Text.RegularExpressions.Regex LabelSeparatorRegex();
}