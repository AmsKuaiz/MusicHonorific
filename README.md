# Nestmusic Honorific

把网易云音乐正在播放的歌，显示成 FF14 里你的称号。

用的是 **Honorific** 插件的 IPC，不改动 Honorific、也不碰 SpotifyHonorific 和其它插件。

## 数据从哪来

不用网易云开放平台、不用登录、不用 mpv。

直接读**网易云客户端的窗口标题**——它本身就是「歌名 - 歌手」，切歌会实时变。

```
cloudmusic 窗口标题：Headphones On - Addison Rae
        ↓
    拆成 歌名 / 歌手
        ↓
Honorific.SetCharacterTitle(index, json)
        ↓
    游戏里显示称号
```

## 怎么装（开发插件方式，不进 installedPlugins）

1. 编译：`dotnet build -c Release`
2. 产物在 `bin\Release\net10.0-windows\MusicHonorific.dll`（同目录有 `.json` 清单）
3. 游戏里 `/xlplugins` → 设置 → 实验性功能 → **开发插件位置** → 添加
   选到上面那个 `.dll` 的**完整路径**
4. 开发插件列表里会出现 **Nestmusic Honorific**，启用它

## 怎么用

- 命令：`/mh` 打开设置窗口
- 窗口里可以改：
  - 启用开关
  - 轮询间隔（也是轮播每行的停留时间）
  - 称号内容（一行静止、多行自动轮播；支持 `{song}` / `{artist}`）
  - 放在名字前 / 放称号位
  - 颜色：单色 RGB、Two Color、16 套 Honorific 内置渐变、发光
- 称号上限 32 字（Honorific 的 `MaxTitleLength`）；超了会被跳过，窗口底部红字提示
- 窗口底下会显示**当前读到的原始窗口标题**和解析结果，方便排查
- 关掉「启用」或卸载插件时，称号会被自动撤掉

## 暂缓的功能

**点歌**（其他玩家点歌 → 自动播放）暂时做不了，原因和结论都记在
[`UAT-点歌功能.md`](UAT-点歌功能.md)：网易云没有类似 Spotify `/me/player/*` 的播放控制接口。

## 依赖

- Dalamud API 15（`%APPDATA%\XIVLauncherCN\addon\Hooks\15.0.3.8\`）
- 目标框架 `net10.0-windows`
- 需要 **Honorific** 插件在跑；没有的话插件仍会加载，但不会改称号
