using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MusicHonorific;

/// <summary>
/// 从播放器进程的窗口标题里读「歌名 - 歌手」。
/// 不绑定网易云：给一串进程名，谁在放就读谁。
/// </summary>
public static class TrackReader
{
    public readonly record struct Track(string Song, string Artist)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Song);
    }

    /// <summary>各家播放器会在标题后面挂自己的名字，读之前先剥掉。</summary>
    private static readonly string[] AppSuffixes =
    {
        " - 网易云音乐", " - QQ音乐", " - Apple Music", " - iTunes",
        " - Spotify", " - 酷狗音乐", " - 酷我音乐", " - 咪咕音乐",
        " - foobar2000", " - AIMP", " - Windows Media Player",
        "网易云音乐", "QQ音乐", "Apple Music", "iTunes", "Spotify",
        "酷狗音乐", "酷我音乐", "咪咕音乐", "foobar2000", "AIMP",
    };

    /// <summary>进程名 → 给人看的播放器名字。</summary>
    private static readonly (string Process, string Display)[] KnownPlayers =
    {
        ("cloudmusic", "网易云音乐"),
        ("QQMusic", "QQ音乐"),
        ("AppleMusic", "Apple Music"),
        ("iTunes", "Apple Music"),
        ("Spotify", "Spotify"),
        ("KuGou", "酷狗音乐"),
        ("kugou", "酷狗音乐"),
        ("KGMusic", "酷狗音乐"),
        ("KwMusic", "酷我音乐"),
        ("kuwo", "酷我音乐"),
        ("MiguMusic", "咪咕音乐"),
        ("foobar2000", "foobar2000"),
        ("AIMP", "AIMP"),
        ("Microsoft.ZuneMusic", "Windows 媒体播放器"),
        ("Music.UI", "Windows 媒体播放器"),
    };

    /// <summary>把进程名翻成播放器名字；不认识的就把进程名原样给回去。</summary>
    public static string DisplayName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return string.Empty;

        foreach (var (proc, display) in KnownPlayers)
        {
            if (string.Equals(proc, processName, StringComparison.OrdinalIgnoreCase))
                return display;
        }

        return processName;
    }

    /// <summary>
    /// 找带标题的播放器窗口。
    /// <para>优先返回「真的在出声」的那个（网易云开着但暂停时，不会抢走 Spotify）。</para>
    /// <para>进程只枚举一次（按名字筛），比逐个 GetProcessesByName 快 6 倍。</para>
    /// </summary>
    public static string? GetWindowTitle(string[] processNames, out string? matchedProcess, out bool playing)
    {
        matchedProcess = null;
        playing = false;

        // 目标进程名集合（忽略大小写）
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in processNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
                wanted.Add(name.Trim());
        }

        if (wanted.Count == 0)
            return null;

        // 目标进程：PID → 进程名。一次进程枚举搞定。
        var targets = new Dictionary<uint, string>();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return null; }

        foreach (var p in all)
        {
            try
            {
                string procName;
                try { procName = p.ProcessName; }
                catch { continue; }

                if (wanted.Contains(procName))
                    targets[(uint)p.Id] = procName;
            }
            catch { /* 单个进程读不到就跳过 */ }
            finally { p.Dispose(); }
        }

        if (targets.Count == 0)
            return null;

        // 一次 EnumWindows 扫完所有窗口，按 PID 归到对应播放器名下。
        // 每个进程保留「最像歌名」的那条（桌面歌词、迷你播放器这类会被过滤掉）。
        var best = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bestScore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        EnumerateWindowTitles((pid, text) =>
        {
            if (!targets.TryGetValue(pid, out var procName))
                return;
            if (IsNoiseTitle(text))
                return;

            var score = TitleScore(text);
            if (!bestScore.TryGetValue(procName, out var old) || score > old)
            {
                bestScore[procName] = score;
                best[procName] = text;
            }
        });

        if (best.Count == 0)
            return null;

        // 谁在出声（只查一次，内部还有 5 秒缓存）
        var playingNames = PlayingProcessNames();

        foreach (var kv in best)
        {
            if (playingNames.Contains(kv.Key))
            {
                matchedProcess = kv.Key;
                playing = true;
                return kv.Value;
            }
        }

        // 都没出声（例如全部暂停）——退回顺序里的第一个，保持原样显示
        var first = best.First();
        matchedProcess = first.Key;
        return first.Value;
    }

    /// <summary>标题「像不像歌名」：含「 - 」的优先，其次看长度。</summary>
    private static int TitleScore(string text)
    {
        var score = Math.Min(text.Length, 80);
        if (text.Contains(" - "))
            score += 200;
        return score;
    }

    /// <summary>不是歌名的窗口标题（桌面歌词、迷你播放器、应用名之类）。</summary>
    private static readonly string[] NoiseTitles =
    {
        "桌面歌词", "歌词", "迷你", "mini", "播放列表", "网易云音乐", "netease", "cloudmusic",
        "qq音乐", "spotify", "apple music", "itunes", "酷狗", "酷我", "咪咕",
        "foobar", "aimp", "windows media", "音乐", "player",
    };

    private static bool IsNoiseTitle(string title)
    {
        var t = title.Trim().ToLowerInvariant();
        if (t.Length == 0)
            return true;

        foreach (var noise in NoiseTitles)
        {
            if (t.Contains(noise))
                return true;
        }

        return false;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// 一次 EnumWindows 扫完所有可见顶层窗口，把 (进程 PID, 窗口标题) 交给回调。
    /// </summary>
    private static void EnumerateWindowTitles(Action<uint, string> onWindow)
    {
        try
        {
            EnumWindows((hWnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd))
                        return true;

                    var len = GetWindowTextLengthW(hWnd);
                    if (len <= 0)
                        return true;

                    GetWindowThreadProcessId(hWnd, out var owner);

                    var sb = new System.Text.StringBuilder(len + 2);
                    if (GetWindowTextW(hWnd, sb, sb.Capacity) <= 0)
                        return true;

                    var text = sb.ToString().Trim();
                    if (text.Length > 0)
                        onWindow(owner, text);
                }
                catch
                {
                    // 单个窗口出错不影响其它
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 整体失败就当作没有窗口
        }
    }

    /// <summary>
    /// 「正在出声的进程名」。COM 枚举音频会话比较重，
    /// 缓存 5 秒，避免每次轮询都做一遍。
    /// </summary>
    private static HashSet<string>? _playingCache;
    private static long _playingCacheAt;
    private static readonly object _playingLock = new();

    private static HashSet<string> PlayingProcessNames()
    {
        lock (_playingLock)
        {
            var now = Environment.TickCount64;
            if (_playingCache is not null && now - _playingCacheAt < 5000)
                return _playingCache;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pid in AudioSessions.GetPlayingProcessIds())
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    names.Add(p.ProcessName);
                }
                catch { /* 进程已经退了就跳过 */ }
            }

            _playingCache = names;
            _playingCacheAt = now;
            return names;
        }
    }

    /// <summary>把「歌名 - 歌手 - QQ音乐」这种尾巴剥干净。</summary>
    private static string StripAppName(string title)
    {
        var text = title.Trim();

        foreach (var suffix in AppSuffixes.OrderByDescending(s => s.Length))
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^suffix.Length].Trim();
                break;
            }
        }

        return text.Trim(' ', '-', '—', '·');
    }

    /// <summary>把「歌名 - 歌手」拆开。拆不开就当整句是歌名。</summary>
    public static Track Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return new Track(string.Empty, string.Empty);

        var text = StripAppName(title);

        // 常见：「Song - Artist」「Song - Artist1/Artist2」
        var idx = text.LastIndexOf(" - ", StringComparison.Ordinal);

        // 有些播放器用不带空格的「Song-Artist」；只有一个横杠时才这么猜
        if (idx <= 0 && text.Count(c => c is '-' or '－' or '—') == 1)
        {
            var only = text.IndexOfAny(new[] { '-', '－', '—' });
            if (only > 0 && only < text.Length - 1)
            {
                var s0 = text[..only].Trim();
                var a0 = text[(only + 1)..].Trim();
                if (s0.Length > 0 && a0.Length > 0)
                    return new Track(s0, a0);
            }
        }

        if (idx > 0 && idx + 3 < text.Length)
        {
            var song = text[..idx].Trim();
            var artist = text[(idx + 3)..].Trim();
            if (song.Length > 0)
                return new Track(song, artist);
        }

        return new Track(text, string.Empty);
    }
}
