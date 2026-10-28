using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MusicHonorific;

/// <summary>
/// 系统媒体会话（SMTC）。
/// <para>
/// 插件自己不直接调 WinRT —— 那份运行库在 Dalamud 里重复加载会炸
/// （Attempt to update previously set global instance）。
/// 所以交给同目录下 <c>bridge\SmtcBridge.exe</c> 那个小进程去查，这里只读它写的文件。
/// </para>
/// </summary>
public static class MediaSessions
{
    /// <summary>
    /// <c>Position</c>/<c>Duration</c> 单位是秒；<c>AtMs</c> 是采集时刻（epoch 毫秒），
    /// 用来在两次采集之间把进度往前推。
    /// </summary>
    public sealed record NowPlaying(
        string AppId,
        string AppName,
        string Title,
        string Artist,
        bool IsPlaying,
        double Position = 0,
        double Duration = 0,
        long AtMs = 0);

    /// <summary>结果超过这个时长没更新，就当桥接进程没在跑。</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    public static string LastError { get; private set; } = string.Empty;

    private static readonly (string Keyword, string Display)[] Known =
    {
        ("spotify", "Spotify"),
        ("applemusic", "Apple Music"),
        ("apple music", "Apple Music"),
        ("itunes", "Apple Music"),
        ("qqmusic", "QQ音乐"),
        ("kugou", "酷狗音乐"),
        ("kwmusic", "酷我音乐"),
        ("migu", "咪咕音乐"),
        ("cloudmusic", "网易云音乐"),
        ("netease", "网易云音乐"),
    };

    private static string DisplayFor(string appId)
    {
        var lower = appId.ToLowerInvariant();
        foreach (var (keyword, display) in Known)
        {
            if (lower.Contains(keyword))
                return display;
        }

        return appId;
    }

    /// <summary>上次读到的文件时间 + 结果，没变就直接复用，避免每秒解析 5 次 JSON。</summary>
    private static DateTime _cachedWriteTimeUtc;
    private static NowPlaying? _cached;
    private static string _cachedError = string.Empty;

    /// <summary>读桥接进程写的 JSON；读不到就返回 null（调用方退回窗口标题）。</summary>
    public static NowPlaying? GetNowPlaying(string jsonPath)
    {
        try
        {
            var info = new FileInfo(jsonPath);
            if (!info.Exists)
            {
                LastError = "桥接进程还没生成数据";
                return null;
            }

            // 文件没变：直接给上次的结果（最常见的情况，省掉解析和分配）
            if (info.LastWriteTimeUtc == _cachedWriteTimeUtc)
            {
                LastError = _cachedError;
                return _cached;
            }

            _cachedWriteTimeUtc = info.LastWriteTimeUtc;

            if (DateTime.UtcNow - info.LastWriteTimeUtc > MaxAge)
            {
                LastError = "桥接数据太旧（进程可能没在跑）";
                _cached = null;
                _cachedError = LastError;
                return null;
            }

            using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var okProp) && !okProp.GetBoolean())
            {
                LastError = root.TryGetProperty("error", out var err) ? err.GetString() ?? "未知错误" : "未知错误";
                _cached = null;
                _cachedError = LastError;
                return null;
            }

            if (root.TryGetProperty("none", out var noneProp) && noneProp.GetBoolean())
            {
                LastError = string.Empty;
                _cached = null;
                _cachedError = string.Empty;
                return null;
            }

            if (!root.TryGetProperty("appId", out var appIdProp))
            {
                LastError = string.Empty;
                _cached = null;
                _cachedError = string.Empty;
                return null;
            }

            var appId = appIdProp.GetString() ?? string.Empty;
            LastError = string.Empty;

            var result = new NowPlaying(
                appId,
                DisplayFor(appId),
                root.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("artist", out var a) ? a.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("playing", out var p) && p.GetBoolean(),
                root.TryGetProperty("position", out var pos) && pos.TryGetDouble(out var pv) ? pv : 0,
                root.TryGetProperty("duration", out var dur) && dur.TryGetDouble(out var dv) ? dv : 0,
                root.TryGetProperty("at", out var at) && at.TryGetInt64(out var av) ? av : 0);

            _cached = result;
            _cachedError = string.Empty;
            return result;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            _cached = null;
            _cachedError = LastError;
            return null;
        }
    }
}
