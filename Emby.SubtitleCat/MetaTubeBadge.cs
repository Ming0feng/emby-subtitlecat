using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Emby.SubtitleCat;

/// <summary>
/// 复用 MetaTube 的图片角标能力：读取 MetaTube 插件配置后，
/// 把条目的主图替换为带中文字幕角标的 MetaTube 图片地址。
/// </summary>
public static class MetaTubeBadge
{
    private const string MetaTubeConfigFileName = "MetaTube.json";
    private const string ChineseSubtitleGenre = "中文字幕";

    /// <summary>为条目补上中文字幕角标；已带角标或 MetaTube 未启用时不处理。</summary>
    public static async Task<bool> Apply(BaseItem item, ILibraryManager libraryManager,
        IApplicationPaths applicationPaths, PluginLogger logger)
    {
        try
        {
            if (item.ProviderIds is null
                || !item.ProviderIds.TryGetValue("MetaTube", out var rawPid)
                || string.IsNullOrWhiteSpace(rawPid))
            {
                return false;
            }

            var config = LoadConfig(applicationPaths);
            if (config is null
                || (!config.EnableBadges && !config.EnableUncensoredBadges)
                || string.IsNullOrWhiteSpace(config.Server))
            {
                return false;
            }

            if (!TryParsePid(rawPid, out var provider, out var id, out var position))
            {
                return false;
            }

            var existing = item.GetImageInfo(ImageType.Primary, 0);
            var badge = BuildBadgeSpec(item, config);
            if (string.IsNullOrEmpty(badge)
                || string.Equals(GetBadge(existing?.Path), badge, StringComparison.OrdinalIgnoreCase))
            {
                return false; // 没有角标可加，或角标已经是期望值
            }

            var url = BuildImageUrl(config, provider, id, position, badge);

            item.SetImage(new ItemImageInfo { Path = url, Type = ImageType.Primary }, 0);

            var genres = item.Genres?.ToList() ?? new List<string>();
            if (!genres.Any(g => g.Equals(ChineseSubtitleGenre, StringComparison.OrdinalIgnoreCase)))
            {
                genres.Add(ChineseSubtitleGenre);
                item.Genres = genres.ToArray();
            }

#if __EMBY__
            libraryManager.UpdateItem(item, item, ItemUpdateType.MetadataEdit, null);
#else
            await libraryManager.UpdateItemAsync(item, item, ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);
#endif
            logger.Info("SubtitleCat: 已为 {0} 更新角标 {1}", item.Name, badge);

            // Emby 在“保存图片到媒体文件夹”模式下会把远端主图落盘成 poster.jpg，
            // 之后任意一次刷新都会用它把角标 URL 覆盖回去（旧图只带旧角标）。
            // 这张旧海报是 Emby 自己生成的，删掉后下次刷新会按新角标重新生成。
            RemoveStalePoster(existing?.Path, item.Path, logger);

            return true;
        }
        catch (Exception ex)
        {
            logger.ErrorException("SubtitleCat 添加角标失败: {0}", ex, item.Name);
            return false;
        }
    }

