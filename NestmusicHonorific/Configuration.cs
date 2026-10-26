using System.Numerics;
using Dalamud.Configuration;

namespace NestmusicHonorific;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>总开关：关掉就完全不碰称号。</summary>
    public bool Enabled = true;

    /// <summary>轮询间隔（毫秒）。切歌时窗口标题会变，2 秒足够。</summary>
    public int PollIntervalMs = 2000;

    /// <summary>
    /// 要监听的播放器进程名（按顺序找，谁在放就读谁）。
    /// 网易云 cloudmusic、QQ音乐 QQMusic、Apple Music AppleMusic。
    /// </summary>
    public string[] ProcessNames =
    {
        "cloudmusic", "QQMusic", "AppleMusic", "Spotify",
        "KuGou", "KwMusic", "MiguMusic",
    };

    /// <summary>
    /// 称号内容，一行一句。只有一行就静止显示；多行会自动轮播。
    /// 每行都可以用 {song} 歌名、{artist} 歌手、{app} 正在用的播放器。
    /// </summary>
    public string[] Lines =
    {
        "♪正在听 - {app}♪",
        "♪ {song} - {artist}♪",
    };

    /// <summary>
    /// 超过称号上限（32 字）时怎么办：
    /// 0 = 跳过这一行、1 = 只显示歌名（还超就截断）、2 = 直接截断。
    /// </summary>
    public int OverflowMode = 1;

    /// <summary>true = 放到名字前面；false = 放到名字后面（称号位）。</summary>
    public bool IsPrefix = false;

    /// <summary>停止播放后，要不要把称号撤掉。</summary>
    public bool ClearWhenStopped = true;

    /// <summary>停多久才撤（秒）。0 = 立刻。</summary>
    public int IdleClearSeconds = 0;

    /// <summary>是否用自定义颜色。</summary>
    public bool UseColor = true;

    /// <summary>称号颜色（0~1）。</summary>
    public Vector3 Color = new(0.45f, 0.78f, 1.00f);

    /// <summary>
    /// 渐变模式，和 Honorific 的下拉一一对应：
    /// -2 = None（不用渐变）、-1 = Two Color（用 Color + Color3）、0~15 = 内置色板。
    /// </summary>
    public int GradientMode = -2;

    /// <summary>Two Color 模式下的第二个颜色。</summary>
    public Vector3 Color3 = new(1.00f, 0.55f, 0.85f);

    /// <summary>渐变动画：0=脉冲、1=波浪、2=静态。</summary>
    public int GradientAnimation = 1;

    /// <summary>发光效果。</summary>
    public bool UseGlow = false;

    /// <summary>发光颜色（0~1）。</summary>
    public Vector3 GlowColor = new(0.6f, 0.85f, 1.0f);

    /// <summary>认不出的标题（比如「网易云音乐」）就不覆盖。</summary>
    public string[] IgnoreTitles = { "网易云音乐", "NetEase Cloud Music", "cloudmusic" };

}
