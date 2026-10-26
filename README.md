# Music Honorific

把正在播放的音乐，显示成《最终幻想14》里的角色称号。

支持 **网易云音乐**、**QQ音乐**、**Apple Music**、**Spotify**。

![icon](MusicHonorific/images/icon.png)

## 效果

```
♪正在听 - 网易云音乐♪
♪ 歌名 - 歌手♪
```

多行自动轮播，切歌时从第一行重新开始；暂停播放称号会自动隐藏。

## 特点

- **不碰账号**：不需要登录、不需要 API 密钥、不需要额外播放器
- **自动识别播放器**：按「谁真的在出声」判断，网易云开着但暂停时不会抢走 Spotify
- **多行轮播**：一行静止，多行轮换，每行停留时间可调
- **超长自动处理**：称号超过 32 字时自动只留歌名，或截断
- **颜色交给 Honorific**：单色、双色渐变、16 套内置色板、发光，全部由 Honorific 渲染

---

## 复制这一行，添加到 Dalamud 仓库

点代码块右上角的复制按钮，直接粘到 Dalamud 的「第三方插件仓库」里：

```
https://raw.githubusercontent.com/AmsKuaiz/MusicHonorific/main/repo.json
```

具体步骤见下方 [安装本插件](#安装本插件)。

---

## 安装前：先装 Honorific

**这一步不能跳过。** 本插件只负责「读歌名」，把称号真正画到角色头上的是
[Honorific](https://github.com/Caraxi/Honorific)。没有它，本插件能装、能开窗口，但改不了任何称号。

### 怎么装 Honorific

1. 游戏内输入 `/xlplugins` 打开插件安装器
2. 切换到 **插件列表 / Available Plugins**
3. 搜索框输入 `Honorific`
4. 找到作者是 **Caraxi** 的那个，点 **安装**
5. 在 **已安装 / Installed Plugins** 里确认它是**启用**状态
6. 建议打开它一次（`/honorific`），确认界面正常

> Honorific 是**官方插件**，Dalamud 自带列表里就有，不需要添加任何第三方仓库。

### 不装会怎样

本插件窗口顶部会出现红色提示：

```
⚠ 没有找到 Honorific 插件
称号功能全部依赖它，现在装了也改不了称号。
```

插件本体照常加载，只是称号相关功能全部无效。

---

## 安装本插件

### 方式一：第三方仓库（推荐，可自动更新）

1. 游戏内输入 `/xlplugins`
2. 点右上角 **设置 / Settings**
3. 切到 **实验性功能 / Experimental**
4. 找到 **第三方插件仓库 / Custom Plugin Repositories**
5. 填入下面这行，点 **加号（+）** 添加：

```
https://raw.githubusercontent.com/AmsKuaiz/MusicHonorific/main/repo.json
```

6. 点 **保存并关闭 / Save and Close**
7. 回到插件列表，搜索 `Music Honorific`
8. 点 **安装**，确认它是**启用**状态

> 添加后搜不到的话，重新打开一次「实验性功能」页面，或点插件列表右上角的刷新。

### 方式二：手动安装（不想加仓库时）

1. 打开 [最新 Release](https://github.com/AmsKuaiz/MusicHonorific/releases)
2. 下载 **Assets** 里的 `MusicHonorific.zip`（**不是** Source code 那两个）
3. 解压到固定位置，例如：

```
D:\DalamudPlugins\MusicHonorific\
```

4. 解压后目录应该是这样（**bridge 必须和 dll 在同一层**）：

```
MusicHonorific\
├── MusicHonorific.dll
├── MusicHonorific.json
├── bridge\
│   ├── SmtcBridge.exe
│   ├── SmtcBridge.dll
│   ├── SmtcBridge.runtimeconfig.json
│   ├── Microsoft.Windows.SDK.NET.dll
│   └── WinRT.Runtime.dll
└── images\
    └── icon.png
```

5. 游戏内 `/xlplugins` → 设置 → **实验性功能** → **开发插件位置 / Dev Plugin Locations**
6. 点加号，选中 `MusicHonorific.dll` 的**完整路径**
7. 在开发插件列表里启用 **Music Honorific**

> ⚠️ `bridge` 文件夹千万别丢。它负责查系统媒体会话，
> 少了它，Apple Music、Spotify 这类窗口标题不带歌名的播放器就读不到。

---

## 使用

游戏内输入 `/mh` 或 `/musichonorific` 打开设置窗口。

### 占位符

| 占位符 | 含义 |
|---|---|
| `{song}` | 歌名 |
| `{artist}` | 歌手 |
| `{app}` | 正在使用的播放器（网易云音乐 / QQ音乐 / Apple Music / Spotify） |

### 设置项

| 设置 | 作用 |
|---|---|
| **启用** | 关掉后立刻撤掉称号 |
| **轮询间隔** | 多久检查一次；多行轮播时也是每行停留时间（最低 1 秒） |
| **称号内容** | 一行一句。一行静止，多行自动轮播 |
| **超过 32 字时** | 跳过这一行 / 只显示歌名（推荐）/ 直接截断 |
| **放在名字前面** | 勾上是名字前缀，不勾则占称号位（名字后面） |
| **停止播放后撤掉称号** | 停止多久后隐藏，0 = 立刻 |
| **使用自定义颜色** | 关掉则使用游戏原生称号颜色 |
| **渐变** | None / Two Color / 16 套 Honorific 内置色板 |
| **动画** | 脉冲 / 波浪 / 静态（由 Honorific 渲染） |
| **发光** | 独立开关 + 颜色 |

---

## 工作原理

音乐信息有两条来源，按优先级使用：

1. **系统媒体会话（SMTC）**
   Apple Music、Spotify 等会主动向系统上报播放内容，
   能看到歌名、歌手与播放状态（Playing / Paused）。

2. **播放器窗口标题**
   网易云、QQ音乐会把「歌名 - 歌手」写在窗口标题上，直接读标题。

判断「谁在播放」用的是 **Windows 音频会话**（CoreAudio）——
看哪个进程当前真的在输出声音，所以同时开多个播放器也不会认错。

### 为什么有一个 SmtcBridge.exe

系统媒体会话属于 WinRT。Dalamud 插件在游戏进程内反复重载时，
WinRT 运行库会重复加载并抛出：

```
InvalidOperationException: Attempt to update previously set global instance
```

所以把「查媒体会话」交给同目录下的 `SmtcBridge.exe` 独立进程，
它把结果写成 JSON，插件只读文件。插件重载多少次都不会冲突。

---

## 常见问题

**Q：装上了但称号完全不变？**
先确认 **Honorific 已安装并启用**，然后看本插件窗口顶部有没有红色提示。

**Q：Apple Music / Spotify 读不到？**
检查 `bridge` 文件夹是否和 `MusicHonorific.dll` 在同一目录。
窗口「诊断」区会显示「媒体会话读取失败：…」，按提示排查。

**Q：网易云读得到，但显示的是上一首？**
网易云暂停时窗口标题不会清空。本插件靠音频静音判断播放状态，约 1 秒延迟；
如果同时开着别的播放器，会优先选真正出声的那个。

**Q：称号只有几个字，被截断了？**
Honorific 的称号上限是 32 字，超出部分会被截断或跳过。
可以在设置里改「只显示歌名」，或缩短称号内容。

**Q：暂停后称号还在？**
设置里勾上「停止播放后撤掉称号」，并把秒数设为 0。

---


## 参考与致谢

- **[Honorific](https://github.com/Caraxi/Honorific)** — 称号渲染本体。通过其 IPC（`SetCharacterTitle` / `GetCharacterTitle` / `ClearCharacterTitle`）设置称号。
- **[SpotifyHonorific](https://github.com/Valiice/SpotifyHonorific)** — 思路来源。沿用其「向 Honorific 推送 TitleData」的做法，数据源改为本地播放器。

- Windows 平台 API：`Windows.Media.Control`（SMTC）、
  `IAudioSessionManager2` / `IAudioMeterInformation`（CoreAudio 音频会话峰值）。

### 相关地址（方便复制）

```
Honorific          https://github.com/Caraxi/Honorific
SpotifyHonorific   https://github.com/Valiice/SpotifyHonorific
Dalamud            https://github.com/goatcorp/Dalamud
本插件仓库          https://github.com/AmsKuaiz/MusicHonorific
仓库清单            https://raw.githubusercontent.com/AmsKuaiz/MusicHonorific/main/repo.json
```

## 许可

MIT
