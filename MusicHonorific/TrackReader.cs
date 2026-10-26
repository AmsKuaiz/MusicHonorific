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
    /// 挨个进程找带标题的窗口。
    /// <para>优先返回「真的在出声」的那个（网易云开着但暂停时，不会抢走 Spotify）。</para>
    /// </summary>
    public static string? GetWindowTitle(string[] processNames, out string? matchedProcess)
    {
        matchedProcess = null;

        // 先收集所有候选人：(进程名, 窗口标题)
        var candidates = new List<(string Name, string Title)>();

        foreach (var name in processNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            Process[] procs;
            try { procs = Process.GetProcessesByName(name.Trim()); }
            catch { continue; }

            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        p.Refresh();
                        var t = p.MainWindowTitle;
                        if (!string.IsNullOrWhiteSpace(t))
                            candidates.Add((p.ProcessName, t.Trim()));
                    }
                    catch { /* 单个进程读不到就跳过 */ }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        if (candidates.Count == 0)
            return null;

        // 谁在出声
        var playingNames = PlayingProcessNames();

        foreach (var c in candidates)
        {
            if (playingNames.Contains(c.Name))
            {
                matchedProcess = c.Name;
                return c.Title;
            }
        }

        // 都没出声（例如全部暂停）——退回顺序里的第一个，保持原样显示
        matchedProcess = candidates[0].Name;
        return candidates[0].Title;
    }

    /// <summary>这个进程当前是否真的在出声（用来判断暂停/播放）。</summary>
    public static bool IsProcessPlaying(string? processName)
        => !string.IsNullOrWhiteSpace(processName) && PlayingProcessNames().Contains(processName);

    /// <summary>取「正在出声」的进程名集合。</summary>
    private static HashSet<string> PlayingProcessNames()
    {
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

        return names;
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
