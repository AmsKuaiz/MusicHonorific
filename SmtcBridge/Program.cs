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

while (true)
{
    object payload;

    try
    {
        var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
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
            best = new
            {
                appId,
                title = props?.Title ?? string.Empty,
                artist = CleanArtist(props?.Artist ?? string.Empty),
                playing,
                ok = true,
            };
            bestPlaying = playing;
        }

        payload = best ?? new { ok = true, none = true };
    }
    catch (Exception ex)
    {
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

    await Task.Delay(700);
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
