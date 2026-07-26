using System.Drawing.Drawing2D;

namespace TapeLadyCaptureSuite.Controls;

internal sealed class StatusLamp : Control
{
    private Color _lampColor = Color.Gray;

    public Color LampColor
    {
        get => _lampColor;
        set
        {
            _lampColor = value;
            Invalidate();
        }
    }

    public StatusLamp()
    {
        DoubleBuffered = true;
        Size = new Size(22, 22);
        MinimumSize = new Size(18, 18);
        MaximumSize = new Size(28, 28);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = ClientRectangle;
        bounds.Inflate(-2, -2);

        using var shadow = new SolidBrush(Color.FromArgb(80, Color.Black));
        var shadowBounds = bounds;
        shadowBounds.Offset(1, 2);
        e.Graphics.FillEllipse(shadow, shadowBounds);

        using var path = new GraphicsPath();
        path.AddEllipse(bounds);

        using var brush = new PathGradientBrush(path)
        {
            CenterColor = ControlPaint.LightLight(LampColor),
            SurroundColors = [ControlPaint.Dark(LampColor)]
        };

        e.Graphics.FillEllipse(brush, bounds);

        using var border = new Pen(Color.FromArgb(180, Color.White), 1);
        e.Graphics.DrawEllipse(border, bounds);
    }
}
