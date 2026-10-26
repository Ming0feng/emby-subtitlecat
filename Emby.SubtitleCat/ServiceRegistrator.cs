#if !__EMBY__
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Emby.SubtitleCat;

/// <summary>
/// Jellyfin 没有 Emby 的 IServerEntryPoint，改为注册一个常驻服务来监听新片入库事件。
/// 同时必须把 SubtitleCatProvider 注册进 DI：Jellyfin 的 SubtitleManager 通过构造函数
/// 注入 IEnumerable&lt;ISubtitleProvider&gt;，只注册后台服务的话 /Libraries/AvailableOptions
/// 返回的 SubtitleFetchers 会是空数组，字幕下载器列表里看不到本插件，手动搜字幕功能不存在。
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<AutoSubtitleService>();
        serviceCollection.AddSingleton<ISubtitleProvider, SubtitleCatProvider>();
    }
}
#endif
