using System.Text.Json;
using PuppeteerSharp;

namespace RobloxAudioChecker;

internal static partial class RobloxAssetChecker
{
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
            Headless = true
        });
        var page = await browser.NewPageAsync();

        if (!File.Exists("ids.txt"))
        {
            Console.WriteLine("Put a file named 'ids.txt' next to the program");
            return;
        }
        var rawIds = (await File.ReadAllLinesAsync("ids.txt")).ToList();
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
        var playable = 0;
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
                await page.WaitForSelectorAsync(
                    ".MuiButtonBase-root.MuiTab-root.web-blox-css-tss-14jybbz-Typography-body1-tab.MuiTab-textColorInherit.web-blox-css-mui-dsncs0-Typography-button");
                await Task.Delay(1020);
            var content = await page.GetContentAsync();
            
            if(content.Contains("Audio preview is not available on your browser."))
            {
                status = "Private/Group";
                offSaleIds.Add(id);
                playable++;
                Console.ForegroundColor = ConsoleColor.Blue;
            }
            else if (content.Contains("MuiTypography-root web-blox-css-tss-a5n33q-Typography-body1-Typography-root-timeStamp MuiTypography-inherit web-blox-css-mui-1de74pe"))
            {
                status = "Playable";
                ids.Add(id);
                playable++;
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else
            {
                status = "Moderated";
                Console.ForegroundColor = ConsoleColor.Red;
            }
            
            Console.SetCursorPosition(0, statusLine);
            line = $"Checking {id}: {status} ({i + 1}/{total})";
            Console.Write(line.PadRight(Console.WindowWidth));
            
            Console.ResetColor();
            
            await Task.Delay(300);
        }
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        const string outputFile = "Sorted IDs.txt";
        var final = "";
        var output = "Audio AutoCheck by Graze\n";
        foreach (var id in ids)
        {
            output += $"{id} - \n";
        }
        var output2 = " # Group (?) / Semi Private ID's (?) # \n # some work everywhere some dont - no idea #\n";
        foreach (var id in offSaleIds)
        {
            output2 += $"{id} - \n";
        }
        
        if (offSaleIds.Count > 0) 
            final = output + output2; 
        else
            final = output;

        await File.WriteAllTextAsync(outputFile, final);
        
        Console.SetCursorPosition(0, statusLine + 2);
        Console.WriteLine($"\n {playable}/{total}: \nResults saved to {outputFile}");

        await browser.CloseAsync();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\d+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}
