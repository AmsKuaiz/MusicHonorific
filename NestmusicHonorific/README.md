# Music Honorific

把正在播放的音乐，显示成《最终幻想14》里的角色称号。

支持 **网易云音乐**、**QQ音乐**、**Apple Music**、**Spotify**。

![icon](NestmusicHonorific/images/icon.png)

## 效果

```
♪正在听 - 网易云音乐♪
♪ 歌名 - 歌手♪
```

多行会自动轮播，切歌时从第一行重新开始；暂停播放称号自动隐藏。

## 特点

- **不碰任何账号**：不需要登录、不需要 API 密钥、不需要额外的播放器
- **自动识别播放器**：哪个在出声就用哪个，网易云开着但暂停时不会抢走 Spotify
- **多行轮播**：一行静止，多行轮换，每行停留时间可调
- **超长自动处理**：称号超过 32 字时自动只留歌名，或截断
- **颜色交给 Honorific**：单色、双色渐变、16 套内置色板、发光，全部由 Honorific 渲染

## 依赖

需要 **[Honorific](https://github.com/Caraxi/Honorific)**（官方插件，在 Dalamud 插件列表里直接搜得到）。

没有它本插件无法修改任何称号，插件窗口顶部会提示。

## 安装

### 方式一：第三方仓库（推荐）

Dalamud 中打开：

```
/xlplugins → 设置 → 实验性功能 → 第三方插件仓库 → 添加
```

填入：

```
https://raw.githubusercontent.com/AmsKuaiz/MusicHonorific/main/repo.json
```

然后在插件列表里搜索 **Music Honorific** 安装即可。

### 方式二：手动装

下载 [最新 Release](https://github.com/AmsKuaiz/MusicHonorific/releases) 里的 `NestmusicHonorific.zip`，
解压后在 Dalamud 里用「开发插件」的方式加载 `NestmusicHonorific.dll`。

> `bridge` 文件夹必须和 DLL 放在同一目录。

## 使用

游戏内输入 `/nestmusic` 打开设置。

### 占位符

| 占位符 | 含义 |
|---|---|
| `{song}` | 歌名 |
| `{artist}` | 歌手 |
| `{app}` | 正在使用的播放器（网易云音乐 / Spotify / …） |

### 设置项

- **轮询间隔**：多久检查一次；多行轮播时也是每行的停留时间（最低 1 秒）
- **称号内容**：一行一句，多行自动轮播
- **超过 32 字时**：跳过 / 只显示歌名 / 直接截断
- **放在名字前面**：勾上是前缀，不勾占称号位
- **停止播放后撤掉称号**：可设置延迟秒数
- **颜色与效果**：单色、Two Color、Honorific 内置色板、发光

## 工作原理

音乐信息有两条来源，按优先级使用：

1. **系统媒体会话（SMTC）**：Apple Music、Spotify 等会主动上报，能看到歌名、歌手和播放状态
2. **播放器窗口标题**：网易云、QQ音乐这类窗口标题就是「歌名 - 歌手」

判断「谁在播放」则通过 Windows 的音频会话（看哪个进程真的在出声），
所以同时开着多个播放器也不会认错。

> 系统媒体会话由同目录下的 `SmtcBridge.exe` 独立进程查询。
> 原因：Dalamud 插件重载时重复加载 WinRT 运行库会报
> `Attempt to update previously set global instance`，放到独立进程可以彻底避开。

## 构建

需要 .NET SDK 10 与 Dalamud 开发环境。

```powershell
# 插件本体
dotnet build NestmusicHonorific\NestmusicHonorific.csproj -c Release

# 媒体会话桥接
dotnet publish SmtcBridge\SmtcBridge.csproj -c Release -r win-x64 --self-contained false
```

把 `SmtcBridge` 的 publish 产物放进插件目录下的 `bridge\` 子文件夹。

## 已知限制

- 称号上限 32 字（Honorific 的 `MaxTitleLength`），超了会被跳过或截断
- 网易云不注册系统媒体会话，只能靠窗口标题，暂停状态靠音频静音判断（约 1 秒延迟）
- 「点歌」功能暂未实现，原因见 [UAT-点歌功能.md](NestmusicHonorific/UAT-点歌功能.md)

## 许可

MIT
