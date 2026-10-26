using System.IO;
using System.Text.RegularExpressions;

namespace Emby.SubtitleCat;

/// <summary>从文件名/条目名中提取番号，例如 hhd800.com@SSIS-033-C.mp4 → SSIS-033。</summary>
public static class SubtitleNumber
{
    // PPV 段可选：既要匹配 FC2-PPV-3079302 / FC2PPV3079302，也要匹配 FC2-3079302。
    // 少了这个可选项时 FC2-3079302 会落到下面的 NumberRegex，被截成 FC2-30793。
    private static readonly Regex Fc2Regex = new(@"(?i)FC2[-_ ]?(?:PPV[-_ ]?)?(?<n>\d{4,9})", RegexOptions.Compiled);
    private static readonly Regex NumberRegex = new(@"(?<series>[A-Za-z]{2,8})[-_ ]?(?<digits>\d{2,5})", RegexOptions.Compiled);
    private static readonly Regex PrefixRegex = new(@"(?i)^\s*(\[[^\]]*\]|\([^)]*\)|[a-z0-9.\-]+@)", RegexOptions.Compiled);
    private static readonly Regex SuffixRegex = new(
        @"(?i)([-_. ](c|uc|ch|cd\d{1,2}|4k|8k|ai|hdr|uhd|hevc|av1|10bit|8bit|part\d{1,2}|pt\d{1,2}|vol\d{1,2}|中文字幕|中字|字幕|无码|破解|修复|高清))+$",
        RegexOptions.Compiled);

    public static string? Extract(string? pathOrName)
    {
        if (string.IsNullOrWhiteSpace(pathOrName))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(pathOrName.Trim());
        name = PrefixRegex.Replace(name, string.Empty);
        name = SuffixRegex.Replace(name, string.Empty);

        var fc2 = Fc2Regex.Match(name);
        if (fc2.Success)
        {
            return $"FC2-PPV-{fc2.Groups["n"].Value}";
        }

        var m = NumberRegex.Match(name);
        if (!m.Success)
        {
            return null;
        }

        return $"{m.Groups["series"].Value.ToUpperInvariant()}-{m.Groups["digits"].Value}";
    }

    /// <summary>归一化为纯字母数字，用于宽松比较。</summary>
    public static string Normalize(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : Regex.Replace(value, "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase).ToLowerInvariant();
    }
}
