using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emby.SubtitleCat;

/// <summary>
/// SRT 清洗：删除广告/水印字幕块，重建序号，统一换行。
/// </summary>
public static class SubtitleSanitizer
{
    private static readonly Regex WatermarkRegex = new(
        @"(?i)(whisper\s*jav|耳語\s*jav|耳语\s*jav|hhd800\.com|98堂|98t\.la|subtitlecat|subscene|opensubtitles|advertis|字幕由.{0,12}(提供|制作)|更多字幕|下载字幕|www\.[a-z0-9\-]+\.(com|net|org|xyz|cc))",
        RegexOptions.Compiled);

    private static readonly Regex TimeLineRegex = new(
        @"^\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}\s*-->\s*\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}",
        RegexOptions.Compiled);

    private static readonly Regex CreditRegex = new(
        @"^\s*(字幕|翻译|翻譯|校对|校對|时间轴|時間軸|压制|壓制|出品)\s*[:：]",
        RegexOptions.Compiled);

    public static string Clean(string srt)
    {
        if (string.IsNullOrWhiteSpace(srt))
        {
            return srt;
        }

        var normalized = srt.Replace("\r\n", "\n").Replace('\r', '\n');
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var kept = new List<(string Time, List<string> Lines)>();
        foreach (var raw in blocks)
        {
            var lines = new List<string>(raw.Split('\n'));
            // 去掉可能存在的序号行
            if (lines.Count > 0 && int.TryParse(lines[0].Trim(), out _))
            {
                lines.RemoveAt(0);
            }

            if (lines.Count == 0 || !TimeLineRegex.IsMatch(lines[0].Trim()))
            {
                continue;
            }

            var time = lines[0].Trim();
            var textLines = new List<string>();
            foreach (var line in lines.Skip(1))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (WatermarkRegex.IsMatch(trimmed) || CreditRegex.IsMatch(trimmed))
                {
                    continue; // 丢弃水印/广告行
                }

                textLines.Add(line);
            }

            if (textLines.Count == 0)
            {
                continue; // 整块只有水印
            }

            kept.Add((time, textLines));
        }

        var sb = new StringBuilder();
        for (var i = 0; i < kept.Count; i++)
        {
            sb.Append(i + 1).Append('\n');
            sb.Append(kept[i].Time).Append('\n');
            foreach (var line in kept[i].Lines)
            {
                sb.Append(line).Append('\n');
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}
