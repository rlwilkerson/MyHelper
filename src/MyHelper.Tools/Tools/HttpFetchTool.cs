using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace MyHelper.Tools.Tools;

public static class HttpFetchTool
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static AIFunction Create() => AIFunctionFactory.Create(
        async ([Description("URL to fetch via HTTP GET.")] string url) =>
        {
            try
            {
                var response = await _http.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();

                if (body.Length > 50_000)
                    body = body[..50_000] + "\n[truncated]";

                return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n\n{body}";
            }
            catch (Exception ex)
            {
                return $"Error fetching URL: {ex.Message}";
            }
        },
        "http_get",
        "Fetch the body of a URL via HTTP GET.");
}
