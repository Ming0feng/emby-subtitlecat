using System;
#if !__EMBY__
using System.Collections.Generic;
#endif
using MediaBrowser.Common.Plugins;

#if __EMBY__
using MediaBrowser.Common;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
#else
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
#endif

namespace Emby.SubtitleCat;

/// <summary>
/// 插件入口。Emby 依据 <see cref="PluginConfiguration"/> 的特性自动生成设置界面；
/// Jellyfin 使用内嵌的 configPage.html。
/// </summary>
#if __EMBY__
public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage
#else
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
#endif
{
#if __EMBY__
    public Plugin(IApplicationHost applicationHost) : base(applicationHost)
    {
        Instance = this;
    }

    public PluginConfiguration Configuration => GetOptions();
#else
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }
#endif

    public static Plugin? Instance { get; private set; }

    public override string Name => "SubtitleCat";

    public override Guid Id => Guid.Parse("b7f2c5d1-3a4e-4b6f-9c8d-2e1f0a3b4c5d");

    public override string Description => "按番号从 subtitlecat.com 搜索并下载字幕";

#if __EMBY__
    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    /// <summary>
    /// 返回内嵌的 thumb.png。之前返回 null，而类仍声明了 IHasThumbImage，
    /// Emby 请求 /Plugins/{id}/Thumb 时拿不到流，直接 500。
    /// </summary>
    public System.IO.Stream? GetThumbImage() =>
        GetType().Assembly.GetManifestResourceStream("Emby.SubtitleCat.thumb.png");
#else
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = "Emby.SubtitleCat.Configuration.configPage.html"
            }
        };
    }
#endif
}
