using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.SubtitleCat;

/// <summary>
/// 字幕选择与后处理管线：
/// 简体 → 繁体(转简体) → 英文/日语(Google 翻译)，再统一走清洗。
/// </summary>
public static class SubtitlePipeline
{
    private static readonly Regex TimeRegex = new(
        @"^\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}\s*-->\s*\d{1,2}:\d{2}:\d{2}[,.]\d{1,3}",
        RegexOptions.Compiled);

    /// <summary>按配置挑选最合适的语言变体。</summary>
    public static SubtitleVariant? PickBest(IReadOnlyList<SubtitleVariant> variants, PluginConfiguration config)
    {
        if (variants.Count == 0)
        {
            return null;
        }

        var simplified = variants.FirstOrDefault(v => v.IsSimplified);
        if (simplified is not null)
        {
            return simplified;
        }

        var traditional = variants.FirstOrDefault(v => v.IsTraditional);
        if (traditional is not null)
        {
            return traditional;
        }

        if (!config.EnableTranslation)
        {
            return null;
        }

        return variants.FirstOrDefault(v => v.IsEnglish)
               ?? variants.FirstOrDefault(v => v.IsJapanese);
    }

    /// <summary>把原始字幕内容处理成最终写入文件/返回给 Emby 的文本。</summary>
    public static async Task<string> BuildAsync(string rawText, SubtitleVariant variant, PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var text = rawText.TrimStart('\uFEFF');
        if (config.StripWatermarks)
        {
            text = SubtitleSanitizer.Clean(text);
        }

        if (variant.IsTraditional && config.PreferSimplified)
        {
            text = ChineseConverter.ToSimplified(text);
        }

        if ((variant.IsEnglish || variant.IsJapanese) && config.EnableTranslation)
        {
            text = await TranslateSrtAsync(text, config, cancellationToken).ConfigureAwait(false);
        }

        return text;
    }

    /// <summary>逐条翻译 SRT 文本，保持时间轴不变。</summary>
    public static async Task<string> TranslateSrtAsync(string srt, PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var (times, texts) = ParseSrt(srt);
        if (texts.Count == 0)
        {
            return srt;
        }

        var proxy = string.IsNullOrWhiteSpace(config.TranslationProxyUrl)
            ? config.ProxyUrl
            : config.TranslationProxyUrl;

        using var translator = new GoogleTranslator(config.TranslationEndpoint, config.TranslateTargetLanguage,
            proxy, TimeSpan.FromSeconds(Math.Max(5, config.TranslationTimeoutSeconds)));

        var translated = await translator.TranslateLinesAsync(texts, cancellationToken).ConfigureAwait(false);

        var sb = new StringBuilder();
        for (var i = 0; i < times.Count; i++)
        {
            var line = translated[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                line = texts[i];
            }

            sb.Append(i + 1).Append('\n');
            sb.Append(times[i]).Append('\n');
            sb.Append(line.Trim()).Append('\n').Append('\n');
        }

        return sb.ToString();
    }

    private static (List<string> Times, List<string> Texts) ParseSrt(string srt)
    {
        var times = new List<string>();
        var texts = new List<string>();

        var lines = srt.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!TimeRegex.IsMatch(line))
            {
                continue;
            }

            times.Add(line);

            var buffer = new StringBuilder();
            for (var j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j].Trim();
                if (next.Length == 0 || TimeRegex.IsMatch(next))
                {
                    break;
                }

                if (buffer.Length > 0)
                {
                    buffer.Append(' ');
                }

                buffer.Append(next);
            }

            texts.Add(buffer.ToString());
        }

        return (times, texts);
    }
}
