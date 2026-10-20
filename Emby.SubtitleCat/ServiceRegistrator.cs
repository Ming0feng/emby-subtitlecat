#if !__EMBY__
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Emby.SubtitleCat;

/// <summary>
/// Jellyfin 没有 Emby 的 IServerEntryPoint，改为注册一个常驻服务来监听新片入库事件。
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<AutoSubtitleService>();
    }
}
#endif
