using Serilog;
using System.Diagnostics;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .MinimumLevel.Information()
    .CreateLogger();

string outputFolder = "output";
var urls = args;

// Better for the proof of concept, so I dont need to deal with "file already exists"...
if (Directory.Exists(outputFolder))
    Directory.Delete(outputFolder, true);
Directory.CreateDirectory(outputFolder);

var client = new DoooodClient(outputFolder, maxDegreeOfParallelism: 5);

var sw = Stopwatch.StartNew();
await Parallel.ForEachAsync(urls, async (url, _) =>
{
    await client.DownloadVideoAsync(url);
});
sw.Stop();

Log.Logger.Information("Finished scraping {count} videos in {elapsed}", urls.Length, $"{sw.Elapsed:mm\\:ss\\.fff}");
