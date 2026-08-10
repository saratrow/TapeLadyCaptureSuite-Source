using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;
using TapeLadyCaptureSuite.Services.Capture;

namespace TapeLadyCaptureSuite;

internal sealed class MainStyleVmr9HostForm : Form
{
    private readonly ComboBox _deviceCombo = new();
    private readonly ComboBox _inputCombo = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Panel _previewHost = new();
    private readonly Label _statusLabel = new();
    private DirectShowPreviewSession? _previewSession;

    public MainStyleVmr9HostForm()
    {
        Text = "Tape Lady Capture Suite - Test Main-Style VMR9 Host";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(800, 600);
        Size = new Size(1040, 780);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);

        BuildInterface();
        WireEvents();
        LoadDevices();
    }

    private void BuildInterface()
    {
        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = Color.FromArgb(52, 55, 59),
            WrapContents = false
        };

        _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceCombo.Width = 340;
        _inputCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _inputCombo.Width = 150;
        _inputCombo.Items.AddRange(["Composite / RCA", "S-Video"]);
        _inputCombo.SelectedIndex = 0;

        ConfigureButton(_startButton, "Start VMR9");
        ConfigureButton(_stopButton, "Stop");
        _stopButton.Enabled = false;

        controls.Controls.Add(_deviceCombo);
        controls.Controls.Add(_inputCombo);
        controls.Controls.Add(_startButton);
        controls.Controls.Add(_stopButton);

        _previewHost.Dock = DockStyle.Fill;
        _previewHost.BackColor = Color.Black;

        _statusLabel.Dock = DockStyle.Bottom;
        _statusLabel.Height = 34;
        _statusLabel.Padding = new Padding(10, 0, 10, 0);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Select the EZCAP device, then start the isolated VMR9 host.";

        Controls.Add(_previewHost);
        Controls.Add(_statusLabel);
        Controls.Add(controls);
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 28;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 104, 110);
        button.BackColor = Color.FromArgb(65, 68, 73);
        button.ForeColor = Color.White;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private void WireEvents()
    {
        _startButton.Click += (_, _) => StartPreview();
        _stopButton.Click += (_, _) => StopPreview();
        _previewHost.Resize += (_, _) =>
        {
            if (_previewSession?.IsRunning == true)
            {
                _previewSession.Resize(_previewHost.ClientSize);
            }
        };
        _previewHost.Paint += (_, e) =>
        {
            IntPtr hdc = e.Graphics.GetHdc();
            try
            {
                _previewSession?.RepaintVideo(hdc);
            }
            finally
            {
                e.Graphics.ReleaseHdc(hdc);
            }
        };
        FormClosing += (_, _) => DisposePreviewSession();
    }

    private void LoadDevices()
    {
        foreach (var device in DeviceService.GetVideoDevices())
        {
            _deviceCombo.Items.Add(device);
        }

        for (int index = 0; index < _deviceCombo.Items.Count; index++)
        {
            if ((_deviceCombo.Items[index] as CaptureDeviceInfo)?.Name.Contains(
                    "ezcap", StringComparison.OrdinalIgnoreCase) == true)
            {
                _deviceCombo.SelectedIndex = index;
                return;
            }
        }

        _deviceCombo.SelectedIndex = _deviceCombo.Items.Count > 0 ? 0 : -1;
        _startButton.Enabled = _deviceCombo.SelectedIndex >= 0;
    }

    private void StartPreview()
    {
        if (_deviceCombo.SelectedItem is not CaptureDeviceInfo device ||
            string.IsNullOrWhiteSpace(device.DevicePath))
        {
            return;
        }

        try
        {
            var input = _inputCombo.SelectedIndex == 1
                ? VideoInputKind.SVideo
                : VideoInputKind.Composite;
            DeviceService.TrySetVideoInput(device, input, out string inputMessage);

            _previewHost.CreateControl();
            var session = new DirectShowPreviewSession();
            _previewSession = session;
            session.Start(
                device.DevicePath,
                _previewHost.Handle,
                _previewHost.ClientSize,
                DirectShowRendererMode.Vmr9Windowless);

            _statusLabel.Text = $"VMR9 running: {inputMessage}";
            _startButton.Enabled = false;
            _stopButton.Enabled = true;
            _deviceCombo.Enabled = false;
            _inputCombo.Enabled = false;
        }
        catch (Exception ex)
        {
            StopPreviewSession();
            _statusLabel.Text = "VMR9 could not start.";
            MessageBox.Show(this, ex.Message, "Main-Style VMR9 Host",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopPreview()
    {
        StopPreviewSession();
        _previewHost.Invalidate();
        _statusLabel.Text = "VMR9 stopped.";
        _startButton.Enabled = _deviceCombo.Items.Count > 0;
        _stopButton.Enabled = false;
        _deviceCombo.Enabled = true;
        _inputCombo.Enabled = true;
    }

    private void StopPreviewSession()
    {
        DirectShowPreviewSession? session = _previewSession;
        _previewSession = null;
        if (session is null)
        {
            return;
        }

        session.Stop();
        session.Dispose();
    }

    private void DisposePreviewSession()
    {
        DirectShowPreviewSession? session = _previewSession;
        _previewSession = null;
        session?.Dispose();
    }
}