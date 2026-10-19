using System.ComponentModel;
using Emby.Web.GenericEdit;
using MediaBrowser.Model.Attributes;

namespace Emby.SubtitleCat;

/// <summary>
/// 插件配置。Emby 会依据下列特性自动生成设置界面。
/// </summary>
public class PluginConfiguration : EditableOptionsBase
{
    public override string EditorTitle => "SubtitleCat 字幕";

    [DisplayName("首选语言顺序")]
    [Description("按优先级排列，用英文逗号分隔，可选 zh-CN、zh-TW、zh、en。默认 zh-CN,zh-TW,en")]
    public string PreferredLanguages { get; set; } = "zh-CN,zh-TW,en";

    [DisplayName("最多返回条数")]
    [Description("单次搜索最多返回的字幕条数，范围 1~50。默认 20")]
    public int MaxResults { get; set; } = 20;

    [DisplayName("最多检查的候选结果")]
    [Description("每个候选字幕需再请求详情页才能拿到分语言下载链接，数值越大越全但越慢。默认 8")]
    public int MaxCandidates { get; set; } = 8;

    [DisplayName("启用自动下载（新入库）")]
    [Description("媒体库有新片入库且 MetaTube 刮削成功后，自动搜索并下载中文字幕。默认关闭")]
    public bool EnableAutoDownload { get; set; } = false;

    [DisplayName("内嵌中文字幕关键字")]
    [Description("文件名或 NFO 标题末尾出现这些关键字时视为已内嵌中文字幕并跳过，用英文逗号分隔。默认 -C,-UC,-CH,CH,中文字幕,中字,内嵌字幕。例如 SONE-263-UC、ACHJ-081-C、cjod288ch")]
    public string EmbeddedChineseKeywords { get; set; } = "-C,-UC,-CH,CH,中文字幕,中字,内嵌字幕";

    [DisplayName("已有外挂字幕时跳过")]
    [Description("视频同目录下已存在同名 srt/ass/ssa/sub/vtt 字幕文件时跳过下载（避免重复）。默认开启")]
    public bool SkipWhenExternalSubtitleExists { get; set; } = true;

    [DisplayName("全库扫描只处理 MetaTube 条目")]
    [Description("开启后「全库字幕扫描」只处理带 MetaTube 番号信息的条目，避免普通影片被误识别。默认开启")]
    public bool FullScanOnlyMetaTube { get; set; } = true;

    [DisplayName("下载后刷新媒体信息")]
    [Description("字幕保存后刷新该条目的媒体信息，使外挂字幕挂载到媒体信息中。默认开启")]
    public bool RefreshMediaInfoAfterDownload { get; set; } = true;

    [DisplayName("优先简体")]
    [Description("拿到繁体字幕时自动转为简体。默认开启")]
    public bool PreferSimplified { get; set; } = true;

    [DisplayName("清洗水印与广告")]
    [Description("删除字幕中的站点水印/广告行（如 whisperJAV、hhd800.com）。默认开启")]
    public bool StripWatermarks { get; set; } = true;

    [DisplayName("只返回番号匹配的结果")]
    [Description("开启后只保留标题里包含该番号的字幕，避免刮到同名系列的其他影片。默认开启")]
    public bool StrictNumberMatch { get; set; } = true;

    [DisplayName("启用字幕翻译")]
    [Description("没有简体/繁体字幕时，下载英文或日语字幕后用 Google 翻译转成中文。默认开启")]
    public bool EnableTranslation { get; set; } = true;

    [DisplayName("翻译目标语言")]
    [Description("Google 翻译的目标语言代码，默认 zh-CN（简体中文）")]
    public string TranslateTargetLanguage { get; set; } = "zh-CN";

    [DisplayName("翻译接口地址")]
    [Description("Google 通用翻译接口，默认 https://translate.googleapis.com/translate_a/single")]
    public string TranslationEndpoint { get; set; } = "https://translate.googleapis.com/translate_a/single";

    [DisplayName("翻译专用代理")]
    [Description("访问 Google 需要代理时填写，例如 http://192.168.50.159:7890；留空则沿用上面的代理地址")]
    public string TranslationProxyUrl { get; set; } = string.Empty;

    [DisplayName("翻译请求超时（秒）")]
    [Description("单次翻译请求超时时间，默认 60 秒（字幕较长时会分批请求）")]
    public int TranslationTimeoutSeconds { get; set; } = 60;

    [DisplayName("代理地址")]
    [Description("例如 http://192.168.50.159:7890，留空则使用系统默认代理")]
    public string ProxyUrl { get; set; } = string.Empty;

    [DisplayName("请求超时（秒）")]
    [Description("单次 HTTP 请求超时时间，默认 20 秒")]
    public int TimeoutSeconds { get; set; } = 20;
}