    /// <summary>
    /// 删除与媒体同目录、由 Emby 落盘的旧 poster 图，避免它带着旧角标盖掉新角标。
    /// </summary>
    private static void RemoveStalePoster(string? imagePath, string? mediaPath, PluginLogger logger)
    {
        if (string.IsNullOrWhiteSpace(imagePath)
            || imagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Path.GetFileName(imagePath).StartsWith("poster", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var directory = string.IsNullOrWhiteSpace(mediaPath) ? null : Path.GetDirectoryName(mediaPath);
        if (string.IsNullOrWhiteSpace(directory)
            || !string.Equals(Path.GetDirectoryName(imagePath), directory, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
                logger.Info("SubtitleCat: 已移除旧海报 {0}", imagePath);
            }
        }
        catch (Exception ex)
        {
            logger.Warn("SubtitleCat 删除旧海报失败: {0}", ex.Message);
        }
    }

    /// <summary>拼出条目应使用的角标参数：左上中文字幕 + 右上无码破解。</summary>
    private static string BuildBadgeSpec(BaseItem item, MetaTubeConfig config)
    {
        var parts = new List<string>();

        if (config.EnableBadges)
        {
            parts.Add(string.IsNullOrWhiteSpace(config.BadgeUrl) ? "zimu.png" : config.BadgeUrl);
        }

        if (config.EnableUncensoredBadges && HasUncensored(item.Path, config.UncensoredKeywords))
        {
            var uncensored = string.IsNullOrWhiteSpace(config.UncensoredBadgeUrl)
                ? "uncensored.png"
                : config.UncensoredBadgeUrl;
            parts.Add("tr:" + uncensored);
        }

        return string.Join(",", parts);
    }

    /// <summary>取出图片地址里的 badge 参数值（已解码）。</summary>
    private static string GetBadge(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var queryStart = path.IndexOf('?');
        if (queryStart < 0 || queryStart == path.Length - 1)
        {
            return string.Empty;
        }

        foreach (var pair in path.Substring(queryStart + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            if (pair.Substring(0, separator).Equals("badge", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair.Substring(separator + 1));
            }
        }

        return string.Empty;
    }

    /// <summary>文件名是否带无码/破解标记（-U、-UC、-AI、无码、破解等）。</summary>
    private static bool HasUncensored(string? path, string keywords)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var filename = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        var tokens = filename.Split(
            new[] { '-', '_', ' ', '.', '@', '[', ']', '(', ')', '（', '）' },
            StringSplitOptions.RemoveEmptyEntries);

        var raw = string.IsNullOrWhiteSpace(keywords) ? "U,UC,AI,uncensored,无码,破解" : keywords;
        foreach (var keyword in raw
                     .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(item => item.Trim())
                     .Where(item => item.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (keyword.All(c => c < 128))
            {
                if (tokens.Any(token => token.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (keyword.Length > 1 && filename.EndsWith(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (filename.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildImageUrl(MetaTubeConfig config, string provider, string id, double position,
        string badge)
    {
        var ratio = config.PrimaryImageRatio.ToString("R");
        return $"{config.Server.TrimEnd('/')}/v1/images/primary/" +
               $"{Uri.EscapeDataString(provider)}/{Uri.EscapeDataString(id)}" +
               $"?url=&ratio={ratio}&pos={position.ToString("R")}&auto=false" +
               $"&badge={Uri.EscapeDataString(badge)}&quality={config.DefaultImageQuality}";
    }

    private static bool TryParsePid(string rawPid, out string provider, out string id, out double position)
    {
        provider = string.Empty;
        id = string.Empty;
        position = -1;

        var parts = rawPid.Split(':');
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        provider = parts[0].Trim();
        id = Uri.UnescapeDataString(parts[1]).Trim();

        if (parts.Length > 2 && double.TryParse(parts[2], out var parsed))
        {
            position = parsed;
        }

        return id.Length > 0;
    }

    private static MetaTubeConfig? LoadConfig(IApplicationPaths applicationPaths)
    {
        var directory = applicationPaths.PluginConfigurationsPath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var file = Path.Combine(directory, MetaTubeConfigFileName);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            return new MetaTubeConfig
            {
                Server = GetString(root, "Server"),
                EnableBadges = GetBool(root, "EnableBadges"),
                BadgeUrl = GetString(root, "BadgeUrl"),
                EnableUncensoredBadges = GetBool(root, "EnableUncensoredBadges"),
                UncensoredBadgeUrl = GetString(root, "UncensoredBadgeUrl"),
                UncensoredKeywords = GetString(root, "UncensoredKeywords"),
                PrimaryImageRatio = GetDouble(root, "PrimaryImageRatio", -1),
                DefaultImageQuality = GetInt(root, "DefaultImageQuality", 90)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool GetBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement root, string name, double fallback)
        => root.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;

    private static int GetInt(JsonElement root, string name, int fallback)
        => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private sealed class MetaTubeConfig
    {
        public string Server { get; init; } = string.Empty;

        public bool EnableBadges { get; init; }

        public string BadgeUrl { get; init; } = "zimu.png";

        public bool EnableUncensoredBadges { get; init; }

        public string UncensoredBadgeUrl { get; init; } = "uncensored.png";

        public string UncensoredKeywords { get; init; } = string.Empty;

        public double PrimaryImageRatio { get; init; } = -1;

        public int DefaultImageQuality { get; init; } = 90;
    }
}
