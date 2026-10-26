using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace MusicHonorific;

public sealed class Plugin : IDalamudPlugin
{
    /// <summary>主命令。</summary>
    public const string CommandName = "/mh";

    /// <summary>别名，方便记不住缩写的人。</summary>
    public static readonly string[] CommandAliases = { "/musichonorific" };

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;

    public Configuration Config { get; }

    /// <summary>Honorific 那边的 SetCharacterTitle：<c>(角色在 ObjectTable 的索引, 标题 JSON)</c></summary>
    private ICallGateSubscriber<int, string, object>? _setTitle;
    private ICallGateSubscriber<int, object>? _clearTitle;
    private ICallGateSubscriber<int, string>? _getTitle;

    private readonly Stopwatch _pollTimer = Stopwatch.StartNew();
    private readonly Stopwatch _scrollTimer = Stopwatch.StartNew();

    /// <summary>轮播和轮询共用同一个间隔，最低 1 秒（推太快 Honorific 会闪）。</summary>
    private int EffectiveIntervalMs => Math.Max(1000, Config.PollIntervalMs);

    private int _scrollIndex;
    private bool _wasEnabled = true;
    private bool _windowOpen;

    private string _lastRawTitle = string.Empty;
    private string _matchedProcess = string.Empty;
    private string _appName = "音乐";

    /// <summary>当前是否在播放；暂停后就停止轮播。</summary>
    private bool _isPlaying;

    /// <summary>停止播放后计时，超过设定秒数就撤掉称号。</summary>
    private readonly Stopwatch _idleTimer = Stopwatch.StartNew();
    private bool _titleCleared;

    /// <summary>SMTC 缓存，由后台线程更新（UI 线程不能阻塞在 WinRT 调用上）。</summary>
    private volatile MediaSessions.NowPlaying? _smtc;

    private volatile bool _running = true;
    private Thread? _smtcThread;
    private string _bridgeDir = string.Empty;
    private string _bridgeJsonPath = string.Empty;
    private string _lastPushedText = string.Empty;
    private TrackReader.Track _current = new(string.Empty, string.Empty);

    public string LastStatus { get; private set; } = "还没开始读";
    public bool HonorificAvailable { get; private set; }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // 老配置里颜色可能是 (0,0,0)（全黑，称号会看不见）——兜底成网易云蓝
        if (Config.Color == Vector3.Zero)
            Config.Color = new Vector3(0.45f, 0.78f, 1.00f);

        // Honorific 的 IPC 即使对方没装也能「拿到订阅者」，只有真正调用才报错，
        // 所以这里查插件列表来判断对方在不在。
        HonorificAvailable = DetectHonorific();
        EnsureSubscribers();

