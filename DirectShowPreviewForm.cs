using DirectShowLib;
using TapeLadyCaptureSuite.Services.Capture;

namespace TapeLadyCaptureSuite;

/// <summary>
/// DirectShow proof-of-concept window. Video preview and live PCM audio levels
/// are supplied by one graph and do not use OpenCV or FFmpeg.
/// </summary>
internal sealed class DirectShowPreviewForm : Form
{
    private readonly ComboBox _deviceCombo = new();
    private readonly ComboBox _rendererCombo = new();
    private readonly Button _refreshButton = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _copyGraphButton = new();
    private readonly Panel _previewHost = new();
    private readonly ProgressBar _audioMeter = new();
    private readonly Label _audioLabel = new();
    private readonly Label _statusLabel = new();
    private DirectShowPreviewSession? _previewSession;
    private bool _hasStoppedPreview;

    public DirectShowPreviewForm()
    {
        Text = "Tape Lady Capture Suite — DirectShow Preview and Audio Test";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 650);
        Size = new Size(1040, 810);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);

        BuildInterface();
        WireEvents();
        RefreshDevices();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(14),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "DIRECTSHOW LIVE VIDEO + AUDIO TEST\r\nThe preview preserves the correct 4:3 NTSC display shape. The audio meter reads the EZCAP embedded PCM pin.",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(0, 6, 0, 6)
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));

        _deviceCombo.Dock = DockStyle.Fill;
        _deviceCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _deviceCombo.BackColor = Color.FromArgb(52, 55, 59);
        _deviceCombo.ForeColor = Color.White;

        _rendererCombo.Dock = DockStyle.Fill;
        _rendererCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rendererCombo.BackColor = Color.FromArgb(52, 55, 59);
        _rendererCombo.ForeColor = Color.White;
        _rendererCombo.Items.AddRange(["Default Video Renderer", "VMR9 Windowless"]);
        _rendererCombo.SelectedIndex = 0;

        ConfigureButton(_refreshButton, "Refresh");
        ConfigureButton(_startButton, "Start Preview");
        ConfigureButton(_stopButton, "Stop");
        ConfigureButton(_copyGraphButton, "Copy Graph");

        controls.Controls.Add(_deviceCombo, 0, 0);
        controls.Controls.Add(_rendererCombo, 1, 0);
        controls.Controls.Add(_refreshButton, 2, 0);
        controls.Controls.Add(_startButton, 3, 0);
        controls.Controls.Add(_stopButton, 4, 0);
        controls.Controls.Add(_copyGraphButton, 5, 0);
        root.Controls.Add(controls, 0, 1);

        _previewHost.Dock = DockStyle.Fill;
        _previewHost.BackColor = Color.Black;
        _previewHost.BorderStyle = BorderStyle.FixedSingle;
        root.Controls.Add(_previewHost, 0, 2);

        var audioPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 6)
        };
        audioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        audioPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _audioLabel.Dock = DockStyle.Fill;
        _audioLabel.Text = "EZCAP AUDIO: not connected";
        _audioLabel.ForeColor = Color.Silver;
        _audioLabel.TextAlign = ContentAlignment.MiddleLeft;

        _audioMeter.Dock = DockStyle.Fill;
        _audioMeter.Minimum = 0;
        _audioMeter.Maximum = 100;
        _audioMeter.Value = 0;
        _audioMeter.Style = ProgressBarStyle.Continuous;
        _audioMeter.Margin = new Padding(8, 5, 0, 5);

        audioPanel.Controls.Add(_audioLabel, 0, 0);
        audioPanel.Controls.Add(_audioMeter, 1, 0);
        root.Controls.Add(audioPanel, 0, 3);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Text = "Select the ezcap Video Grabber and click Start Preview.";
        _statusLabel.ForeColor = Color.Silver;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(root);
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 104, 110);
        button.BackColor = Color.FromArgb(65, 68, 73);
        button.ForeColor = Color.White;
        button.Margin = new Padding(6, 0, 0, 0);
    }

    private void WireEvents()
    {
        _refreshButton.Click += (_, _) => RefreshDevices();
        _startButton.Click += (_, _) => StartPreview();
        _stopButton.Click += (_, _) => StopPreview();
        _copyGraphButton.Click += (_, _) => CopyGraphReport();
        _previewHost.Resize += (_, _) =>
        {
            _previewSession?.RecordPreviewHostEvent("Resize", _previewHost);
            if (_previewSession?.IsRunning == true)
            {
                _previewSession.Resize(_previewHost.ClientSize);
            }
        };
        _previewHost.Paint += (_, e) =>
        {
            _previewSession?.RecordPreviewHostEvent("Paint", _previewHost);
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
        FormClosing += (_, _) =>
        {
            DisposePreviewSession();
        };
    }

    private void PreviewSession_AudioLevelChanged(object? sender, AudioLevelEventArgs e)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (IsDisposed)
            {
                return;
            }

            _audioMeter.Value = e.Level;
            _audioLabel.Text = $"EZCAP AUDIO LEVEL: {e.Level,3}%";
            _audioLabel.ForeColor = e.Level > 0 ? Color.White : Color.Silver;
        });
    }

    private void RefreshDevices()
    {
        StopPreview();
        string? previousPath = (_deviceCombo.SelectedItem as DirectShowPreviewDevice)?.DevicePath;

        _deviceCombo.BeginUpdate();
        _deviceCombo.Items.Clear();

        DsDevice[] devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
        try
        {
            foreach (var device in devices)
            {
                _deviceCombo.Items.Add(new DirectShowPreviewDevice(
                    device.Name ?? "Unnamed capture device",
                    device.DevicePath ?? string.Empty));
            }
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }

            _deviceCombo.EndUpdate();
        }

        if (_deviceCombo.Items.Count == 0)
        {
            _statusLabel.Text = "No DirectShow video capture devices were found.";
            _startButton.Enabled = false;
            return;
        }

        int preferredIndex = -1;
        for (int index = 0; index < _deviceCombo.Items.Count; index++)
        {
            var item = (DirectShowPreviewDevice)_deviceCombo.Items[index];
            if (!string.IsNullOrWhiteSpace(previousPath) &&
                string.Equals(item.DevicePath, previousPath, StringComparison.OrdinalIgnoreCase))
            {
                preferredIndex = index;
                break;
            }

            if (preferredIndex < 0 &&
                item.Name.Contains("ezcap", StringComparison.OrdinalIgnoreCase))
            {
                preferredIndex = index;
            }
        }

        _deviceCombo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
        _startButton.Enabled = true;
        _statusLabel.Text = "Ready. Start the VCR, then click Start Preview.";
    }

    private void StartPreview()
    {
        if (_deviceCombo.SelectedItem is not DirectShowPreviewDevice selected ||
            string.IsNullOrWhiteSpace(selected.DevicePath))
        {
            MessageBox.Show(this, "Select a capture device first.", "DirectShow Preview",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DirectShowPreviewStartDiagnostics? diagnostics = null;
        try
        {
            UseWaitCursor = true;
            _audioMeter.Value = 0;
            _audioLabel.Text = "EZCAP AUDIO: connecting...";
            _statusLabel.Text = $"Opening {selected.Name} through DirectShow...";
            _previewHost.CreateControl();
            var rendererMode = _rendererCombo.SelectedIndex == 1
                ? DirectShowRendererMode.Vmr9Windowless
                : DirectShowRendererMode.Default;

            diagnostics = new DirectShowPreviewStartDiagnostics(
                selected.Name,
                selected.DevicePath,
                rendererMode,
                _hasStoppedPreview,
                _previewHost);
            diagnostics.BeginPhase("session construction");
            var previewSession = new DirectShowPreviewSession();
            diagnostics.CompletePhase("session construction");
            previewSession.AudioLevelChanged += PreviewSession_AudioLevelChanged;
            _previewSession = previewSession;
            previewSession.Start(
                selected.DevicePath,
                _previewHost.Handle,
                _previewHost.ClientSize,
                rendererMode,
                diagnostics);

            _audioLabel.Text = previewSession.IsAudioConnected
                ? "EZCAP AUDIO LEVEL: waiting for tape audio..."
                : previewSession.AudioDescription;
            _audioLabel.ForeColor = previewSession.IsAudioConnected ? Color.White : Color.Goldenrod;

            _statusLabel.Text =
                $"DirectShow preview is running: {selected.Name}. " +
                $"Video: {previewSession.VideoStandardDescription}; {previewSession.VideoFormatDescription}; {previewSession.VideoPinDescription}. " +
                $"Audio: {previewSession.AudioDescription}.";
            _startButton.Enabled = false;
            _deviceCombo.Enabled = false;
            _rendererCombo.Enabled = false;
        }
        catch (Exception ex)
        {
            diagnostics?.LogStartFailure(ex);
            StopPreviewSession();
            _audioMeter.Value = 0;
            _audioLabel.Text = "EZCAP AUDIO: not connected";
            _statusLabel.Text = "DirectShow preview could not start.";
            MessageBox.Show(this,
                ex.Message + "\r\n\r\nClose ArcSoft, Camera, OBS, and the main Tape Lady preview, then try again.",
                "DirectShow Preview Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void StopPreview()
    {
        StopPreviewSession();
        _previewHost.Invalidate();
        _audioMeter.Value = 0;
        _audioLabel.Text = "EZCAP AUDIO: not connected";
        _audioLabel.ForeColor = Color.Silver;
        _startButton.Enabled = _deviceCombo.Items.Count > 0;
        _deviceCombo.Enabled = true;
        _rendererCombo.Enabled = true;
        _statusLabel.Text = "Preview stopped.";
    }

    private void CopyGraphReport()
    {
        try
        {
            Clipboard.SetText(_previewSession?.Vmr9DiagnosticsReport ?? "No active DirectShow graph.");
            _statusLabel.Text = "VMR9 diagnostic report copied to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "DirectShow Preview",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopPreviewSession()
    {
        DirectShowPreviewSession? previewSession = _previewSession;
        _previewSession = null;
        if (previewSession is null)
        {
            return;
        }

        previewSession.AudioLevelChanged -= PreviewSession_AudioLevelChanged;
        _hasStoppedPreview = true;
        previewSession.Stop();
        previewSession.Dispose();
    }

    private void DisposePreviewSession()
    {
        DirectShowPreviewSession? previewSession = _previewSession;
        _previewSession = null;
        if (previewSession is null)
        {
            return;
        }

        previewSession.AudioLevelChanged -= PreviewSession_AudioLevelChanged;
        previewSession.Dispose();
    }

    private sealed record DirectShowPreviewDevice(string Name, string DevicePath)
    {
        public override string ToString() => Name;
    }
}
