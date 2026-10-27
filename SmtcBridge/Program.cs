// 把 Windows 的「系统媒体会话」状态写成 JSON 文件，供 Dalamud 插件读取。
// 单独进程的好处：插件怎么重载都不会重复加载 WinRT 运行时。
using System.Diagnostics;
using System.Text.Json;
using Windows.Media.Control;

var outPath = args.Length > 0
    ? args[0]
    : Path.Combine(Path.GetTempPath(), "music-honorific-bridge.json");

var self = Process.GetCurrentProcess().ProcessName;

// 同一时间只留一个实例
try
{
    var others = Process.GetProcessesByName(self).Where(p => p.Id != Environment.ProcessId).ToArray();
    if (others.Length > 0)
    {
        // 还有别的实例在跑：直接退出，让老的继续干活
        return;
    }
}
catch
{
    // 查不到就继续跑
}

var json = new JsonSerializerOptions { WriteIndented = false };

string[] known =
{
    "spotify", "applemusic", "apple music", "itunes", "qqmusic",
    "kugou", "kwmusic", "migu", "cloudmusic", "netease",
};

// 会话管理器只取一次，之后复用。
// 每轮都 RequestAsync 会让系统媒体服务反复初始化，拖慢整机（也会让游戏掉帧）。
GlobalSystemMediaTransportControlsSessionManager? cachedMgr = null;

while (true)
{
    object payload;

    try
    {
        cachedMgr ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var mgr = cachedMgr;
        var best = (object?)null;
        var bestPlaying = false;

        foreach (var s in mgr.GetSessions())
        {
            var appId = s.SourceAppUserModelId ?? string.Empty;
            var lower = appId.ToLowerInvariant();
            if (!known.Any(k => lower.Contains(k)))
                continue;

            var info = s.GetPlaybackInfo();
            var playing = info.PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (best is not null && !(playing && !bestPlaying))
                continue;

            var props = await s.TryGetMediaPropertiesAsync();

            // 播放进度：位置 + 总时长（有些播放器不给，就是 0）
            double position = 0, duration = 0;
            try
            {
                var tl = s.GetTimelineProperties();
                if (tl is not null)
                {
                    position = tl.Position.TotalSeconds;
                    duration = (tl.EndTime - tl.StartTime).TotalSeconds;
                    if (duration <= 0)
                        duration = 0;
                }
            }
            catch
            {
                // 拿不到就当没有进度
            }

            best = new
            {
                appId,
                title = props?.Title ?? string.Empty,
                artist = CleanArtist(props?.Artist ?? string.Empty),
                playing,
                position,
                duration,
                at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ok = true,
            };
            bestPlaying = playing;
        }

        payload = best ?? new { ok = true, none = true };
    }
    catch (Exception ex)
    {
        cachedMgr = null;   // 出问题就丢缓存，下轮重新取
        payload = new { ok = false, error = $"{ex.GetType().Name}: {ex.Message}" };
    }

    try
    {
        var tmp = outPath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(payload, json));
        File.Move(tmp, outPath, overwrite: true);
    }
    catch
    {
        // 写不进去就下一轮再试
    }

    await Task.Delay(1000);
}

static string CleanArtist(string artist)
{
    if (string.IsNullOrWhiteSpace(artist))
        return string.Empty;

    foreach (var sep in new[] { " — ", " – ", " - " })
    {
        var idx = artist.IndexOf(sep, StringComparison.Ordinal);
        if (idx > 0)
            return artist[..idx].Trim();
    }

    return artist.Trim();
}
