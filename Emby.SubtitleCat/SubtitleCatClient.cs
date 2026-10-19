using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.SubtitleCat;

/// <summary>搜索结果中的一个字幕条目（详情页）。</summary>
public sealed class SubtitleCandidate
{
    public string Name { get; init; } = string.Empty;
    public string DetailUrl { get; init; } = string.Empty;
    public int DownloadCount { get; init; }
    public int LanguageCount { get; init; }
}

/// <summary>详情页里的一个可下载文件（按语言区分）。</summary>
public sealed class SubtitleVariant
{
    public string RelativeUrl { get; init; } = string.Empty;

    /// <summary>ISO 639-2 三字母代码，用于 Emby 侧语言识别，如 chi/eng/jpn。</summary>
    public string LanguageTag { get; init; } = string.Empty;

    /// <summary>站点原始语言标记，如 zh-CN、zh-TW、en、ja。</summary>
    public string RawTag { get; init; } = string.Empty;

    public string LanguageLabel { get; init; } = string.Empty;

    public bool IsSimplified =>
        RawTag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
        || LanguageLabel.Contains("简体");

    public bool IsTraditional =>
        RawTag.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)
        || (LanguageLabel.Contains("繁体") && !IsSimplified);

    public bool IsEnglish => RawTag.Equals("en", StringComparison.OrdinalIgnoreCase);

    public bool IsJapanese => RawTag.Equals("ja", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// SubtitleCat 站点客户端。纯 HTTP + 正则解析，不依赖 Emby 程序集。
/// 搜索页：/index.php?search={番号}
/// 详情页：/subs/{id}/{name}.html
/// 下载：  /subs/{id}/{name}-{lang}.srt
/// </summary>
public sealed class SubtitleCatClient : IDisposable
{
    private const string BaseUrl = "https://www.subtitlecat.com";

    private static readonly Regex LinkRegex =
        new(@"href=""(?<href>/?subs/[^""]+?\.html)""[^>]*>(?<name>[^<]*)<", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RowRegex =
        new(@"<tr>(?<row>.*?)</tr>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex SrtRegex =
        new(@"href=""(?<href>/?subs/[^""]+?\.srt)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, (string Code, string Label)> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zh-CN"] = ("chi", "简体中文"),
        ["zh-TW"] = ("chi", "繁体中文"),
        ["zh"] = ("chi", "中文"),
        ["en"] = ("eng", "英语"),
        ["ja"] = ("jpn", "日语"),
        ["ko"] = ("kor", "韩语"),
        ["vi"] = ("vie", "越南语"),
        ["th"] = ("tha", "泰语"),
        ["id"] = ("ind", "印尼语"),
        ["es"] = ("spa", "西班牙语"),
        ["pt"] = ("por", "葡萄牙语"),
        ["fr"] = ("fre", "法语"),
        ["de"] = ("ger", "德语"),
        ["ru"] = ("rus", "俄语"),
        ["ar"] = ("ara", "阿拉伯语"),
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public SubtitleCatClient(string proxyUrl, TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl.Trim());
            handler.UseProxy = true;
        }

        _http = new HttpClient(handler) { Timeout = timeout };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        _ownsClient = true;
    }

    /// <summary>按关键词（通常是番号）搜索字幕。</summary>
    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string keyword, CancellationToken cancellationToken)
    {
        var html = await _http
            .GetStringAsync($"{BaseUrl}/index.php?search={Uri.EscapeDataString(keyword)}", cancellationToken)
            .ConfigureAwait(false);

        var results = new List<SubtitleCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match row in RowRegex.Matches(html))
        {
            var block = row.Groups["row"].Value;
            var link = LinkRegex.Match(block);
            if (!link.Success)
            {
                continue;
            }

            var href = link.Groups["href"].Value.TrimStart('/');
            if (!seen.Add(href))
            {
                continue;
            }

            results.Add(new SubtitleCandidate
            {
                Name = WebUtility.HtmlDecode(link.Groups["name"].Value).Trim(),
                DetailUrl = href,
                DownloadCount = ParseMetric(block, "Downloads"),
                LanguageCount = ParseMetric(block, "Languages")
            });
        }

        return results;
    }

    /// <summary>解析详情页，拿到所有分语言的 .srt 直链。</summary>
    public async Task<IReadOnlyList<SubtitleVariant>> GetVariantsAsync(SubtitleCandidate candidate,
        CancellationToken cancellationToken)
    {
        var html = await _http
            .GetStringAsync($"{BaseUrl}/{candidate.DetailUrl}", cancellationToken)
            .ConfigureAwait(false);

        var variants = new List<SubtitleVariant>();
        foreach (Match m in SrtRegex.Matches(html))
        {
            var raw = m.Groups["href"].Value.TrimStart('/');
            var href = WebUtility.UrlDecode(raw);
            var tag = ParseLanguageTag(href);
            var (code, label) = Languages.TryGetValue(tag, out var l) ? l : (tag, tag);
            variants.Add(new SubtitleVariant
            {
                RelativeUrl = raw,
                LanguageTag = code,
                RawTag = tag,
                LanguageLabel = label
            });
        }

        // 去重（同一语言可能重复出现）
        return variants
            .GroupBy(v => v.RelativeUrl, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>下载字幕文件原始字节。</summary>
    public async Task<byte[]> DownloadAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        return await _http.GetByteArrayAsync($"{BaseUrl}/{relativeUrl}", cancellationToken).ConfigureAwait(false);
    }

    private static string ParseLanguageTag(string url)
    {
        var name = url.Substring(url.LastIndexOf('/') + 1);
        var withoutExt = name.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;

        // 形如 xxx.zh-TW / xxx-zh-TW / xxx.en
        var m = Regex.Match(withoutExt, @"[.\-_](?<tag>zh-CN|zh-TW|zh|en|ja|ko|vi|th|id|es|pt|fr|de|ru|ar)$",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["tag"].Value : "und";
    }

    private static int ParseMetric(string block, string label)
    {
        var m = Regex.Match(block, label + @"</span><span[^>]*>(?<n>[\d,]+)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return 0;
        }

        return int.TryParse(m.Groups["n"].Value.Replace(",", string.Empty), out var n) ? n : 0;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
