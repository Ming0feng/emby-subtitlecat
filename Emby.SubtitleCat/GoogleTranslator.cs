using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.SubtitleCat;

/// <summary>
/// 基于 Google 翻译公开接口（translate_a/single，即浏览器用的通用接口）的翻译器。
/// 按行批量提交，尽量保持时间轴与行数不变。
/// </summary>
public sealed class GoogleTranslator : IDisposable
{
    private const int MaxBatchChars = 3000;

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _target;

    public GoogleTranslator(string endpoint, string targetLanguage, string proxyUrl, TimeSpan timeout)
    {
        _endpoint = string.IsNullOrWhiteSpace(endpoint)
            ? "https://translate.googleapis.com/translate_a/single"
            : endpoint.Trim();
        _target = string.IsNullOrWhiteSpace(targetLanguage) ? "zh-CN" : targetLanguage.Trim();

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl.Trim());
            handler.UseProxy = true;
        }

        _http = new HttpClient(handler) { Timeout = timeout };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
    }

    /// <summary>逐行翻译，返回与输入等长的数组。</summary>
    public async Task<string[]> TranslateLinesAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var result = new string[lines.Count];
        var batch = new List<int>();
        var chars = 0;

        async Task FlushAsync()
        {
            if (batch.Count == 0)
            {
                return;
            }

            var source = batch.Select(i => lines[i]).ToArray();
            var joined = string.Join("\n", source);
            string? translated = null;
            try
            {
                translated = await TranslateTextAsync(joined, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                translated = null;
            }

            var parts = translated?.Split('\n');
            if (parts is not null && parts.Length == source.Length)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    result[batch[i]] = parts[i];
                }
            }
            else
            {
                // 行数对不上时逐行翻译，保证时间轴不错位
                foreach (var index in batch)
                {
                    try
                    {
                        result[index] = await TranslateTextAsync(lines[index], cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        result[index] = lines[index];
                    }
                }
            }

            batch.Clear();
            chars = 0;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var len = lines[i].Length + 1;
            if (chars + len > MaxBatchChars && batch.Count > 0)
            {
                await FlushAsync().ConfigureAwait(false);
            }

            batch.Add(i);
            chars += len;
        }

        await FlushAsync().ConfigureAwait(false);
        return result;
    }

    private async Task<string> TranslateTextAsync(string text, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, string>
        {
            ["client"] = "gtx",
            ["sl"] = "auto",
            ["tl"] = _target,
            ["dt"] = "t",
            ["q"] = text
        };

        using var content = new FormUrlEncodedContent(body);
        using var response = await _http.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        var sb = new StringBuilder();
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            foreach (var segment in doc.RootElement[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0
                    && segment[0].ValueKind == JsonValueKind.String)
                {
                    sb.Append(segment[0].GetString());
                }
            }
        }

        return sb.ToString();
    }

    public void Dispose() => _http.Dispose();
}
