using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SplitRoast.Core.Abstractions;
using SplitRoast.Core.Models;

namespace SplitRoast.Steam;

/// <summary>
/// Discovers co-op games via the public Steam store search endpoint
/// (<c>store.steampowered.com/search/results</c> with <c>infinite=1&amp;json=1</c>) -
/// the same no-API-key JSON feed the store website uses for its infinite scroll.
/// We map our filters onto Steam's own query parameters (<c>category2</c> for the
/// co-op type, <c>tags</c> for genre / subgenre / visual style, <c>sort_by</c> for
/// ordering) and parse the returned result rows. Every failure path returns
/// <see cref="StoreSearchResult.Empty"/>; this must never throw into the UI.
/// </summary>
public sealed class SteamStoreSearchProvider : IStoreDiscoveryProvider
{
    // Same public CDN the artwork provider uses; library_600x900 gives a vertical
    // capsule that matches the installed-library tiles.
    private const string CdnBase = "https://cdn.cloudflare.steamstatic.com/steam/apps";

    private readonly HttpClient _http;

    // One anchor per search result; capture its attributes (which hold the appid)
    // and its inner markup (which holds the title, date, review and price).
    private static readonly Regex RowRegex = new(
        "<a\\b([^>]*data-ds-appid=\"\\d+\"[^>]*)>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AppIdRegex = new("data-ds-appid=\"(\\d+)", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new("<span class=\"title\">(.*?)</span>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ReleasedRegex = new("search_released[^\"]*\">\\s*([^<]*?)\\s*<", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ReviewRegex = new("search_review_summary [^\"]*\"[^>]*data-tooltip-html=\"([^\"]*)\"", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PriceRegex = new("data-price-final=\"(\\d+)\"", RegexOptions.Compiled);
    private static readonly Regex CapsuleRegex = new("<img[^>]+src=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public SteamStoreSearchProvider()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.Add("User-Agent", "SplitRoast");
    }

    public async Task<StoreSearchResult> SearchAsync(StoreSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = BuildUrl(query);
            string json = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            return Parse(json);
        }
        catch
        {
            // Best-effort only: a failed search must never bubble into the UI.
            return StoreSearchResult.Empty;
        }
    }

    private static string BuildUrl(StoreSearchQuery query)
    {
        var sb = new StringBuilder("https://store.steampowered.com/search/results/?infinite=1&json=1&cc=us&l=english");
        sb.Append("&category2=").Append(CategoryId(query.Coop));

        IEnumerable<int> tags = query.Tags.Where(t => t > 0).Distinct();
        string tagList = string.Join(",", tags);
        if (tagList.Length > 0)
        {
            sb.Append("&tags=").Append(tagList);
        }

        string? sort = SortBy(query.Sort);
        if (sort is not null)
        {
            sb.Append("&sort_by=").Append(sort);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            sb.Append("&term=").Append(Uri.EscapeDataString(query.Term.Trim()));
        }

        sb.Append("&start=").Append(Math.Max(0, query.Start));
        sb.Append("&count=").Append(Math.Clamp(query.Count, 1, 100));
        return sb.ToString();
    }

    private static StoreSearchResult Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("results_html", out JsonElement htmlElement)
            || htmlElement.ValueKind != JsonValueKind.String)
        {
            return StoreSearchResult.Empty;
        }

        int total = 0;
        if (root.TryGetProperty("total_count", out JsonElement totalElement))
        {
            if (totalElement.ValueKind == JsonValueKind.Number)
            {
                totalElement.TryGetInt32(out total);
            }
            else if (totalElement.ValueKind == JsonValueKind.String
                     && int.TryParse(totalElement.GetString(), out int parsed))
            {
                total = parsed;
            }
        }

        List<StoreGame> games = ParseRows(htmlElement.GetString() ?? string.Empty);

        // Steam occasionally omits total_count; fall back to the page size so the
        // grid still shows and "load more" stays usable.
        if (total <= 0)
        {
            total = games.Count;
        }

        return new StoreSearchResult { Games = games, TotalCount = total };
    }

    private static List<StoreGame> ParseRows(string html)
    {
        var games = new List<StoreGame>();
        var seen = new HashSet<uint>();

        foreach (Match row in RowRegex.Matches(html))
        {
            string attrs = row.Groups[1].Value;
            string inner = row.Groups[2].Value;

            Match idMatch = AppIdRegex.Match(attrs);
            if (!idMatch.Success || !uint.TryParse(idMatch.Groups[1].Value, out uint appId) || !seen.Add(appId))
            {
                continue;
            }

            string name = Decode(Group(TitleRegex, inner));
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            games.Add(new StoreGame
            {
                AppId = appId,
                Name = name,
                ReleaseDate = NullIfEmpty(Decode(Group(ReleasedRegex, inner))),
                ReviewSummary = ReviewSummary(inner),
                PriceText = PriceText(inner),
                CoverUrl = $"{CdnBase}/{appId}/library_600x900.jpg",
                HeaderUrl = $"{CdnBase}/{appId}/header.jpg",
                CapsuleUrl = NullIfEmpty(Decode(Group(CapsuleRegex, inner)))
            });
        }

        return games;
    }

    private static string? ReviewSummary(string inner)
    {
        string raw = Group(ReviewRegex, inner);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        // The attribute is HTML-encoded ("Very Positive&lt;br&gt;91% of ..."). Decode
        // first, keep only the leading summary phrase (before the line break), then
        // strip any stray tags so no markup ever reaches the UI.
        string decoded = WebUtility.HtmlDecode(raw);
        int br = decoded.IndexOf("<br", StringComparison.OrdinalIgnoreCase);
        string summary = br >= 0 ? decoded[..br] : decoded;
        summary = TagRegex.Replace(summary, string.Empty).Trim();

        // Anything longer than the longest rating phrase ("Overwhelmingly Positive")
        // is the "Need more reviews" notice rather than a score - skip it.
        return summary.Length is > 0 and <= 26 ? summary : null;
    }

    private static string? PriceText(string inner)
    {
        Match m = PriceRegex.Match(inner);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out int cents))
        {
            return null;
        }

        if (cents <= 0)
        {
            return "Free";
        }

        return (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));
    }

    private static string Group(Regex regex, string input)
    {
        Match m = regex.Match(input);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private static string Decode(string value) => WebUtility.HtmlDecode(value).Trim();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int CategoryId(StoreCoopFilter coop) => coop switch
    {
        StoreCoopFilter.OnlineCoop => 38,
        StoreCoopFilter.LanCoop => 39,
        StoreCoopFilter.SplitScreen => 24,
        _ => 9 // AnyCoop
    };

    private static string? SortBy(StoreSortOrder sort) => sort switch
    {
        StoreSortOrder.TopReviews => "Reviews_DESC",
        StoreSortOrder.Newest => "Released_DESC",
        StoreSortOrder.NameAsc => "Name_ASC",
        _ => null // Relevance (Steam default)
    };
}
