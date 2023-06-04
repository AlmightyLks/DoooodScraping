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

    private readonly long _splitSize;
    private readonly string _outputFolder;
    private readonly int _maxDegreeOfParallelism;

    public DoooodClient(string outputFolder, int? maxDegreeOfParallelism = null, long? splitSize = null)
    {
        _outputFolder = outputFolder;
        _splitSize = splitSize ?? 2_097_152; // 2 * 1024 * 1024 = 2MiB
        _maxDegreeOfParallelism = maxDegreeOfParallelism ?? 3;
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
        var response = await Client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        var passLink = ExtractLink(responseBody);
        var link = Path.Combine("https://dooood.com" + passLink); // https://dooood.com/pass_md5/...
        var query = BuildQuery(responseBody);
        request.Dispose();

        request = RequestBuilder.BuildPassMd5Request(link);
        response = await Client.SendAsync(request);
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
        var response = await Client.SendAsync(request);
        var etag = response.Headers.ETag?.ToString();
        var contentLength = response.Content.Headers.ContentLength ?? 0;

        // Downloads are limited to around 150Kbit/s... Just enough to stream it as a genuine person.
        // So, determine how many splits to make, to download X different byte ranges, each of Y bytes
        // 1. Asynchronously requesting different sections will slash the download time by X times
        // 2. The beginning of a download is always a little faster at first, before the speed rate-limit kicks in
        // 
        // Problem: It is possible that with this approach we hit a request rate-limit...
        //          Not yet encountered though.

        int splits = (int)(contentLength / _splitSize) + 1;
        var remainder = contentLength % _splitSize;

        var sizeInMB = (contentLength / 1024f / 1024f);
        var eta = TimeSpan.FromSeconds(1 + (contentLength / 5_000_000D)); // Estimated 5Mbit/s + 1 second overhead
        Logger.Debug("Downloading {FileName} from {VideoUrl} ...", fileName, url);
        Logger.Information("Downloading {FileName} with a Size of {SizeInMiB:0.00}MB ({Size} Bytes) in {Splits} splits. ETA: {ETA:mm':'ss'.'fff}", fileName, sizeInMB, contentLength, splits, eta);

        // Temp folder to store the x amount of segments
        var tempSegmentFolderName = Path.Combine(_outputFolder, Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(tempSegmentFolderName);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, splits),
            new ParallelOptions() { MaxDegreeOfParallelism = _maxDegreeOfParallelism },
            async (value, _) =>
            {
                var from = value * _splitSize;
                var to = value == splits
                ? (value * _splitSize) + remainder
                : ((value + 1) * _splitSize) - 1;

                Logger.Debug("Fetching {from} to {to} for {fileName}...", from, to, fileName);

                // Range: bytes=82640896-136409129
                var request = RequestBuilder.BuildDownloadRequest(url, HttpMethod.Get, etag, $"bytes={from}-{to}");
                var response = await Client.SendAsync(request);
                response.EnsureSuccessStatusCode();

                // Write http response byte stream into segment file
                var tempSegmentName = Path.Combine(tempSegmentFolderName, value.ToString());
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
