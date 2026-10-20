using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
#if !__EMBY__
using MediaBrowser.Model.Configuration;
#endif

namespace Emby.SubtitleCat;

/// <summary>单条媒体的字幕抓取结果。</summary>
public enum SubtitleFetchStatus
{
    Downloaded,
    AlreadyExists,
    EmbeddedChinese,
    ExternalSubtitleExists,
    NoNumber,
    NoCandidate,
    Failed
}

public sealed class SubtitleFetchResult
{
    public SubtitleFetchStatus Status { get; init; }

    public string Number { get; init; } = string.Empty;

    public string? TargetPath { get; init; }

    public string? Message { get; init; }
}

/// <summary>
/// 单条媒体的字幕抓取逻辑，供事件触发（AutoSubtitleService）与全库扫描任务共用。
/// </summary>
public static class SubtitleFetcher
{
    /// <summary>为一条媒体搜索并写入中文字幕，返回处理结果。</summary>
    public static async Task<SubtitleFetchResult> FetchAsync(BaseItem item, PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var mediaPath = item.Path;
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.Failed, Message = "条目没有文件路径" };
        }

        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);

        // 文件名/NFO 里带 -C、-UC、-CH、中文字幕 等标记：已内嵌中文，跳过
        if (EmbeddedSubtitleDetector.HasEmbeddedChinese(item, config))
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.EmbeddedChinese, Number = baseName };
        }

        if (HasChineseSubtitle(directory, baseName))
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.AlreadyExists, Number = baseName };
        }

        // 已有任意外挂字幕（英文/日文等）也跳过，避免重复下载
        if (config.SkipWhenExternalSubtitleExists && HasExternalSubtitle(directory, baseName))
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.ExternalSubtitleExists, Number = baseName };
        }

        var numbers = ResolveNumbers(item, mediaPath);
        if (numbers.Count == 0)
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.NoNumber };
        }

        using var client = new SubtitleCatClient(config.ProxyUrl,
            TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSeconds)));

        // 依次尝试各个番号：文件名可能和元数据里的番号不一致（如 MIKR-1208 vs MIKR-108），
        // 只要其中一个能搜到严格匹配的字幕就用它。
        var number = numbers[0];
        List<SubtitleCandidate> candidates = new();
        foreach (var candidateNumber in numbers)
        {
            var found = (await client.SearchAsync(candidateNumber, cancellationToken).ConfigureAwait(false)).ToList();
            if (config.StrictNumberMatch)
            {
                var wanted = SubtitleNumber.Normalize(candidateNumber);
                found = found
                    .Where(c => SubtitleNumber.Normalize(c.Name).Contains(wanted, StringComparison.Ordinal))
                    .ToList();
            }

            if (found.Count == 0)
            {
                continue;
            }

            number = candidateNumber;
            candidates = found;
            break;
        }

        if (candidates.Count == 0)
        {
            return new SubtitleFetchResult { Status = SubtitleFetchStatus.NoCandidate, Number = number };
        }

        foreach (var candidate in candidates.OrderByDescending(c => c.DownloadCount).Take(Math.Max(1, config.MaxCandidates)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var variants = await client.GetVariantsAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (variants.Count == 0)
            {
                continue;
            }

            var chosen = SubtitlePipeline.PickBest(variants, config);
            if (chosen is null)
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = await client.DownloadAsync(chosen.RelativeUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                continue;
            }

            var text = await SubtitlePipeline
                .BuildAsync(Decode(bytes), chosen, config, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // 简体用 .chs.srt，繁体（未转简体时）用 .cht.srt，Emby 都能识别
            var languageSuffix = chosen.IsTraditional && !config.PreferSimplified ? ".cht.srt" : ".chs.srt";
            var target = Path.Combine(directory, baseName + languageSuffix);

            await File.WriteAllTextAsync(target, text, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            return new SubtitleFetchResult
            {
                Status = SubtitleFetchStatus.Downloaded,
                Number = number,
                TargetPath = target
            };
        }

        return new SubtitleFetchResult { Status = SubtitleFetchStatus.NoCandidate, Number = number };
    }

    /// <summary>条目是否已经挂载了有效的外挂字幕流（字幕文件必须仍然存在）。</summary>
    public static bool HasExternalSubtitleStream(BaseItem item)
    {
        try
        {
            var streams = item.MediaStreams;
            if (streams is null)
            {
                return false;
            }

            return streams.Any(s =>
                s.Type == MediaStreamType.Subtitle
                && s.IsExternal
                && !string.IsNullOrWhiteSpace(s.Path)
                && File.Exists(s.Path));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>该条目所属媒体库是否启用了 SubtitleCat 字幕下载器。</summary>
    public static bool IsProviderEnabled(BaseItem item, ILibraryManager libraryManager)
    {
        try
        {
            var options = libraryManager.GetLibraryOptions(item);
            var disabled = options?.DisabledSubtitleFetchers;
            if (disabled is null || disabled.Length == 0)
            {
                return true;
            }

            return !disabled.Contains("SubtitleCat", StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // 拿不到库配置时不阻断
            return true;
        }
    }

    /// <summary>刷新条目媒体信息，让新写入的字幕被服务端识别。</summary>
#if __EMBY__
    public static async Task RefreshMediaInfoAsync(BaseItem item, ILibraryManager libraryManager,
        IProviderManager providerManager, IFileSystem fileSystem, PluginLogger logger, CancellationToken cancellationToken)
#else
    public static async Task RefreshMediaInfoAsync(BaseItem item, ILibraryManager libraryManager,
        IProviderManager providerManager, IDirectoryService directoryService, PluginLogger logger,
        CancellationToken cancellationToken)
#endif
    {
        try
        {
            // 服务端只在视频文件修改时间变化时才重新扫描同目录的外挂字幕，
            // 写完 .chi.srt 后把视频 mtime 推到当前时间，否则刷新也不会挂载字幕。
            if (!string.IsNullOrWhiteSpace(item.Path))
            {
                try
                {
                    File.SetLastWriteTimeUtc(item.Path, DateTime.UtcNow);
                }
                catch
                {
                    // 忽略只读/权限问题
                }
            }

#if __EMBY__
            var options = new MetadataRefreshOptions(fileSystem)
            {
                EnableRemoteContentProbe = true,
                EnableSubtitleDownloading = false,
                MetadataRefreshMode = MetadataRefreshMode.Default
            };

            var libraryOptions = libraryManager.GetLibraryOptions(item);
            await providerManager.RefreshSingleItem(item, options, null, libraryOptions, cancellationToken)
                .ConfigureAwait(false);
#else
            var options = new MetadataRefreshOptions(directoryService)
            {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.Default
            };

            await providerManager.RefreshSingleItem(item, options, cancellationToken)
                .ConfigureAwait(false);
#endif
            logger.Info("SubtitleCat: 已刷新媒体信息 {0}", item.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.ErrorException("SubtitleCat 刷新媒体信息失败: {0}", ex, item.Name);
        }
    }

    /// <summary>
    /// 收集条目可能的番号，按可信度排序：MetaTube 标识 → 条目名（元数据） → 文件路径。
    /// 文件被错误命名时（例如 MIKR-1208 实为 MIKR-108），仍能回退到元数据番号。
    /// </summary>
    private static List<string> ResolveNumbers(BaseItem item, string mediaPath)
    {
        var numbers = new List<string>();

        void Add(string? value)
        {
            if (!string.IsNullOrEmpty(value) && !numbers.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                numbers.Add(value);
            }
        }

        if (item.ProviderIds.TryGetValue("MetaTube", out var providerId) && !string.IsNullOrWhiteSpace(providerId))
        {
            // MetaTube 的值可能是 "JavDB:xxxx" / "JavBus:SSIS-033" 形式，只取冒号后的部分
            var raw = providerId.Trim();
            var colon = raw.IndexOf(':');
            if (colon >= 0 && colon < raw.Length - 1)
            {
                raw = raw[(colon + 1)..].Trim();
            }

            Add(SubtitleNumber.Extract(raw));
        }

        Add(SubtitleNumber.Extract(item.Name));
        Add(SubtitleNumber.Extract(mediaPath));

        return numbers;
    }

    private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".sub", ".vtt", ".sup" };

    private static bool HasExternalSubtitle(string directory, string baseName)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var extension = Path.GetExtension(file);
                if (!SubtitleExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Equals(baseName, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(baseName + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // 忽略目录访问异常
        }

        return false;
    }

    private static bool HasChineseSubtitle(string directory, string baseName)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            var patterns = new[] { "*.chi.srt", "*.zh.srt", "*.zh-cn.srt", "*.zh-tw.srt", "*.chs.srt", "*.cht.srt" };
            foreach (var pattern in patterns)
            {
                if (Directory.EnumerateFiles(directory, baseName + pattern).Any())
                {
                    return true;
                }
            }
        }
        catch
        {
            // 忽略目录访问异常
        }

        return false;
    }

    private static string Decode(byte[] bytes)
    {
        var text = new UTF8Encoding(false, false).GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
}
