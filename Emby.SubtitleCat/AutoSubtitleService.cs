using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;

#if __EMBY__
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#endif

namespace Emby.SubtitleCat;

/// <summary>
/// 自动任务：媒体库有新片入库且 MetaTube 刮削成功后，自动搜索并下载中文字幕，
/// 随后刷新该条目的媒体信息，让外挂字幕挂载到媒体信息上。
/// </summary>
#if __EMBY__
public class AutoSubtitleService : IServerEntryPoint, IDisposable
#else
public class AutoSubtitleService : IHostedService, IDisposable
#endif
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
#if __EMBY__
    private readonly IFileSystem _fileSystem;
#else
    private readonly IDirectoryService _directoryService;
#endif
    private readonly IApplicationPaths _applicationPaths;
    private readonly PluginLogger _logger;

    private readonly SemaphoreSlim _throttle = new(2, 2);
    private readonly ConcurrentDictionary<string, byte> _pending = new();

#if __EMBY__
    public AutoSubtitleService(ILibraryManager libraryManager, IProviderManager providerManager,
        IFileSystem fileSystem, IApplicationPaths applicationPaths, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _fileSystem = fileSystem;
        _applicationPaths = applicationPaths;
        _logger = new PluginLogger(logManager.GetLogger("SubtitleCat"));
    }

    public void Run() => Listen();
#else
    public AutoSubtitleService(ILibraryManager libraryManager, IProviderManager providerManager,
        IDirectoryService directoryService, IApplicationPaths applicationPaths,
        ILogger<AutoSubtitleService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _directoryService = directoryService;
        _applicationPaths = applicationPaths;
        _logger = new PluginLogger(logger);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Listen();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unlisten();
        return Task.CompletedTask;
    }
#endif

    private void Listen()
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;

#if __EMBY__
        // Emby 安装新字幕提供程序时会默认全库启用；首次安装/新建电影库时反向关闭（不阻塞启动）。
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            LibraryDefaults.Apply(_libraryManager, _applicationPaths, _logger);
        });
#endif
    }

    private void Unlisten()
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
    }

    private void OnItemChanged(object sender, ItemChangeEventArgs e)
    {
        try
        {
#if __EMBY__
            // 新建电影媒体库时，等配置落盘后默认关闭 SubtitleCat
            if (e.Item is CollectionFolder folder
                && string.Equals(folder.CollectionType, "movies", StringComparison.OrdinalIgnoreCase))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    LibraryDefaults.Apply(_libraryManager, _applicationPaths, _logger);
                });
                return;
            }
#endif

            var config = Plugin.Instance?.Configuration;
            if (config is null || !config.EnableAutoDownload)
            {
                return;
            }

            var item = e.Item;
            if (item is null || item is not Movie)
            {
                return;
            }

            // 只处理 MetaTube 刮削成功的条目
            if (!item.ProviderIds.ContainsKey("MetaTube"))
            {
                return;
            }

            // 该条目所属媒体库需启用 SubtitleCat 字幕下载器
            if (!SubtitleFetcher.IsProviderEnabled(item, _libraryManager))
            {
                return;
            }

            if (!_pending.TryAdd(item.Id.ToString(), 0))
            {
                return; // 正在处理
            }

            _ = Task.Run(async () =>
            {
                await _throttle.WaitAsync().ConfigureAwait(false);
                try
                {
                    await ProcessAsync(item, config).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("SubtitleCat 自动字幕处理失败: {0}", ex, item.Name);
                }
                finally
                {
                    _pending.TryRemove(item.Id.ToString(), out _);
                    _throttle.Release();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.ErrorException("SubtitleCat 事件处理异常", ex);
        }
    }

    private async Task ProcessAsync(BaseItem item, PluginConfiguration config)
    {
        var result = await SubtitleFetcher.FetchAsync(item, config, CancellationToken.None).ConfigureAwait(false);
        switch (result.Status)
        {
            case SubtitleFetchStatus.Downloaded:
                _logger.Info("SubtitleCat: {0} 已下载字幕 -> {1}", result.Number, result.TargetPath);
                if (config.RefreshMediaInfoAfterDownload)
                {
#if __EMBY__
                    await SubtitleFetcher
                        .RefreshMediaInfoAsync(item, _libraryManager, _providerManager, _fileSystem, _logger,
                            CancellationToken.None)
                        .ConfigureAwait(false);
#else
                    await SubtitleFetcher
                        .RefreshMediaInfoAsync(item, _libraryManager, _providerManager, _directoryService, _logger,
                            CancellationToken.None)
                        .ConfigureAwait(false);
#endif
                }

                await MetaTubeBadge.Apply(item, _libraryManager, _applicationPaths, _logger).ConfigureAwait(false);
                break;
            case SubtitleFetchStatus.NoCandidate:
                _logger.Info("SubtitleCat: {0} 没有可用的简体/繁体字幕", result.Number);
                break;
        }
    }

    public void Dispose()
    {
        Unlisten();
        _throttle.Dispose();
    }
}
