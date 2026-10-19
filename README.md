# Emby.SubtitleCat

按番号从 [subtitlecat.com](https://subtitlecat.com) 搜索并下载中文字幕的 Emby 插件。

插件会把「番号提取 → 字幕搜索 → 语言筛选 → 清洗/转简体/翻译 → 写入同目录 srt → 刷新挂载」整条链路自动化，并与 MetaTube 刮削结果联动。

![version](https://img.shields.io/badge/version-1.1.0.0-blue)
![framework](https://img.shields.io/badge/.NET-net6.0-purple)
![emby](https://img.shields.io/badge/Emby-4.8%20%7C%204.9-green)

## 功能

- **字幕提供程序**：在 Emby 的字幕搜索里按番号检索 SubtitleCat，返回按语言优先级排序的结果。
- **新片自动下载**：媒体库有新片入库且 MetaTube 刮削成功后，自动抓取中文字幕（默认关闭）。
- **全库扫描**：计划任务遍历电影库，为缺失中文字幕的条目批量补齐。
- **语言优先级**：默认 `zh-CN,zh-TW,en`，简体优先，其次繁体，最后英文。
- **繁转简**：命中繁体字幕时使用内置字符表（`TSCharacters.txt`）转为简体。
- **字幕翻译**：没有中文时，下载英文/日文字幕并逐条调用 Google 翻译，保持时间轴不变。
- **番号严格匹配**：只保留标题中包含该番号的结果，避免刮到同名系列。
- **内嵌中文识别**：文件名或 NFO 标题带 `-C`、`-UC`、`-CH`、`中文字幕` 等标记时判定已内嵌，直接跳过。
- **外挂字幕去重**：同目录已存在 `srt/ass/ssa/sub/vtt` 时跳过。
- **水印清洗**：删除字幕中的站点水印与广告行（如 `whisperJAV`、`hhd800.com`）。
- **角标联动**：下载成功后复用 MetaTube 的图片角标能力，为主图补上「中文字幕」角标。
- **媒体库默认关闭**：Emby 安装新字幕提供程序时会默认全库启用，插件反向处理为默认关闭，需手动勾选后才生效。

## 工作流程

```
媒体文件 / 条目名
      │
      ▼
番号提取  hhd800.com@SSIS-033-C.mp4 → SSIS-033
      │        （支持 FC2-PPV-1234567）
      ▼
SubtitleCat 搜索 → 严格番号过滤 → 取详情页各语言变体
      │
      ▼
语言优先级排序（zh-CN > zh-TW > en > ja）
      │
      ▼
后处理：清洗水印 → 繁体转简体 → 英文/日文翻译
      │
      ▼
写入同目录 <文件名>.srt → 刷新媒体信息 → 挂载字幕 → 补角标
```

## 安装

1. 从 [Releases](https://github.com/Ming0feng/emby-subtitlecat/releases) 下载最新的 `Emby.SubtitleCat_x.x.x.x.zip`。
2. 解压到 Emby 插件目录：
   - macOS / Linux：`~/.config/emby-server/plugins/`
   - Windows：`%AppData%\Emby-Server\programdata\plugins\`
   - Docker：映射到容器内 `/config/plugins/`
3. 重启 Emby。
4. 进入 **控制台 → 插件 → SubtitleCat** 完成配置。
5. 进入每个电影媒体库的 **元数据 → 字幕下载器**，勾选 `SubtitleCat`。

## 配置

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| 首选语言顺序 | `zh-CN,zh-TW,en` | 逗号分隔，可选 `zh-CN`、`zh-TW`、`zh`、`en` |
| 最多返回条数 | `20` | 单次搜索返回上限，范围 1~50 |
| 最多检查的候选结果 | `8` | 每个候选需请求详情页，越大越全但越慢 |
| 启用自动下载（新入库） | 关 | 新片入库且 MetaTube 刮削成功后自动抓取 |
| 内嵌中文字幕关键字 | `-C,-UC,-CH,CH,中文字幕,中字,内嵌字幕` | 命中即视为已内嵌并跳过 |
| 已有外挂字幕时跳过 | 开 | 同目录已有字幕文件则跳过 |
| 全库扫描只处理 MetaTube 条目 | 开 | 避免普通影片被误识别 |
| 下载后刷新媒体信息 | 开 | 让外挂字幕挂载到媒体信息 |
| 优先简体 | 开 | 繁体字幕自动转简体 |
| 清洗水印与广告 | 开 | 删除站点水印/广告行 |
| 只返回番号匹配的结果 | 开 | 标题必须包含该番号 |
| 启用字幕翻译 | 开 | 无中文时翻译英文/日文字幕 |
| 翻译目标语言 | `zh-CN` | Google 翻译目标语言代码 |
| 翻译接口地址 | `https://translate.googleapis.com/translate_a/single` | Google 通用翻译接口 |
| 翻译专用代理 | 空 | 留空时沿用下方代理地址 |
| 翻译请求超时（秒） | `60` | 字幕较长时会分批请求 |
| 代理地址 | 空 | 例如 `http://127.0.0.1:7890`，留空用系统代理 |
| 请求超时（秒） | `20` | 单次 HTTP 请求超时 |

> 访问 Google 翻译通常需要代理。若字幕搜索与翻译走不同线路，可分别填写「代理地址」和「翻译专用代理」。

## 使用

### 手动搜索

在条目的字幕管理界面选择 `SubtitleCat`，输入或确认番号后搜索，按语言挑一条下载。

### 自动下载

1. 打开「启用自动下载（新入库）」。
2. 确认对应媒体库已启用 `SubtitleCat` 字幕下载器。
3. 新片被 MetaTube 刮削成功后，插件会自动抓取字幕并刷新挂载。

> 只处理带 `MetaTube` ProviderId 的电影条目；并发上限为 2。

### 全库扫描

**控制台 → 计划任务 → SubtitleCat 全库字幕扫描**，手动运行即可。任务默认不带触发器。

结束时会输出统计：新下载、已有中文字幕（补挂）、内嵌中文跳过、已有外挂字幕跳过、未启用字幕库跳过、无番号、无可用字幕、失败。

> 该任务只遍历电影库，剧集库不受支持。

## 构建

需要 .NET 6 或更高版本的 SDK。

```bash
dotnet publish Emby.SubtitleCat/Emby.SubtitleCat.csproj -c Release -o out
```

产物为 `out/Emby.SubtitleCat.dll`，连同 `Emby.SubtitleCat.pdb` 打包成 zip 即可分发。

## 兼容性

- 目标框架：`net6.0`（Emby 4.8 / 4.9 运行时为 .NET 6）
- 依赖：`MediaBrowser.Server.Core` / `MediaBrowser.Common` 4.9.1.90（`ExcludeAssets="runtime"`，不随包分发）
- 字幕来源：subtitlecat.com，仅支持电影（`VideoContentType.Movie`）

## 说明

- 本插件仅做字幕检索与后处理，不内置任何字幕资源，所有内容均来自第三方站点。
- 请遵守当地法律法规与目标站点的使用条款，仅用于个人学习与研究。
- 站点结构与接口可能随时调整，导致搜索或下载失效。
