using System.Numerics;
using Dalamud.Configuration;

namespace MusicHonorific;

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

    /// <summary>进度条格子数（占位符 {bar} 用）。</summary>
    public int BarLength = 8;

    /// <summary>
    /// {bar} 的样式：
    /// 0 = 圆圈 ●○、1 = 方框 ■□、2 = 计时（1:23/3:45）、3 = 自定义字符。
    /// </summary>
    public int BarStyle = 0;

    /// <summary>自定义样式的填充字符（BarStyle = 3 时用）。</summary>
    public string BarFilled = "◆";

    /// <summary>自定义样式的空白字符（BarStyle = 3 时用）。</summary>
    public string BarEmpty = "◇";

    /// <summary>
    /// 播放器不给总时长时怎么画进度条（网易云、QQ音乐）：
    /// 0 = 不显示、1 = 按估算时长填充、2 = 循环流动（纯装饰）。
    /// </summary>
    public int NoDurationMode = 1;

    /// <summary>估算时长（秒）。NoDurationMode = 1 时用，默认 3 分 30 秒。</summary>
    public int AssumedSeconds = 210;

    /// <summary>循环流动一圈要多少秒。NoDurationMode = 2 时用。</summary>
    public int CycleSeconds = 30;

    /// <summary>
    /// 超过称号上限（32 字）时怎么办：
    /// 0 = 跳过这一行、1 = 只显示歌名（还超就截断）、2 = 直接截断。
    /// </summary>
    public int OverflowMode = 1;

    /// <summary>true = 放到名字前面；false = 放到名字后面（称号位）。</summary>
    public bool IsPrefix = false;

    /// <summary>
    /// 切歌时怎么处理轮播：
    /// 0 = 回到第一行、1 = 停在当前行继续。
    /// </summary>
    public int OnTrackChange = 0;

    /// <summary>
    /// 暂停/停止播放时怎么处理称号：
    /// 0 = 撤掉、1 = 保留最后一行、2 = 显示下面的自定义文字。
    /// </summary>
    public int OnPause = 0;

    /// <summary>暂停时显示的固定文字（OnPause = 2 时用）。</summary>
    public string PauseText = "♪ 已暂停 ♪";

    /// <summary>停止播放后多久执行上面那条规则（秒）。</summary>
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
