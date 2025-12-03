using PuppeteerSharp;

namespace RobloxAssetChecker;

internal static partial class RobloxAssetChecker
{
    
    [Flags]
    private enum IdType
    {
        None    = 0,
        Audio   = 1 << 0, // 1
        Decals  = 1 << 1, // 2
        Clothes = 1 << 2, // 4
        Models  = 1 << 3  // 8
    }
    private static IdType _toScan = IdType.None;
    
    private enum ReturnType
    {
        Public,
        HiddenOrGroupOrArchived,
        Moderated,
    }
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
    
    private static async Task Main()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("Graze's Audio Checker");
        Console.WriteLine(new string('-', 50));
        
        Console.WriteLine("Downloading Headless Chrome if needed Please wait...");
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false
        });
        var page = await browser.NewPageAsync();
        
        Console.WriteLine("Checking what to Scan for.");
        CheckFiles();
        
        if (_toScan == IdType.None)
        {
            Console.WriteLine("Nothing to scan, returning.");
            return;
        }
        if ((_toScan & IdType.Audio) != 0)
        {
            Console.WriteLine("Scanning audio...");
            await ScanTxtFile("audio.txt",page, IdType.Audio);
        }
        if ((_toScan & IdType.Clothes) != 0)
        {
            Console.WriteLine("Scanning clothes...");
            await ScanTxtFile("clothes.txt",page, IdType.Clothes);
        }
        if ((_toScan & IdType.Decals) != 0)
        {
            Console.WriteLine("Scanning decals...");
            await ScanTxtFile("decals.txt",page, IdType.Decals);
        }

        if ((_toScan & IdType.Models) != 0)
        {
            Console.WriteLine("Scanning models...");
            await ScanTxtFile("models.txt",page, IdType.Decals);
        }
        await browser.CloseAsync();
    }
    
        private static async Task ScanTxtFile(string toScan, IPage page, IdType type)
    {
        var rawIds = (await File.ReadAllLinesAsync(toScan)).ToList();
        var cleanedIds = rawIds
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(line =>
            {
                var match = MyRegex().Match(line);
                return match.Success ? match.Value : null;
            })
            .Where(id => id != null)
            .Distinct()
            .ToList();

        Console.WriteLine($"Found {cleanedIds.Count}/{rawIds.Count} IDs for checking (duplicates removed)\n");

        var ids = new List<string?>();
        // ReSharper disable once CollectionNeverUpdated.Local
        List<string?> offSaleIds = [];
        var total = cleanedIds.Count;
        
        var statusLine = Console.CursorTop;
        var publicIds = 0;
        for (var i = 0; i < total; i++)
        {
            var id = cleanedIds[i];
            
            Console.SetCursorPosition(0, statusLine);
            var status = "Checking...";
            var line = $"Checking {id}: {status} ({i + 1}/{total})";
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.Write(line.PadRight(Console.WindowWidth));

            var url = $"https://create.roblox.com/store/asset/{id}";
            await page.GoToAsync(url);
            if (_toScan != IdType.Clothes)
            {
                await page.WaitForSelectorAsync(
                    "MuiGrid-root web-blox-css-tss-1bg6u9k-Grid-root-root MuiGrid-container web-blox-css-mui");   
            }
            else
            {
                await page.WaitForSelectorAsync("topic-navigation-button");
            }
            await Task.Delay(1020);
            var content = await page.GetContentAsync();

            var returnedStatus = type switch
            {
                IdType.Audio   => ScanAudio(content),
                IdType.Decals  => ScanDecals(content),
                IdType.Clothes => ScanClothes(content),
                IdType.Models  => ScanModels(content),
                _ => ReturnType.Moderated
            };
            
            switch (returnedStatus)
            {
                case ReturnType.Public:
                    ids.Add(id);
                    publicIds++;
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                
                case ReturnType.HiddenOrGroupOrArchived:
                    offSaleIds.Add(id);
                    publicIds++;
                    Console.ForegroundColor = ConsoleColor.Blue;
                    break;
                
                    case ReturnType.Moderated:
                    default:
                    Console.ForegroundColor = ConsoleColor.Red;
                        break;
            }
            
            status = nameof(returnedStatus);
            Console.SetCursorPosition(0, statusLine);
            line = $"Checking {id}: {status} ({i + 1}/{total})";
            Console.Write(line.PadRight(Console.WindowWidth));
            
            Console.ResetColor();
            
            await Task.Delay(300);
        }
        
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        const string outputFile = "Sorted IDs.txt";
        string final;
        
        var output = ids.Aggregate(" # Assets AutoCheck by Graze # \n # Public ID's # \n", (current, id) => current + $"{id} - \n");
        var output2 = offSaleIds.Aggregate(" # Group (?) / Semi Private ID's or Archived (?) # \n # most work everywhere some dont - no idea #\n # if they are RLY old prob won't play at all in new games # \n", (current, id) => current + $"{id} - \n");

        if (offSaleIds.Count > 0) 
            final = output + output2; 
        else
            final = output;
        await File.WriteAllTextAsync(outputFile, final);
        
        Console.SetCursorPosition(0, statusLine + 2);
        Console.WriteLine($"\n {publicIds}/{total}: \nResults saved to {outputFile}");

    }
        
    private static ReturnType ScanAudio(string content)
    {
        if(content.Contains("preview is not available on your browser."))
        {
            return ReturnType.HiddenOrGroupOrArchived;
        }
        return content.Contains("MuiTypography-root web-blox-css-tss-a5n33q-Typography-body1-Typography-root-timeStamp MuiTypography-inherit web-blox-css-mui-1de74pe") ? ReturnType.Public : ReturnType.Moderated;
    }
    private static ReturnType ScanDecals(string content)
    {
        return content.Contains("MuiButtonBase-root MuiTab-root web-blox-css-tss-1tdmhvr-Typography-body1 MuiTab-textColorInherit Mui-selected web-blox-css-mui-dsncs0-Typography-button") ? ReturnType.Public : ReturnType.Moderated;
    }
    private static ReturnType ScanClothes(string content)
    {
        return content.Contains("shopping-cart-buy-button item-purchase-btns-container") ? ReturnType.Public : ReturnType.Moderated;
    }
    private static ReturnType ScanModels(string content)
    {
        return content.Contains("MuiButtonBase-root MuiTab-root web-blox-css-tss-1tdmhvr-Typography-body1 MuiTab-textColorInherit Mui-selected web-blox-css-mui-dsncs0-Typography-button") ? ReturnType.Public : ReturnType.Moderated;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\d+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
