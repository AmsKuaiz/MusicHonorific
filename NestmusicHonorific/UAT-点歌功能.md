# 点歌功能 · UAT（暂缓）

> 状态：**搁置**，等网易云开放更多接口再说。
> 写于 2026-10-27 凌晨。这里记的是当时的全部调研结论，别再从零查一遍。

## 想要的效果

其他玩家在游戏里点歌 → 称号显示谁点了什么 → **自动在网易云里播放那首歌**。

## 结论：现在做不到（播放那半）

调研过的三条路，全部堵死：

| 方案 | 结果 |
|---|---|
| `ncm-cli`（网易云官方 CLI，走开放平台凭证） | `play` 返回成功，但 mpv 从不启动。扒包发现真正的播放器是 **TUI**，而 TUI 明确报错：`TUI 播放器必须在交互式终端（TTY）中运行` —— 没法从插件后台拉起 |
| `orpheus://` 协议（客户端自己的唤起协议） | 协议注册着（`"D:\网易云音乐\CloudMusic\cloudmusic.exe" --webcmd="%1"`），但直接调用客户端**不理**，歌不变 |
| 私有 API（weapi/eapi + cookie） | 未采用。非官方、易断、有封号风险 |

**根因**：Spotify 有官方 `/me/player/*`（读+控制），网易云开放平台只给**内容**（搜歌、播放地址、歌单、播客、云盘），**不给遥控器**。

## 已经跑通的部分（可以直接拿来用）

- **读当前播放**：`C:\Users\32811\.dsh\...` 不用接口，读网易云客户端**窗口标题**就是「歌名 - 歌手」，切歌实时变。
  - 实现见 `TrackReader.cs`
  - 注意：客户端有多个 `cloudmusic` 进程，只有一个带 `MainWindowTitle`
- **改称号**：`Honorific.SetCharacterTitle(int 角色索引, string TitleData的JSON)`，已封装在 `Plugin.cs`

## 如果将来接口开了 / 换方案

### 方案 A：网易云真开放了播放控制

需要加的东西：

1. `Configuration.cs` 加一个「点歌」开关
2. 新文件 `SongRequests.cs`：
   - 监听聊天：`IChatGui.ChatMessage`，过滤 `Tell` 频道，匹配 `!点歌 歌名`
   - 解析后入队，记录 `(歌名, 点歌人, 时间)`
3. `Plugin.cs` 里把队列接到称号轮播（插一行「♪ 某某 点了「歌名」」）
4. 播放调用处替换成新的 API 客户端

### 方案 B：只做「点歌板」，不做播放

不用等任何接口，现在就能做：

- 接聊天命令 → 称号显示点歌人和歌名
- 私聊回一句「记下了」
- 插件窗口里列一个点歌队列，点一下用 `orpheus://song/<originalId>` 试着唤起（成不成看客户端）

`originalId` 从哪来：`ncm-cli search song --keyword X` 返回里有 `originalId` 和 `id`（加密 ID）。

### 方案 C：换播放器

如果哪天能接受换成别的播放器（比如 Spotify、或支持 SMTC 的本地播放器），
那「读到什么」可以用 SMTC（`Windows.Media.Control`）统一拿，控制也一并有了。

## 环境备忘（都还装着，别重装）

```
.NET SDK 9.0.320 + 10.0.403
ncm-cli 0.1.7   C:\Users\32811\dsh-plugins\ncm-cli\
                入口 node_modules\@music163\ncm-cli\dist\index.js
凭证            ~\.dsh\netease-app.txt（appId + privateKey，ACL 已锁）
                已登录：ncm-cli login 显示「已登录实名账号」
mpv 0.41.0      C:\Program Files\MPV Player\mpv.exe（也在 C:\mpv\）
                已加进用户 PATH
```

⚠️ 那份 privateKey 曾在聊天里直接贴过，**建议主人去开放平台重置一次**。
