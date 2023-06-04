public static class RequestBuilder
{
    public static HttpRequestMessage BuildPassMd5Request(string url)
    {
        var request = new HttpRequestMessage();
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);
        request.Headers.TryAddWithoutValidation("Referer", url);
        return request;
    }
    public static HttpRequestMessage BuildEmbedRequest(string url)
    {
        var request = new HttpRequestMessage();
        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(url);
        return request;
    }
    public static HttpRequestMessage BuildDownloadRequest(string url, HttpMethod method, string? etag = null, string? range = null)
    {
        var request = new HttpRequestMessage();
        request.Method = method;
        request.RequestUri = new Uri(url);

        if (etag is not null)
            request.Headers.TryAddWithoutValidation("If-Range", etag);
        request.Headers.TryAddWithoutValidation("Range", range ?? "bytes=0-");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity;q=1, *;q=0");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-GB,en;q=0.7");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        request.Headers.TryAddWithoutValidation("Host", request.RequestUri.Host); // *.dood.video, where sub-domain is variable and can be whatever... Like "km270l.dood.video"
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");
        request.Headers.TryAddWithoutValidation("Referer", "https://dooood.com/");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "video");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-GPC", "1");
        request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Brave\";v=\"113\", \"Chromium\";v=\"113\", \"Not-A.Brand\";v=\"24\"");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");

        return request;
    }
}
