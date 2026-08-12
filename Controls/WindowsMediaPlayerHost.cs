using System.ComponentModel;

namespace TapeLadyCaptureSuite.Controls;

internal sealed class WindowsMediaPlayerHost : AxHost
{
    public WindowsMediaPlayerHost()
        : base("6BF52A52-394A-11D3-B153-00C04F79FAA6")
    {
        Dock = DockStyle.Fill;
    }

    private dynamic Player => GetOcx();

    public void Open(string path)
    {
        CreateControl();
        Player.settings.autoStart = false;
        Player.URL = path;
    }

    public void Play() => Player.controls.play();

    public void Pause() => Player.controls.pause();

    public void Stop() => Player.controls.stop();

    public double Position
    {
        get => ReadDouble(() => Player.controls.currentPosition);
        set => Player.controls.currentPosition = Math.Max(0, value);
    }

    public double Duration => ReadDouble(() => Player.currentMedia?.duration);

    public int Volume
    {
        get => (int)Math.Round(ReadDouble(() => Player.settings.volume));
        set => Player.settings.volume = Math.Clamp(value, 0, 100);
    }

    public bool Muted
    {
        get
        {
            try
            {
                return Player.settings.mute;
            }
            catch
            {
                return false;
            }
        }
        set => Player.settings.mute = value;
    }

    protected override void AttachInterfaces()
    {
    }

    private static double ReadDouble(Func<object?> read)
    {
        try
        {
            return Convert.ToDouble(read(), System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                Stop();
            }
            catch
            {
            }
        }

        base.Dispose(disposing);
    }
}