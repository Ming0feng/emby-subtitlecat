using System;

#if __EMBY__
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Logging;
#endif

namespace Emby.SubtitleCat;

/// <summary>
/// 日志适配层：Emby 使用 <c>MediaBrowser.Model.Logging.ILogger</c>，
/// Jellyfin 使用 <c>Microsoft.Extensions.Logging.ILogger</c>，这里统一成一套调用方式。
/// </summary>
public sealed class PluginLogger
{
    private readonly ILogger _logger;

    public PluginLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void Info(string message, params object?[] args)
    {
#if __EMBY__
        _logger.Info(message, args);
#else
        _logger.LogInformation(message, args);
#endif
    }

    public void Warn(string message, params object?[] args)
    {
#if __EMBY__
        _logger.Warn(message, args);
#else
        _logger.LogWarning(message, args);
#endif
    }

    public void Error(string message, params object?[] args)
    {
#if __EMBY__
        _logger.Error(message, args);
#else
        _logger.LogError(message, args);
#endif
    }

    public void ErrorException(string message, Exception exception, params object?[] args)
    {
#if __EMBY__
        _logger.ErrorException(message, exception, args);
#else
        _logger.LogError(exception, message, args);
#endif
    }
}
