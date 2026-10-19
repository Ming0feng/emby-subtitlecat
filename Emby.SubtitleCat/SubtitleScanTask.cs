using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.SubtitleCat;

/// <summary>
/// 计划任务：扫描整个媒体库，为缺少中文字幕的影片按番号抓取字幕。
/// 默认不设置触发器，需在 Emby 后台「计划任务」中手动运行或自行添加触发时间。
/// </summary>
public class SubtitleScanTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger _logger;

    public SubtitleScanTask(ILibraryManager libraryManager, IProviderManager providerManager,
        IFileSystem fileSystem, IApplicationPaths applicationPaths, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _applicationPaths = applicationPaths;
        _logger = logManager.GetLogger("SubtitleCat");
    }

    public string Name => "SubtitleCat 全库字幕扫描";

    public string Key => "SubtitleCatFullLibraryScan";

    public string Description => "遍历各电影媒体库，按番号从 SubtitleCat 抓取缺失的中文字幕（剧集库不支持该下载器，不会处理）。";

    public string Category => "SubtitleCat";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.Warn("SubtitleCat 全库扫描：插件配置未就绪，跳过");
            return;
        }

        // SubtitleCatProvider 只声明支持电影，剧集库不会出现该下载器，因此这里也只扫电影
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsFolder = false,
            IncludeItemTypes = new[] { "Movie" }
        };

        var items = _libraryManager.GetItemList(query, cancellationToken)
            .Where(i => i.LocationType == LocationType.FileSystem)
            .Where(i => !string.IsNullOrWhiteSpace(i.Path))
            .Where(i => !config.FullScanOnlyMetaTube || i.ProviderIds.ContainsKey("MetaTube"))
            .ToList();

        _logger.Info("SubtitleCat 全库扫描开始：{0} 个条目（仅 MetaTube 刮削条目：{1}）",
            items.Count, config.FullScanOnlyMetaTube);

        int downloaded = 0, exists = 0, attached = 0, embedded = 0, externalSub = 0, noNumber = 0, noCandidate = 0;
        int notEnabled = 0, failed = 0;

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(items.Count == 0 ? 100 : 100.0 * i / items.Count);

            var item = items[i];

            // 只处理所在媒体库启用了 SubtitleCat 字幕下载器的条目
            if (!SubtitleFetcher.IsProviderEnabled(item, _libraryManager))
            {
                notEnabled++;
                continue;
            }

            SubtitleFetchResult result;
            try
            {
                result = await SubtitleFetcher.FetchAsync(item, config, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("SubtitleCat 全库扫描处理失败: {0}", ex, item.Name);
                result = new SubtitleFetchResult { Status = SubtitleFetchStatus.Failed, Message = ex.Message };
            }

            switch (result.Status)
            {
                case SubtitleFetchStatus.Downloaded:
                    downloaded++;
                    _logger.Info("SubtitleCat [{0}/{1}] 已下载 {2} -> {3}",
                        i + 1, items.Count, result.Number, result.TargetPath);

                    if (config.RefreshMediaInfoAfterDownload)
                    {
                        await SubtitleFetcher
                            .RefreshMediaInfoAsync(item, _libraryManager, _providerManager, _fileSystem, _logger,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    // 下载完成后调用 MetaTube 补角标
                    MetaTubeBadge.Apply(item, _libraryManager, _applicationPaths, _logger);

                    break;
                case SubtitleFetchStatus.AlreadyExists:
                    exists++;
                    // 字幕文件已存在但 Emby 没挂载（旧版本下载的字幕）：补挂一次
                    if (config.RefreshMediaInfoAfterDownload && !SubtitleFetcher.HasExternalSubtitleStream(item))
                    {
                        await SubtitleFetcher
                            .RefreshMediaInfoAsync(item, _libraryManager, _providerManager, _fileSystem, _logger,
                                cancellationToken)
                            .ConfigureAwait(false);
                        attached++;
                    }

                    // 以前下载的字幕也要补角标（已有角标会自动跳过）
                    MetaTubeBadge.Apply(item, _libraryManager, _applicationPaths, _logger);

                    break;
                case SubtitleFetchStatus.EmbeddedChinese:
                    embedded++;
                    _logger.Info("SubtitleCat [{0}/{1}] {2} 已内嵌中文字幕，跳过", i + 1, items.Count, item.Name);
                    break;
                case SubtitleFetchStatus.ExternalSubtitleExists:
                    externalSub++;
                    break;
                case SubtitleFetchStatus.NoNumber:
                    noNumber++;
                    break;
                case SubtitleFetchStatus.NoCandidate:
                    noCandidate++;
                    _logger.Info("SubtitleCat [{0}/{1}] {2} 无可用字幕", i + 1, items.Count, item.Name);
                    break;
                default:
                    failed++;
                    break;
            }
        }

        progress?.Report(100);
        _logger.Info(
            "SubtitleCat 全库扫描完成：新下载 {0}，已有中文字幕 {1}（补挂 {2}），内嵌中文跳过 {3}，已有外挂字幕跳过 {4}，未启用字幕库跳过 {5}，无番号 {6}，无可用字幕 {7}，失败 {8}",
            downloaded, exists, attached, embedded, externalSub, notEnabled, noNumber, noCandidate, failed);
    }
}
