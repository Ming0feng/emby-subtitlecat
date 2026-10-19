using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;

namespace Emby.SubtitleCat;

/// <summary>
/// 判断媒体是否已经自带中文字幕：文件名后缀关键字（-C/-UC/-CH/cjod288ch 等）+ NFO 元数据标题。
/// 关键字可在插件配置中自行增删。
/// </summary>
public static class EmbeddedSubtitleDetector
{
    private static readonly Regex TailNoise = new(
        @"(?i)([-_. ](2160p|1080p|720p|480p|4k|8k|hdr|uhd|hevc|av1|x265|x264|h265|h264|10bit|8bit|aac|dts|flac|proper|repack))+$",
        RegexOptions.Compiled);

    private static readonly Regex TokenSplit = new(@"[-_.\s\[\]()【】]+", RegexOptions.Compiled);

    private static readonly Regex NfoField = new(
        @"(?is)<(title|originaltitle|sorttitle|tagline|tag)>(.*?)</\1>",
        RegexOptions.Compiled);

    public static bool HasEmbeddedChinese(BaseItem item, PluginConfiguration config)
    {
        var keywords = ParseKeywords(config.EmbeddedChineseKeywords);
        if (keywords.Count == 0)
        {
            return false;
        }

        var fileName = item.Path is null ? null : Path.GetFileNameWithoutExtension(item.Path);
        if (MatchesFileName(fileName, keywords))
        {
            return true;
        }

        // 条目名/原始标题（可能是刮削后的标题，按词元匹配，避免 "WATCH" 之类误伤）
        if (MatchesText(item.Name, keywords) || MatchesText(item.OriginalTitle, keywords))
        {
            return true;
        }

        // 标签（如 NFO 的 <tag>中文字幕</tag> 会被 Emby 读入）
        if (item.Tags is not null && item.Tags.Any(t => MatchesText(t, keywords)))
        {
            return true;
        }

        // 类型（JavBus 等刮削常把「中文字幕」写成 <genre>）
        if (item.Genres is not null && item.Genres.Any(g => MatchesText(g, keywords)))
        {
            return true;
        }

        // ProviderId（MetaTube 番号可能带 -C/-UC 后缀）
        if (item.ProviderIds.Values.Any(v => MatchesFileName(v, keywords)))
        {
            return true;
        }

        return FindNfoFiles(item).Any(nfo => MatchesNfo(nfo, keywords));
    }

    /// <summary>文件名：支持 -C 这类后缀，也支持 cjod288ch 这类无分隔符后缀。</summary>
    private static bool MatchesFileName(string? fileName, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var value = TailNoise.Replace(fileName.Trim(), string.Empty);
        var tokens = TokenSplit.Split(value).Where(t => t.Length > 0).ToList();

        foreach (var keyword in keywords)
        {
            if (!IsAscii(keyword))
            {
                if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            var core = keyword.TrimStart('-', '_', '.');
            if (value.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (tokens.Any(t => t.Equals(core, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>标题/NFO 字段：只做词元匹配与中文包含匹配，避免英文单词尾部误判。</summary>
    private static bool MatchesText(string? text, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = TailNoise.Replace(text.Trim(), string.Empty);
        var tokens = TokenSplit.Split(value).Where(t => t.Length > 0).ToList();

        foreach (var keyword in keywords)
        {
            if (!IsAscii(keyword))
            {
                if (value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            var core = keyword.TrimStart('-', '_', '.');
            if (tokens.Any(t => t.Equals(core, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesNfo(string path, IReadOnlyList<string> keywords)
    {
        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4 * 1024 * 1024)
            {
                return false;
            }

            text = File.ReadAllText(path);
        }
        catch
        {
            return false;
        }

        foreach (Match match in NfoField.Matches(text))
        {
            if (MatchesText(match.Groups[2].Value, keywords))
            {
                return true;
            }
        }

        // NFO 的其它字段（如 plot/tag）里出现“中文字幕/中字/内嵌字幕”等中文提示
        return keywords
            .Where(k => !IsAscii(k))
            .Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> FindNfoFiles(BaseItem item)
    {
        var mediaPath = item.Path;
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            yield break;
        }

        var directory = Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        foreach (var candidate in new[] { Path.Combine(directory, baseName + ".nfo"), Path.Combine(directory, "movie.nfo") })
        {
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static List<string> ParseKeywords(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        return raw
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsAscii(string value) => value.All(c => c < 128);
}
