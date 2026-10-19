using System;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Controller.Plugins;

namespace Emby.SubtitleCat;

/// <summary>Emby 插件入口。配置界面由 Emby 依据 PluginConfiguration 的特性自动生成。</summary>
public class Plugin : BasePluginSimpleUI<PluginConfiguration>, IHasThumbImage
{
    public Plugin(IApplicationHost applicationHost) : base(applicationHost)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public PluginConfiguration Configuration => GetOptions();

    public override string Name => "SubtitleCat";

    public override Guid Id => Guid.Parse("b7f2c5d1-3a4e-4b6f-9c8d-2e1f0a3b4c5d");

    public override string Description => "按番号从 subtitlecat.com 搜索并下载字幕";

    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    public System.IO.Stream? GetThumbImage() => null;
}
