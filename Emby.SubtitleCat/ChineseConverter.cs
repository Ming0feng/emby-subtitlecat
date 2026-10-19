using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Emby.SubtitleCat;

/// <summary>
/// 繁转简（基于 OpenCC 的 TSCharacters 字表，字符级转换）。
/// 字幕场景下字符级转换已足够，且不引入外部依赖。
/// </summary>
public static class ChineseConverter
{
    private static readonly Lazy<Dictionary<char, char>> Map = new(LoadMap, true);

    private static Dictionary<char, char> LoadMap()
    {
        var map = new Dictionary<char, char>(5000);
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Emby.SubtitleCat.TSCharacters.txt");
            if (stream is null)
            {
                return map;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    continue;
                }

                var trad = parts[0].Trim();
                var simp = parts[1].Split(' ')[0].Trim();
                if (trad.Length == 1 && simp.Length == 1 && trad[0] != simp[0])
                {
                    map[trad[0]] = simp[0];
                }
            }
        }
        catch
        {
            // 字表缺失时退化为原文
        }

        return map;
    }

    /// <summary>判断是否包含较多繁体字（用于决定是否需要转换）。</summary>
    public static bool LooksTraditional(string text, int threshold = 3)
    {
        var hits = 0;
        foreach (var ch in text)
        {
            if (Map.Value.ContainsKey(ch) && ++hits >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    public static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text) || Map.Value.Count == 0)
        {
            return text;
        }

        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Map.Value.TryGetValue(chars[i], out var simplified))
            {
                chars[i] = simplified;
            }
        }

        return new string(chars);
    }
}
