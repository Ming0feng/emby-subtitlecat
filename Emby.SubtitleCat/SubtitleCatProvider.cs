using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using Emby.SubtitleCat;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Subtitles;
using MediaBrowser.Model.Providers;

/// <summary>
/// Emby 字幕提供程序：按番号在 subtitlecat.com 搜索字幕，由 Emby 负责下载并保存到媒体目录。
/// </summary>
public class SubtitleCatProvider : ISubtitleProvider
{
    public string Name => "SubtitleCat";

    public IEnumerable<VideoContentType> SupportedMediaTypes => new[] { VideoContentType.Movie };

    public async Task<IEnumerable<RemoteSubtitleInfo>> Search(SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // 番号优先从媒体文件路径提取，其次用条目名称
        var number = SubtitleNumber.Extract(request.MediaPath)
                     ?? SubtitleNumber.Extract(request.Name);

        if (string.IsNullOrEmpty(number))
        {
            return Array.Empty<RemoteSubtitleInfo>();
        }

        using var client = new SubtitleCatClient(config.ProxyUrl, TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)));

        var candidates = await client.SearchAsync(number, cancellationToken).ConfigureAwait(false);
        if (config.StrictNumberMatch)
        {
            var wanted = SubtitleNumber.Normalize(number);
            candidates = candidates
                .Where(c => SubtitleNumber.Normalize(c.Name).Contains(wanted, StringComparison.Ordinal))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<RemoteSubtitleInfo>();
        }

        var preferred = (config.PreferredLanguages ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var results = new List<(RemoteSubtitleInfo Info, int LangIndex, int Downloads)>();

        foreach (var candidate in candidates.Take(Math.Max(1, config.MaxCandidates)))
        {
            IReadOnlyList<SubtitleVariant> variants;
            try
            {
                variants = await client.GetVariantsAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var variant in variants)
            {
                var langIndex = preferred.FindIndex(p => Matches(p, variant));
                if (langIndex < 0)
                {
                    langIndex = preferred.Count + 10; // 未在首选列表里的语言排后面
                }

                results.Add((new RemoteSubtitleInfo
                {
                    Id = EncodeId(variant.RelativeUrl, variant.LanguageTag, variant.RawTag),
                    Name = $"{candidate.Name} · {variant.LanguageLabel} ({number})",
                    ProviderName = Name,
                    Author = "subtitlecat.com",
                    Format = "srt",
#if __EMBY__
                    Language = variant.LanguageTag,
#else
                    ThreeLetterISOLanguageName = variant.LanguageTag,
#endif
                    DownloadCount = candidate.DownloadCount,
                    DateCreated = DateTime.UtcNow
                }, langIndex, candidate.DownloadCount));
            }
        }

        return results
            .OrderBy(r => LangPriority(GetLanguage(r.Info), preferred))
            .ThenByDescending(r => r.Downloads)
            .Take(Math.Clamp(config.MaxResults, 1, 50))
            .Select(r => r.Info)
            .ToList();
    }

    public async Task<SubtitleResponse> GetSubtitles(string id, CancellationToken cancellationToken)
    {
        var (url, language, rawTag) = DecodeId(id);
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        using var client = new SubtitleCatClient(config.ProxyUrl, TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)));
        var bytes = await client.DownloadAsync(url, cancellationToken).ConfigureAwait(false);

        var variant = new SubtitleVariant
        {
            RelativeUrl = url,
            LanguageTag = language,
            RawTag = rawTag
        };

        var text = await SubtitlePipeline
            .BuildAsync(Encoding.UTF8.GetString(bytes), variant, config, cancellationToken)
            .ConfigureAwait(false);

        return new SubtitleResponse
        {
            Format = "srt",
            Language = language,
            Stream = new MemoryStream(new UTF8Encoding(false).GetBytes(text))
        };
    }

    private static string GetLanguage(RemoteSubtitleInfo info)
    {
#if __EMBY__
        return info.Language;
#else
        return info.ThreeLetterISOLanguageName;
#endif
    }

    private static int LangPriority(string language, List<string> preferred)
    {
        var index = preferred.FindIndex(p =>
            p.Equals(language, StringComparison.OrdinalIgnoreCase)
            || (p.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && language == "chi"));
        return index < 0 ? preferred.Count + 10 : index;
    }

    private static bool Matches(string preference, SubtitleVariant variant)
    {
        return preference.Equals(variant.LanguageTag, StringComparison.OrdinalIgnoreCase)
               || (preference.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && variant.LanguageTag == "chi");
    }

    private static string EncodeId(string relativeUrl, string language, string rawTag)
    {
        var raw = $"{relativeUrl}|{language}|{rawTag}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static (string Url, string Language, string RawTag) DecodeId(string id)
    {
        var padded = id.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        var parts = raw.Split('|');
        return parts.Length switch
        {
            >= 3 => (parts[0], parts[1], parts[2]),
            2 => (parts[0], parts[1], parts[1]),
            _ => (raw, "und", "und")
        };
    }
}