        if (!HonorificAvailable)
        {
            ChatGui.PrintError("没有找到 Honorific 插件，Music Honorific 无法修改称号。", CommandName);
            Log.Warning("未检测到 Honorific，称号功能不可用。");
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "打开 Music Honorific 设置窗口（同 /musichonorific）",
        });

        foreach (var alias in CommandAliases)
        {
            CommandManager.AddHandler(alias, new CommandInfo(OnCommand)
            {
                HelpMessage = "打开 Music Honorific 设置窗口",
            });
        }

        // SMTC 查询交给独立进程，插件只读它写的 JSON（避免 WinRT 重复加载）
        _bridgeDir = Path.Combine(PluginInterface.AssemblyLocation.DirectoryName ?? ".", "bridge");
        _bridgeJsonPath = Path.Combine(Path.GetTempPath(), "music-honorific-bridge.json");
        StartBridge();

        _smtcThread = new Thread(SmtcLoop)
        {
            IsBackground = true,
            Name = "MusicHonorific.SMTC",
        };
        _smtcThread.Start();

        Framework.Update += OnUpdate;
        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
    }

    public void Dispose()
    {
        _running = false;
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;

        // 卸载/停用一律把称号撤掉，不留残影
        ClearTitle();

        CommandManager.RemoveHandler(CommandName);
        foreach (var alias in CommandAliases)
            CommandManager.RemoveHandler(alias);
    }

    private void OnCommand(string command, string args) => ToggleWindow();

    /// <summary>后台循环：定期读桥接进程写的媒体会话状态。</summary>
    private void SmtcLoop()
    {
        while (_running)
        {
            try
            {
                _smtc = MediaSessions.GetNowPlaying(_bridgeJsonPath);
            }
            catch
            {
                _smtc = null;
            }

            // 分小段睡，卸载时能及时退出
            for (var i = 0; i < 10 && _running; i++)
                Thread.Sleep(100);
        }
    }

    /// <summary>启动 bridge\SmtcBridge.exe；已经在跑的话新的会自己退出。</summary>
    private void StartBridge()
    {
        try
        {
            var exe = Path.Combine(_bridgeDir, "SmtcBridge.exe");
            if (!File.Exists(exe))
            {
                Log.Warning($"没找到桥接程序：{exe}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"\"{_bridgeJsonPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _bridgeDir,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "启动桥接进程失败");
        }
    }

    public void ToggleWindow() => _windowOpen = !_windowOpen;

    /// <summary>Honorific 装了并且启用了吗。</summary>
    private static bool DetectHonorific()
    {
        try
        {
            return PluginInterface.InstalledPlugins.Any(p =>
                string.Equals(p.InternalName, "Honorific", StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取三个 IPC 订阅者（重复调用安全）。</summary>
    private void EnsureSubscribers()
    {
        try
        {
            _setTitle ??= PluginInterface.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
            _clearTitle ??= PluginInterface.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
            _getTitle ??= PluginInterface.GetIpcSubscriber<int, string>("Honorific.GetCharacterTitle");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "取 Honorific 的 IPC 失败");
        }
    }

    /// <summary>界面上「再检查一次」按钮。</summary>
    public void RecheckHonorific()
    {
        HonorificAvailable = DetectHonorific();

        if (HonorificAvailable)
        {
            EnsureSubscribers();
            ChatGui.Print("[Music Honorific] 找到 Honorific 了，称号功能可用。", CommandName);
            LastStatus = "已连上 Honorific";
        }
        else
        {
            ChatGui.PrintError("[Music Honorific] 还是没找到 Honorific，请先安装并启用它。", CommandName);
            LastStatus = "仍未找到 Honorific";
        }
    }

    public void Save() => PluginInterface.SavePluginConfig(Config);

    private void OnUpdate(IFramework framework)
    {
        // 关掉插件时把称号撤掉；重新打开再照常推
        if (!Config.Enabled)
        {
            if (_wasEnabled)
            {
                ClearTitle();
                _wasEnabled = false;
            }

            return;
        }

        _wasEnabled = true;

        // 单次轮询：读标题
        if (_pollTimer.ElapsedMilliseconds >= EffectiveIntervalMs)
        {
            _pollTimer.Restart();
            PollOnce();
            RepairIfLost();
        }

        // 没在播放：等一会儿还不动，就把称号撤掉
        if (!_isPlaying)
        {
            if (!Config.ClearWhenStopped)
                return;

            if (_idleTimer.Elapsed.TotalSeconds < Math.Max(0, Config.IdleClearSeconds))
                return;

            if (_titleCleared)
                return;

            ClearTitle();
            _titleCleared = true;
            _lastPayload = string.Empty;
            LastStatus = "没有在播放，称号已清掉（继续播放会自己回来）";
            return;
        }

        // 又开始放了：计时归零，允许再次清掉
        _idleTimer.Restart();
        _titleCleared = false;

        // 只有一行就静止，不用轮播
        if (Config.Lines.Length <= 1)
            return;

        // 每行停留 = 轮询间隔（同一个数，节奏才一致）；最低 1 秒防闪
        if (_scrollTimer.ElapsedMilliseconds < EffectiveIntervalMs)
            return;

        _scrollTimer.Restart();

        // 找下一条能放下的行（超长会按规则退化成只显示歌名）
        for (var step = 1; step <= Config.Lines.Length; step++)
        {
            var next = (_scrollIndex + step) % Config.Lines.Length;
            var line = FitLine(Config.Lines[next]);
            if (line is null)
                continue;

            _scrollIndex = next;
            PushTitle(line);
            return;
        }

        LastStatus = $"所有行都超出称号限制（{MaxTitleLength} 字），暂时无法修改";
    }

    /// <summary>
    /// Honorific 有时会把我们设的称号清掉（换区、重绘、切职业）。
    /// 每次轮询去问一下「你现在显示的还是我那条吗」，不是就补回去。
    /// </summary>
    private void RepairIfLost()
    {
        if (!HonorificAvailable || _getTitle is null || string.IsNullOrEmpty(_lastPushedText))
            return;

        var index = LocalPlayerIndex();
        if (index < 0)
            return;

        try
        {
            var current = _getTitle.InvokeFunc(index);
            if (!string.IsNullOrEmpty(current) && current.Contains(_lastPushedText, StringComparison.Ordinal))
                return;

            var text = _lastPushedText;
            _lastPayload = string.Empty;   // 强制重推
            PushTitle(text);
            LastStatus = $"称号被清掉了，已补回：{text}";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "查询 Honorific 当前称号失败");
        }
    }

    private void PollOnce()
    {
        string raw;
        TrackReader.Track parsed;
        string appName;
        string source;

        // 1) 先看系统媒体会话（Apple Music、Spotify 这类窗口标题不带歌名的靠它）
        var smtc = _smtc;
        if (smtc is { IsPlaying: true } s && !string.IsNullOrWhiteSpace(s.Title))
        {
            raw = string.IsNullOrWhiteSpace(s.Artist) ? s.Title : $"{s.Title} - {s.Artist}";
            parsed = new TrackReader.Track(s.Title.Trim(), s.Artist.Trim());
            appName = s.AppName;
            source = $"媒体会话 {s.AppId}";
            _isPlaying = true;
        }
        else
        {
            // 2) 退回窗口标题（网易云、QQ音乐这些）
            var title = TrackReader.GetWindowTitle(Config.ProcessNames, out var matched);
            if (title is null)
            {
                _isPlaying = false;
                LastStatus = $"没找到正在播放的播放器（找的是：{string.Join("、", Config.ProcessNames)}）";
                return;
            }

            raw = title;
            parsed = TrackReader.Parse(title);
            appName = TrackReader.DisplayName(matched);
            source = $"窗口标题 {matched}";

            // 窗口标题判断不了暂停，靠音频会话看它有没有在出声
            _isPlaying = TrackReader.IsProcessPlaying(matched);

            if (string.IsNullOrWhiteSpace(appName))
                appName = "音乐";

            // 客户端的窗口标题在没播歌时会是应用名之类，这种不覆盖
            if (Config.IgnoreTitles.Any(t => string.Equals(t, raw, StringComparison.OrdinalIgnoreCase)))
            {
                LastStatus = $"忽略：{raw}";
                return;
            }
        }

        _appName = appName;
        _matchedProcess = source;

        if (parsed.IsEmpty)
        {
            LastStatus = $"没解析出歌名：{raw}";
            return;
        }

        if (raw != _lastRawTitle)
        {
            _lastRawTitle = raw;
            _current = parsed;

            // 换歌了：轮播从头开始
            _scrollIndex = 0;
            _scrollTimer.Restart();
        }

        // 没在播放就不推称号（暂停=隐藏），交给 OnUpdate 那边决定清不清
        if (!_isPlaying)
        {
            LastStatus = "没有在播放，称号隐藏";
            return;
        }

        // 换歌时从第一行重新开始
        var text = Config.Lines.Length > 0
            ? FitLine(Config.Lines[Math.Clamp(_scrollIndex, 0, Config.Lines.Length - 1)])
            : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            LastStatus = $"当前这行放不下（超过 {MaxTitleLength} 字），暂时无法修改";
            return;
        }

        _lastPayload = string.Empty;   // 换歌了，强制重推
        PushTitle(text!);

        LastStatus = _isPlaying
            ? (Config.Lines.Length > 1
                ? $"轮播中（第 {_scrollIndex + 1}/{Config.Lines.Length} 行）：{text}"
                : $"已推送：{text}")
            : $"已暂停，停在：{text}";
    }

    private string RenderLine(string template)
        => template
            .Replace("{song}", _current.Song)
            .Replace("{artist}", _current.Artist)
            .Replace("{app}", _appName)
            .Trim();

    /// <summary>超长时的处理方式，顺序对应 Configuration.OverflowMode。</summary>
    private static readonly string[] OverflowModes =
    {
        "跳过这一行",
        "只显示歌名（推荐）",
        "直接截断",
    };

    /// <summary>Honorific 的称号上限（源码里 MaxTitleLength = 32）。</summary>
    private const int MaxTitleLength = 32;

    /// <summary>把文本压到上限以内（末尾加省略号）。</summary>
    private static string Truncate(string text)
        => text.Length <= MaxTitleLength
            ? text
            : text[..(MaxTitleLength - 1)].TrimEnd() + "…";

    /// <summary>
    /// 把一行模板变成真正要显示的称号；放不下就按「超长规则」退化。
    /// 返回 null 表示这行放弃显示。
    /// </summary>
    private string? FitLine(string template)
    {
        var text = RenderLine(template);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (text.Length <= MaxTitleLength)
            return text;

        return Config.OverflowMode switch
        {
            // 只留歌名
            1 => string.IsNullOrWhiteSpace(_current.Song)
                ? null
                : Truncate(_current.Song),
            // 直接截断
            2 => Truncate(text),
            // 跳过这一行
            _ => null,
        };
    }

    /// <summary>有没有哪一行放不下（按当前规则判断）。</summary>
    public bool AnyLineTooLong
        => Config.Lines.Any(l => RenderLine(l).Length > MaxTitleLength);

    /// <summary>是不是所有行都放不下（那整轮都没东西显示）。</summary>
    public bool AllLinesTooLong
        => Config.Lines.Length > 0 && Config.Lines.All(l => FitLine(l) is null);

    private void PushTitle(string text)
    {
        if (!HonorificAvailable)
        {
            LastStatus = "Honorific 不可用";
            return;
        }

        // 超字数就不推：Honorific 会直接拒绝，推了也没意义
        if (text.Length > MaxTitleLength)
        {
            LastStatus = $"「{text}」超出称号限制（{text.Length}/{MaxTitleLength} 字），暂时无法修改";
            return;
        }

        var index = LocalPlayerIndex();
        if (index < 0)
        {
            LastStatus = "找不到自己的角色（可能还没进游戏）";
            return;
        }

        try
        {
            object? color = Config.UseColor
                ? new { X = Config.Color.X, Y = Config.Color.Y, Z = Config.Color.Z }
                : null;

            object? color3 = Config.UseColor && Config.GradientMode == -1
                ? new { X = Config.Color3.X, Y = Config.Color3.Y, Z = Config.Color3.Z }
                : null;

            object? glow = Config.UseGlow
                ? new { X = Config.GlowColor.X, Y = Config.GlowColor.Y, Z = Config.GlowColor.Z }
                : null;

            var payload = JsonSerializer.Serialize(new
            {
                Title = text,
                IsPrefix = Config.IsPrefix,

                // GradientColourSet：-2 不传、-1 是 Two Color、>=0 是 Honorific 的内置色板
                IsOriginal = false,
                Color = color,
                Glow = glow,
                Color3 = color3,
                GradientColourSet = Config.GradientMode >= -1 ? Config.GradientMode : (int?)null,
                GradientAnimationStyle = Config.GradientMode >= -1 ? Config.GradientAnimation : (int?)null,
            });

            // 整份数据没变才跳过：只改颜色、只改渐变，也要立刻重推
            if (payload == _lastPayload)
                return;

            _setTitle!.InvokeAction(index, payload);
            _lastPushedText = text;
            _lastPayload = payload;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "推送称号失败");
            LastStatus = "推送失败，看日志";
        }
    }

    private void ClearTitle()
    {
        if (!HonorificAvailable || _clearTitle is null)
            return;

        try
        {
            var index = LocalPlayerIndex();
            if (index >= 0)
                _clearTitle.InvokeAction(index);
            _lastPushedText = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "清称号失败");
        }
    }

    private int LocalPlayerIndex()
    {
        var local = ObjectTable.LocalPlayer;
        if (local is null)
            return -1;

        for (var i = 0; i < ObjectTable.Length; i++)
        {
            var obj = ObjectTable[i];
            if (obj is not null && obj.EntityId == local.EntityId)
                return i;
        }

        return -1;
    }

    private void DrawUi()
    {
        if (_windowOpen)
            DrawWindow();
    }

    private static void Label(string text) => ImGui.TextUnformatted(text);

    /// <summary>Honorific 内置的渐变色板（顺序就是它的 GradientColourSet 编号）。</summary>
    private static readonly string[] GradientSets =
    {
        "0 彩虹 Pride",
        "1 跨性别 Transgender",
        "2 女同 Lesbian",
        "3 双性恋 Bisexual",
        "4 黑白 Black & White",
        "5 黑红 Black & Red",
        "6 黑蓝 Black & Blue",
        "7 黑黄 Black & Yellow",
        "8 黑绿 Black & Green",
        "9 黑粉 Black & Pink",
        "10 黑青 Black & Cyan",
        "11 樱花 Cherry Blossom",
        "12 金色 Golden",
        "13 粉彩彩虹 Pastel Rainbow",
        "14 暗彩虹 Dark Rainbow",
        "15 非二元 Non-binary",
    };

    /// <summary>Honorific 的 GradientAnimationStyle：Pulse=0、Wave=1、Static=2。</summary>
    private static readonly string[] GradientAnims = { "脉冲 Pulse", "波浪 Wave", "静态 Static" };

    private string _lastPayload = string.Empty;

    private void DrawWindow()
    {
        ImGui.SetNextWindowSize(new Vector2(560, 600), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Music Honorific###MusicHonorific", ref _windowOpen))
        {
            ImGui.End();
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        var changed = false;

        // ── 顶部：Honorific 缺失警告 ──────────────────────────────
        if (!HonorificAvailable)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "⚠ 没有找到 Honorific 插件");
            ImGui.TextWrapped("称号功能全部依赖它，现在装了也改不了称号。");
            ImGui.TextDisabled("请先在 /xlplugins 里安装并启用 Honorific，然后重载本插件。");

            if (ImGui.Button("复制 Honorific 名字"))
                ImGui.SetClipboardText("Honorific");

            ImGui.SameLine();
            if (ImGui.Button("再检查一次"))
                RecheckHonorific();

            ImGui.Spacing();
        }

        // ── 顶部：实时状态卡片 ───────────────────────────────────
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.14f, 0.14f, 0.16f, 1f));
        if (ImGui.BeginChild("##statusCard", new Vector2(0, 88), true))
        {
            ImGui.TextDisabled("正在播放");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.55f, 0.85f, 1f, 1f),
                string.IsNullOrEmpty(_appName) ? "（未知）" : _appName);

            if (_current.IsEmpty || !_isPlaying)
                ImGui.TextDisabled(_isPlaying ? "还没读到歌名" : "当前没有在播放（称号已隐藏）");
            else
                ImGui.TextWrapped(_current.Song +
                    (string.IsNullOrEmpty(_current.Artist) ? "" : $" — {_current.Artist}"));

            ImGui.TextDisabled(LastStatus);
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();

        // ── 操作按钮 ────────────────────────────────────────────
        ImGui.Spacing();
        if (ImGui.Button("立刻读一次"))
        {
            _lastRawTitle = string.Empty;
            _pollTimer.Restart();
            _scrollTimer.Restart();
            _scrollIndex = 0;
            PollOnce();
        }

        ImGui.SameLine();
        if (ImGui.Button("清掉称号"))
            ClearTitle();

        ImGui.SameLine();
        changed |= ImGui.Checkbox("启用", ref Config.Enabled);

        // ── 区块：称号内容 ──────────────────────────────────────
        SectionHeader("称号内容");

        ImGui.Indent();
        ImGui.SetNextItemWidth(360);
        if (ImGui.InputInt("轮询间隔（毫秒）", ref Config.PollIntervalMs, 250, 1000))
            changed = true;
        Config.PollIntervalMs = Math.Clamp(Config.PollIntervalMs, 500, 60000);
        ImGui.TextDisabled("每行停留时间也是它；低于 500 会被拉回");

        ImGui.Spacing();
        ImGui.TextUnformatted("称号内容（一行一句）");
        ImGui.TextDisabled("一行静止；多行自动轮播");
        ImGui.TextDisabled("占位符：{song} 歌名、{artist} 歌手、{app} 播放器");

        var joined = string.Join("\n", Config.Lines);
        if (ImGui.InputTextMultiline("##lines", ref joined, 2048, new Vector2(-1, 92)))
        {
            Config.Lines = joined
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray();
            changed = true;
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(260);
        if (ImGui.BeginCombo("超过 32 字时", OverflowModes[Math.Clamp(Config.OverflowMode, 0, 2)]))
        {
            for (var i = 0; i < OverflowModes.Length; i++)
            {
                if (ImGui.Selectable(OverflowModes[i], i == Config.OverflowMode))
                {
                    Config.OverflowMode = i;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();
        changed |= ImGui.Checkbox("放在名字前面", ref Config.IsPrefix);
        ImGui.SameLine();
        ImGui.TextDisabled("（不勾就占称号位）");

        changed |= ImGui.Checkbox("停止播放后撤掉称号", ref Config.ClearWhenStopped);
        if (Config.ClearWhenStopped)
        {
            ImGui.SetNextItemWidth(140);
            if (ImGui.InputInt("停多久撤掉（秒）", ref Config.IdleClearSeconds, 5, 30))
                changed = true;
            Config.IdleClearSeconds = Math.Clamp(Config.IdleClearSeconds, 0, 3600);
            ImGui.TextDisabled("0 = 立刻；10~20 秒可以避免切歌时闪");
        }

        ImGui.Unindent();

        // ── 区块：颜色与效果 ────────────────────────────────────
        SectionHeader("颜色与效果");

        ImGui.Indent();
        changed |= ImGui.Checkbox("使用自定义颜色", ref Config.UseColor);
        if (Config.UseColor)
        {
            ImGui.SetNextItemWidth(300);
            var modeLabel = Config.GradientMode switch
            {
                -2 => "None",
                -1 => "Two Color",
                _ => GradientSets[Math.Clamp(Config.GradientMode, 0, GradientSets.Length - 1)],
            };

            if (ImGui.BeginCombo("渐变", modeLabel))
            {
                if (ImGui.Selectable("None", Config.GradientMode == -2))
                {
                    Config.GradientMode = -2;
                    changed = true;
                }

                if (ImGui.Selectable("Two Color", Config.GradientMode == -1))
                {
                    Config.GradientMode = -1;
                    changed = true;
                }

                for (var i = 0; i < GradientSets.Length; i++)
                {
                    if (ImGui.Selectable(GradientSets[i], Config.GradientMode == i))
                    {
                        Config.GradientMode = i;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            if (Config.GradientMode >= -1)
            {
                ImGui.SetNextItemWidth(200);
                if (ImGui.BeginCombo("动画", GradientAnims[Math.Clamp(Config.GradientAnimation, 0, 2)]))
                {
                    for (var i = 0; i < GradientAnims.Length; i++)
                    {
                        if (ImGui.Selectable(GradientAnims[i], i == Config.GradientAnimation))
                        {
                            Config.GradientAnimation = i;
                            changed = true;
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            var c = Config.Color;
            ImGui.SetNextItemWidth(300);
            if (ImGui.ColorEdit3("颜色", ref c, ImGuiColorEditFlags.NoLabel))
            {
                Config.Color = c;
                changed = true;
            }

            if (Config.GradientMode == -1)
            {
                var c3 = Config.Color3;
                ImGui.SetNextItemWidth(300);
                if (ImGui.ColorEdit3("第二色", ref c3, ImGuiColorEditFlags.NoLabel))
                {
                    Config.Color3 = c3;
                    changed = true;
                }
            }

            changed |= ImGui.Checkbox("发光", ref Config.UseGlow);
            if (Config.UseGlow)
            {
                var g = Config.GlowColor;
                ImGui.SetNextItemWidth(300);
                if (ImGui.ColorEdit3("发光颜色", ref g, ImGuiColorEditFlags.NoLabel))
                {
                    Config.GlowColor = g;
                    changed = true;
                }
            }
        }

        ImGui.Unindent();

        // ── 区块：诊断 ──────────────────────────────────────────
        SectionHeader("诊断");

        ImGui.Indent();
        ImGui.TextDisabled("从播放器读到的原始文字：");
        ImGui.TextWrapped(string.IsNullOrEmpty(_lastRawTitle) ? "（还没读到）" : _lastRawTitle);
        ImGui.TextDisabled($"来源：{(_matchedProcess.Length == 0 ? "—" : _matchedProcess)}");

        if (AnyLineTooLong)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f),
                $"超过称号限制字数（上限 {MaxTitleLength} 字），暂时无法修改");

            foreach (var line in Config.Lines)
            {
                var rendered = RenderLine(line);
                if (rendered.Length <= MaxTitleLength)
                    continue;

                ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), $"{rendered.Length} 字：{rendered}");
            }

            if (AllLinesTooLong)
                ImGui.TextDisabled("所有行都超了，轮播已停下");
        }

        if (!string.IsNullOrEmpty(MediaSessions.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f),
                $"媒体会话读取失败：{MediaSessions.LastError}");
        }

        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.PopStyleVar();

        // 改动即时保存
        if (changed)
            Save();

        ImGui.End();
    }

    /// <summary>分区块标题：文字居中，两边各一条细线，把界面切干净。</summary>
    private static void SectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.Spacing();

        var origin = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var size = ImGui.CalcTextSize(text);
        var lineY = origin.Y + size.Y / 2f;
        var draw = ImGui.GetWindowDrawList();
        var lineColor = ImGui.GetColorU32(new Vector4(0.42f, 0.47f, 0.56f, 0.75f));

        var leftEnd = origin.X + (avail - size.X) / 2f - 10f;
        var rightStart = origin.X + (avail + size.X) / 2f + 10f;

        if (leftEnd > origin.X + 4f)
            draw.AddLine(new Vector2(origin.X, lineY), new Vector2(leftEnd, lineY), lineColor, 1f);

        if (rightStart < origin.X + avail - 4f)
            draw.AddLine(new Vector2(rightStart, lineY), new Vector2(origin.X + avail, lineY), lineColor, 1f);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - size.X) / 2f);
        ImGui.TextColored(new Vector4(0.55f, 0.78f, 1f, 1f), text);
        ImGui.Spacing();
    }
}
