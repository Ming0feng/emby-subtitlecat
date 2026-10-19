using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;

namespace Emby.SubtitleCat;

/// <summary>
/// Emby 在安装新字幕提供程序时，会默认在所有支持的媒体库中启用它。
/// 这里反向处理：首次安装时关闭所有电影库的 SubtitleCat，之后新建的电影库也自动关闭。
/// 用户手动勾选启用后不再干预（库 ID 会记录在插件配置目录的 json 中）。
/// </summary>
public static class LibraryDefaults
{
    private const string ProviderName = "SubtitleCat";
    private const string StateFileName = "Emby.SubtitleCat.libraries.json";

    public static void Apply(ILibraryManager libraryManager, IApplicationPaths applicationPaths, ILogger logger)
    {
        try
        {
            ApplyCore(libraryManager, applicationPaths, logger);
        }
        catch (Exception ex)
        {
            logger.ErrorException("SubtitleCat 初始化媒体库默认设置失败", ex);
        }
    }

    private static void ApplyCore(ILibraryManager libraryManager, IApplicationPaths applicationPaths, ILogger logger)
    {
        var directory = applicationPaths.PluginConfigurationsPath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var statePath = Path.Combine(directory, StateFileName);

        LibraryState? state = null;
        if (File.Exists(statePath))
        {
            try
            {
                state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(statePath));
            }
            catch
            {
                state = null;
            }
        }

        var firstRun = state is null;
        state ??= new LibraryState();

        var folders = libraryManager.GetVirtualFolders()
            .Where(f => string.Equals(f.CollectionType, "movies", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var changed = firstRun;
        var disabledCount = 0;

        foreach (var folder in folders)
        {
            var key = GetKey(folder);
            if (key is null)
            {
                continue;
            }

            var known = state.Libraries.Contains(key, StringComparer.OrdinalIgnoreCase);
            if (!known)
            {
                // 首次安装或新建的媒体库：默认关闭 SubtitleCat
                if (Disable(libraryManager, folder, logger))
                {
                    disabledCount++;
                    changed = true;
                }

                state.Libraries.Add(key);
                changed = true;
            }
        }

        if (changed)
        {
            File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (firstRun || disabledCount > 0)
        {
            logger.Info("SubtitleCat: 媒体库默认设置完成，本次检查 {0} 个电影库，关闭 {1} 个", folders.Count, disabledCount);
        }
    }

    private static bool Disable(ILibraryManager libraryManager, VirtualFolderInfo folder, ILogger logger)
    {
        var options = folder.LibraryOptions ?? new LibraryOptions();
        var disabled = (options.DisabledSubtitleFetchers ?? Array.Empty<string>()).ToList();
        if (disabled.Any(d => d.Equals(ProviderName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var collectionFolder = ResolveFolder(libraryManager, folder);
        if (collectionFolder is null)
        {
            logger.Warn("SubtitleCat: 未找到媒体库「{0}」的配置对象，跳过默认关闭", folder.Name);
            return false;
        }

        disabled.Add(ProviderName);
        options.DisabledSubtitleFetchers = disabled.ToArray();
        collectionFolder.UpdateLibraryOptions(options);
        logger.Info("SubtitleCat: 已默认关闭媒体库「{0}」的 SubtitleCat 字幕下载器", folder.Name);
        return true;
    }

    private static CollectionFolder? ResolveFolder(ILibraryManager libraryManager, VirtualFolderInfo folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.ItemId))
        {
            if (Guid.TryParse(folder.ItemId, out var guid))
            {
                return libraryManager.GetItemById(guid) as CollectionFolder;
            }

            if (long.TryParse(folder.ItemId, out var id))
            {
                return libraryManager.GetItemById(id) as CollectionFolder;
            }
        }

        if (!string.IsNullOrWhiteSpace(folder.Id) && Guid.TryParse(folder.Id, out var folderGuid))
        {
            return libraryManager.GetItemById(folderGuid) as CollectionFolder;
        }

        return null;
    }

    private static string? GetKey(VirtualFolderInfo folder)
    {
        if (!string.IsNullOrWhiteSpace(folder.ItemId))
        {
            return folder.ItemId;
        }

        return string.IsNullOrWhiteSpace(folder.Id) ? null : folder.Id;
    }

    private sealed class LibraryState
    {
        public int Version { get; set; } = 1;

        public List<string> Libraries { get; set; } = new();
    }
}
