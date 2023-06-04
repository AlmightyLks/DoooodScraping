using Serilog;
using Serilog.Core;

public class DoooodClient
{
    // Static constants used for requesting, initialized at first contact with DoooodClient
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.0.0 Safari/537.36";
    private static readonly HttpClient Client = new HttpClient();
    static DoooodClient()
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("user-agent", UserAgent);
        Client.Timeout = TimeSpan.FromMinutes(10);
    }

    private static readonly ILogger Logger = Log.ForContext<DoooodClient>();

    private string _outputFolder;

    public DoooodClient(string outputFolder)
    {
        _outputFolder = outputFolder;
    }
    public async Task DownloadVideoAsync(string url)
    {
        if (url.Contains("/d/")) // if non-embed, turn into embed
            url = url.Replace("/d/", "/e/");

        var fileName = $"{url.Split('/')[^1]}.mp4";

        Logger.Information("Starting to fetch {url} ({fileName})...", url, fileName);

        var streamUrl = await GetStreamUrlAsync(url);

        Logger.Information("Scraped Stream-Url {streamUrl} for {url}!", $"{streamUrl[..(streamUrl.IndexOf('/', 10) + 1)]}", url);
        await DownloadVideoFromStreamLinkAsync(streamUrl, fileName);
        Logger.Information("Successfully downloaded {fileName} to {filePath}!", fileName, Path.Combine(_outputFolder, fileName));
    }

    private async Task<string> GetStreamUrlAsync(string url)
    {
        var request = RequestBuilder.BuildEmbedRequest(url);
        var response = await Client!.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        var passLink = ExtractLink(responseBody);
        var link = Path.Combine("https://dooood.com" + passLink); // https://dooood.com/pass_md5/...
        var query = BuildQuery(responseBody);
        request.Dispose();

        request = RequestBuilder.BuildPassMd5Request(link);
        response = await Client!.SendAsync(request);
        var result = await response.Content.ReadAsStringAsync();
        request.Dispose();
        response.Dispose();
        return $"{result}{query}";
    }
    private static string BuildQuery(string htmlBody)
    {
        var token = GetToken();
        // Technically part of the url building, but it doesnt seem to be necessary...
        // Security by obscurity...
        // var randomString = GetRandomString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        return $"?token={token}&expiry={now}";

        string GetToken()
        {
            var htmlBodyButCooler = htmlBody.AsSpan();
            var startIndex = htmlBodyButCooler.IndexOf("cookieIndex='") + "cookieIndex='".Length;
            var endIndex = htmlBodyButCooler.Slice(startIndex).IndexOf("'");

            return htmlBodyButCooler.Slice(startIndex, endIndex).ToString();
        }
    }
    private async Task DownloadVideoFromStreamLinkAsync(string url, string fileName)
    {
        var request = RequestBuilder.BuildDownloadRequest(url, HttpMethod.Head);
        var response = await Client!.SendAsync(request);
        var etag = response.Headers.ETag?.ToString();
        var contentLength = response.Content.Headers.ContentLength ?? 0;

        // Downloads are limited to around 150Kbit/s... Just enough to stream it as a genuine person.
        // So, split up the download by downloading 15 different byte ranges
        // 1. Asynchronously requesting different sections will slash the download time by 15
        // 2. The beginning of a download is always a little faster at first, before the speed rate-limit kicks in
        // 
        // Problem: It is possible that with this approach we hit a request rate-limit...
        //          Not yet encountered though.
        const int Splits = 15;

        var lengths = contentLength / Splits;
        var remainder = contentLength % lengths;

        Logger.Debug("Downloading {FileName} the Size of {Size:0.00}MB in {Splits}x {SplitSize}MB splits", fileName, (contentLength / 1024f / 1024f), Splits, (lengths / 1024f / 1024f));

        var itLengths = new List<long>();
        for (int i = 1; i < Splits + 1; i++)
            itLengths.Add(lengths * i);

        // Temp folder to store the 15 segments
        var tempSegmentFolderName = Path.Combine(_outputFolder, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(tempSegmentFolderName);

        await Parallel.ForEachAsync(itLengths, new ParallelOptions() { MaxDegreeOfParallelism = 3 }, async (value, _) =>
        {
            var index = itLengths.FindIndex(x => x == value);
            var from = value - (contentLength / Splits);
            var to = index == Splits ? value + remainder : value - 1;

            Logger.Debug("Fetching {from} to {to} for {fileName}...", from, to, fileName);

            //Range: bytes=82640896-136409129
            var request = RequestBuilder.BuildDownloadRequest(url, HttpMethod.Get, etag, $"bytes={from}-{to}");
            var response = await Client!.SendAsync(request);

            // Write http response byte stream into segment file
            var tempSegmentName = Path.Combine(tempSegmentFolderName, index.ToString());
            using var fileStream = File.Create(tempSegmentName);
            var httpStream = await response.Content.ReadAsStreamAsync();
            await httpStream.CopyToAsync(fileStream);
        });

        // Create final file, go through folder with segments and order by segment file name (index)
        using (var fileStream = File.Create(Path.Combine(_outputFolder, fileName)))
        {
            foreach (var file in Directory.GetFiles(tempSegmentFolderName).OrderBy(x => int.Parse(Path.GetFileName(x))))
            {
                fileStream.Write(File.ReadAllBytes(file));
            }
        }

        Directory.Delete(tempSegmentFolderName, true);
    }
    private static string GetRandomString()
        => new string(Enumerable.Repeat("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", 10).Select(s => s[new Random().Next(s.Length)]).ToArray());
    private static string ExtractLink(string str)
    {
        var strButCooler = str.AsSpan();
        var startIndex = strButCooler.IndexOf("pass_md5");
        var endIndex = strButCooler.Slice(startIndex).IndexOf('\'');

        startIndex -= 1;
        endIndex += 1;

        return strButCooler.Slice(startIndex, endIndex).ToString();
    }
}
