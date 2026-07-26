using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;
using AgentCore.Tools;

namespace CodeSharp.Tools;

/// <summary>
/// Tool for web search and URL retrieval (browser-bar model).
/// </summary>
public sealed class WebTools
{
    private readonly HttpClient _httpClient;

    public WebTools(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    [Tool("SearchWeb", "Search the web or directly retrieve a URL.")]
    public async Task<string> SearchWeb(
        [Description("Search query keywords OR a full URL starting with http:// or https://.")] string query,
        [Description("Restrict or prioritize search results to a specific domain (ignored if query is a URL).")] string? domain = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "Error: query cannot be empty.";

        query = query.Trim();

        // Browser-bar router check: direct URL vs search engine query
        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await FetchUrlAsync(uri, ct).ConfigureAwait(false);
        }

        // Web search query execution
        return await ExecuteWebSearchAsync(query, domain, ct).ConfigureAwait(false);
    }

    private async Task<string> FetchUrlAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CodeSharp/1.0");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            
            var plainText = Regex.Replace(content, "<.*?>", " ");
            plainText = WebUtility.HtmlDecode(Regex.Replace(plainText, @"\s+", " ")).Trim();

            return OutputHelpers.HeadTail(plainText, maxChars: 15_000);
        }
        catch (Exception ex)
        {
            return $"Error fetching URL '{uri}': {ex.Message}";
        }
    }

    private async Task<string> ExecuteWebSearchAsync(string query, string? domain, CancellationToken ct)
    {
        try
        {
            var searchTerms = string.IsNullOrWhiteSpace(domain) ? query : $"site:{domain} {query}";
            var searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(searchTerms)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) CodeSharp/1.0");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Robust result parsing extracting Title, URL, and Snippet
            var results = ParseDuckDuckGoResults(html);

            if (results.Count == 0)
                return $"No web search results found for '{query}'.";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Web search results for '{query}':\n");

            for (int i = 0; i < results.Count; i++)
            {
                var item = results[i];
                sb.AppendLine($"{i + 1}. {item.Title}");
                sb.AppendLine($"   URL: {item.Url}");
                sb.AppendLine($"   Snippet: {item.Snippet}\n");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error performing web search: {ex.Message}";
        }
    }

    private static List<SearchResultItem> ParseDuckDuckGoResults(string html)
    {
        var list = new List<SearchResultItem>();

        // Match result blocks containing title link and snippet
        var resultBlockMatches = Regex.Matches(html, @"<div class=""result[^""]* body"">(.*?)</div>\s*</div>", RegexOptions.Singleline);

        foreach (Match blockMatch in resultBlockMatches)
        {
            if (list.Count >= 5) break;

            var blockContent = blockMatch.Groups[1].Value;

            // Extract Title & URL
            var linkMatch = Regex.Match(blockContent, @"<a class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            var snippetMatch = Regex.Match(blockContent, @"<a class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline);

            if (linkMatch.Success)
            {
                var rawUrl = linkMatch.Groups[1].Value;
                var rawTitle = linkMatch.Groups[2].Value;
                var rawSnippet = snippetMatch.Success ? snippetMatch.Groups[1].Value : string.Empty;

                var cleanTitle = WebUtility.HtmlDecode(Regex.Replace(rawTitle, "<.*?>", "")).Trim();
                var cleanSnippet = WebUtility.HtmlDecode(Regex.Replace(rawSnippet, "<.*?>", "")).Trim();
                var cleanUrl = ExtractRealUrl(rawUrl);

                if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(cleanUrl))
                {
                    list.Add(new SearchResultItem(cleanTitle, cleanUrl, cleanSnippet));
                }
            }
        }

        // Fallback simple parsing if block match structure shifts
        if (list.Count == 0)
        {
            var fallbackLinkMatches = Regex.Matches(html, @"<a class=""result__a""[^>]*href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            foreach (Match m in fallbackLinkMatches)
            {
                if (list.Count >= 5) break;
                var rawUrl = m.Groups[1].Value;
                var rawTitle = m.Groups[2].Value;
                var cleanTitle = WebUtility.HtmlDecode(Regex.Replace(rawTitle, "<.*?>", "")).Trim();
                var cleanUrl = ExtractRealUrl(rawUrl);
                if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(cleanUrl))
                {
                    list.Add(new SearchResultItem(cleanTitle, cleanUrl, "Snippet unavailable."));
                }
            }
        }

        return list;
    }

    private static string ExtractRealUrl(string url)
    {
        url = WebUtility.HtmlDecode(url);
        // DuckDuckGo redirects links via /l/?uddg=...
        var match = Regex.Match(url, @"uddg=([^&]+)");
        if (match.Success)
        {
            return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        return url;
    }

    private sealed record SearchResultItem(string Title, string Url, string Snippet);
}
