using System.Diagnostics;
using TapeLadyCaptureSuite.Controls;
using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;

namespace TapeLadyCaptureSuite;

internal sealed partial class MainForm : Form
{
    private readonly PreviewService _previewService = new();
    private readonly RecordingService _recordingService = new();
    private readonly System.Windows.Forms.Timer _recordingTimer = new();
    private readonly System.Windows.Forms.Timer _fileTimer = new();

    private readonly ComboBox _videoDeviceCombo = new();
    private readonly ComboBox _audioDeviceCombo = new();
    private readonly ComboBox _inputCombo = new();
    private readonly PictureBox _previewBox = new();
    private readonly Button _startPreviewButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _installFfmpegButton = new();
    private readonly Button _recordButton = new();
    private readonly Button _pauseButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _fullScreenButton = new();
    private readonly Button _browseButton = new();
    private readonly TextBox _customerText = new();
    private readonly TextBox _tapeLabelText = new();
    private readonly TextBox _saveFolderText = new();
    private readonly Label _statusText = new();
    private readonly StatusLamp _statusLamp = new();
    private readonly Label _timerText = new();
    private readonly Label _resolutionText = new();
    private readonly Label _fileSizeText = new();
    private readonly Label _droppedFramesText = new();
    private readonly Label _engineText = new();

    private DateTime _recordingStarted;
    private TimeSpan _pausedDuration;
    private DateTime? _pauseStarted;
    private CaptureUiState _captureState = CaptureUiState.Ready;
    private Form? _fullScreenForm;
    private string? _activeOutputPath;
    private string? _ffmpegPath;
    private bool _closing;

    private enum CaptureUiState
    {
        Ready,
        Preview,
        Recording,
        Paused,
        Finalizing
    }

    public MainForm()
    {
        Text = "Tape Lady Capture Suite — Milestone 5.2";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1280, 780);
        Size = new Size(1580, 900);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);
        KeyPreview = true;

        BuildInterface();
        WireEvents();

        _recordingTimer.Interval = 250;
        _recordingTimer.Tick += (_, _) => UpdateRecordingClock();

        _fileTimer.Interval = 1000;
        _fileTimer.Tick += (_, _) => UpdateRecordingStatistics();

        Shown += async (_, _) =>
        {
            _saveFolderText.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "Tape Lady Captures");

            RefreshFfmpegStatus();
            RefreshDevices();
            RestoreWorkflowState();
            await StartSelectedPreviewAsync();
        };

        FormClosing += MainForm_FormClosing;
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(12),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildDeviceBar(), 0, 1);
        root.Controls.Add(BuildJobBar(), 0, 2);
        root.Controls.Add(BuildPreviewPanel(), 0, 3);
        root.Controls.Add(BuildStatusBar(), 0, 4);
        root.Controls.Add(BuildTransportBar(), 0, 5);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel2,
            BackColor = Color.FromArgb(20, 21, 23)
        };

        split.Panel1.Controls.Add(root);
        split.Panel2.Controls.Add(BuildWorkflowPanel());
        Controls.Add(split);

        // A SplitContainer is created at a very small default width. Setting a
        // large Panel2MinSize before it has been laid out can crash the app at
        // startup. Apply the production-width layout only after the form exists.
        void ApplyWorkflowPanelWidth()
        {
            if (split.ClientSize.Width < 800)
            {
                return;
            }

            const int workflowWidth = 385;
            split.Panel2MinSize = 360;
            var desired = split.ClientSize.Width - workflowWidth;
            var maximum = split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth;
            split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, maximum);
        }

        Shown += (_, _) => ApplyWorkflowPanelWidth();
        split.Resize += (_, _) => ApplyWorkflowPanelWidth();
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 19, 21),
            Padding = new Padding(18, 10, 18, 10)
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "TAPE LADY",
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(18, 7)
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "CAPTURE SUITE",
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            ForeColor = Color.Silver,
            Location = new Point(21, 43)
        };

        var version = new Label
        {
            AutoSize = true,
            Text = "Milestone 5.2 • Audio Device Detection",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.DarkGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        panel.Controls.Add(version);

        panel.Resize += (_, _) =>
        {
            version.Location = new Point(
                panel.ClientSize.Width - version.Width - 18,
                27);
        };

        return panel;
    }

    private Control BuildDeviceBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 2,
            Padding = new Padding(10, 6, 10, 6),
            BackColor = Color.FromArgb(52, 55, 59)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));

        AddBarLabel(panel, "Video", 0);
        AddBarLabel(panel, "Audio Source", 2);
        AddBarLabel(panel, "Input", 4);

        ConfigureCombo(_videoDeviceCombo);
        ConfigureCombo(_audioDeviceCombo);
        ConfigureCombo(_inputCombo);

        _inputCombo.Items.AddRange(["Composite / RCA", "S-Video"]);
        _inputCombo.SelectedIndex = 0;

        panel.Controls.Add(_videoDeviceCombo, 1, 1);
        panel.Controls.Add(_audioDeviceCombo, 3, 1);
        panel.Controls.Add(_inputCombo, 5, 1);

        ConfigureSmallButton(_refreshButton, "Refresh");
        ConfigureSmallButton(_startPreviewButton, "Start");
        ConfigureSmallButton(_fullScreenButton, "Full Screen");
        ConfigureSmallButton(_installFfmpegButton, "Install FFmpeg");

        panel.Controls.Add(_refreshButton, 6, 1);
        panel.Controls.Add(_startPreviewButton, 7, 1);
        panel.Controls.Add(_fullScreenButton, 8, 1);
        panel.Controls.Add(_installFfmpegButton, 9, 1);

        return panel;
    }

    private Control BuildJobBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = Color.FromArgb(42, 44, 47)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 6));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        AddJobLabel(panel, "Customer", 0);
        AddJobLabel(panel, "Tape Label", 2);
        AddJobLabel(panel, "Save Folder", 4);

        ConfigureTextBox(_customerText);
        ConfigureTextBox(_tapeLabelText);
        ConfigureTextBox(_saveFolderText);

        panel.Controls.Add(_customerText, 1, 1);
        panel.Controls.Add(_tapeLabelText, 3, 1);
        panel.Controls.Add(_saveFolderText, 5, 1);

        ConfigureSmallButton(_browseButton, "Browse...");
        panel.Controls.Add(_browseButton, 6, 1);

        return panel;
    }

    private static void AddJobLabel(TableLayoutPanel panel, string text, int column)
    {
        panel.Controls.Add(
            new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            },
            column,
            0);
    }

    private static void ConfigureTextBox(TextBox textBox)
    {
        textBox.Dock = DockStyle.Fill;
        textBox.BackColor = Color.FromArgb(27, 29, 31);
        textBox.ForeColor = Color.WhiteSmoke;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Margin = new Padding(0, 2, 8, 3);
    }

    private static void AddBarLabel(TableLayoutPanel panel, string text, int column)
    {
        panel.Controls.Add(
            new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            },
            column,
            0);
    }

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(30, 32, 35);
        combo.ForeColor = Color.WhiteSmoke;
        combo.Margin = new Padding(0, 0, 8, 0);
    }

    private static void ConfigureSmallButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(112, 116, 122);
        button.BackColor = Color.FromArgb(72, 75, 80);
        button.ForeColor = Color.White;
        button.Margin = new Padding(5, 0, 0, 0);
    }

    private Control BuildPreviewPanel()
    {
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(12, 13, 14)
        };

        _previewBox.Dock = DockStyle.Fill;
        _previewBox.BackColor = Color.Black;
        _previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        _previewBox.BorderStyle = BorderStyle.FixedSingle;

        var overlay = new Label
        {
            Text = "LIVE PREVIEW",
            AutoSize = true,
            ForeColor = Color.FromArgb(165, 165, 165),
            BackColor = Color.FromArgb(120, 0, 0, 0),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Padding = new Padding(8, 4, 8, 4),
            Location = new Point(18, 18)
        };

        outer.Controls.Add(_previewBox);
        outer.Controls.Add(overlay);
        overlay.BringToFront();

        return outer;
    }

    private Control BuildStatusBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 2,
            Padding = new Padding(16, 8, 16, 6),
            BackColor = Color.FromArgb(45, 47, 50)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));

        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _statusLamp.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_statusLamp, 0, 0);

        _statusText.AutoSize = true;
        _statusText.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        _statusText.ForeColor = Color.White;
        _statusText.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_statusText, 1, 0);

        AddStatusCaption(panel, "TIME", 2);
        _timerText.Text = "00:00:00";
        _timerText.AutoSize = true;
        _timerText.Font = new Font("Consolas", 17F, FontStyle.Bold);
        _timerText.ForeColor = Color.White;
        _timerText.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_timerText, 3, 0);

        AddStatusCaption(panel, "FILE", 4);
        _fileSizeText.Text = "0 MB";
        _fileSizeText.AutoSize = true;
        _fileSizeText.ForeColor = Color.WhiteSmoke;
        _fileSizeText.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_fileSizeText, 5, 0);

        AddStatusCaption(panel, "DROPPED", 6);
        _droppedFramesText.Text = "0";
        _droppedFramesText.AutoSize = true;
        _droppedFramesText.ForeColor = Color.WhiteSmoke;
        _droppedFramesText.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_droppedFramesText, 7, 0);

        _resolutionText.Text = "720 × 480 source • 640 × 480 MP4";
        _resolutionText.AutoSize = true;
        _resolutionText.ForeColor = Color.Silver;
        _resolutionText.Anchor = AnchorStyles.Right;
        panel.Controls.Add(_resolutionText, 9, 0);

        _engineText.AutoSize = true;
        _engineText.ForeColor = Color.FromArgb(190, 190, 190);
        _engineText.Font = new Font("Segoe UI", 8.5F);
        _engineText.Anchor = AnchorStyles.Left;
        panel.SetColumnSpan(_engineText, 10);
        panel.Controls.Add(_engineText, 0, 1);

        SetUiState(CaptureUiState.Ready);
        return panel;
    }

    private static void AddStatusCaption(
        TableLayoutPanel panel,
        string text,
        int column)
    {
        panel.Controls.Add(
            new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Color.Silver,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Anchor = AnchorStyles.Right
            },
            column,
            0);
    }

    private Control BuildTransportBar()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(10, 8, 10, 8),
            BackColor = Color.FromArgb(25, 27, 29)
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 15));

        ConfigureTransportButton(
            _recordButton,
            "●  RECORD",
            Color.FromArgb(139, 34, 38));

        ConfigureTransportButton(
            _pauseButton,
            "Ⅱ  PAUSE",
            Color.FromArgb(78, 82, 87));

        ConfigureTransportButton(
            _stopButton,
            "■  STOP",
            Color.FromArgb(78, 82, 87));

        panel.Controls.Add(_recordButton, 1, 0);
        panel.Controls.Add(_pauseButton, 2, 0);
        panel.Controls.Add(_stopButton, 3, 0);

        panel.Controls.Add(
            new Label
            {
                Text = "Space: Record / Pause",
                AutoSize = true,
                ForeColor = Color.Gray,
                Anchor = AnchorStyles.Right
            },
            5,
            0);

        return panel;
    }

    private static void ConfigureTransportButton(
        Button button,
        string text,
        Color color)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = ControlPaint.Light(color);
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
        button.Margin = new Padding(6, 0, 6, 0);
    }

    private void WireEvents()
    {
        _refreshButton.Click += (_, _) =>
        {
            RefreshFfmpegStatus();
            RefreshDevices();
        };

        _installFfmpegButton.Click += async (_, _) => await InstallFfmpegAsync();

        _startPreviewButton.Click += async (_, _) =>
        {
            if (_captureState is CaptureUiState.Recording
                or CaptureUiState.Paused
                or CaptureUiState.Finalizing)
            {
                return;
            }

            if (_previewService.IsRunning)
            {
                await StopPreviewAsync();
            }
            else
            {
                await StartSelectedPreviewAsync();
            }
        };

        _videoDeviceCombo.SelectionChangeCommitted += async (_, _) =>
        {
            RefreshAudioSources();

            if (_previewService.IsRunning)
            {
                await StartSelectedPreviewAsync();
            }
        };

        _inputCombo.SelectionChangeCommitted += async (_, _) =>
        {
            if (_videoDeviceCombo.SelectedItem is not CaptureDeviceInfo selected ||
                selected.Index < 0)
            {
                return;
            }

            if (_previewService.IsRunning)
            {
                await StartSelectedPreviewAsync();
            }
        };

        _recordButton.Click += async (_, _) => await BeginRecordingAsync();
        _pauseButton.Click += async (_, _) => await TogglePauseAsync();
        _stopButton.Click += async (_, _) => await StopRecordingAsync();
        _fullScreenButton.Click += (_, _) => ShowFullScreenPreview();
        _browseButton.Click += (_, _) => BrowseForSaveFolder();

        _previewService.FrameReady += PreviewService_FrameReady;
        _previewService.PreviewError += (_, message) =>
            SafeBeginInvoke(() =>
            {
                SetUiState(CaptureUiState.Ready);
                MessageBox.Show(
                    this,
                    message,
                    "Preview Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });

        _recordingService.PreviewFrameReady += PreviewService_FrameReady;

        _recordingService.StatisticsChanged += (_, _) =>
            SafeBeginInvoke(UpdateRecordingStatistics);

        _recordingService.RecordingError += (_, message) =>
            SafeBeginInvoke(() =>
            {
                MessageBox.Show(
                    this,
                    message,
                    "Recording Engine",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            });

        WireWorkflowEvents();
        KeyDown += MainForm_KeyDown;
    }

    private void RefreshFfmpegStatus()
    {
        _ffmpegPath = FfmpegLocator.Find();
        var available = !string.IsNullOrWhiteSpace(_ffmpegPath);

        _installFfmpegButton.Visible = !available;
        _engineText.Text = available
            ? $"Recording engine ready: {Path.GetFileName(_ffmpegPath)}"
            : "FFmpeg recording engine is not installed. Click Install FFmpeg before recording.";

        _engineText.ForeColor = available
            ? Color.FromArgb(175, 210, 175)
            : Color.FromArgb(245, 190, 105);
    }

    private async Task InstallFfmpegAsync()
    {
        _installFfmpegButton.Enabled = false;
        _engineText.Text =
            "Installing FFmpeg with Windows Package Manager. Follow the installer window...";

        try
        {
            var exitCode = await FfmpegLocator.InstallWithWingetAsync();
            RefreshFfmpegStatus();

            if (_ffmpegPath is null)
            {
                MessageBox.Show(
                    this,
                    $"The installer finished with code {exitCode}, but FFmpeg was not found yet. " +
                    "Close and reopen Tape Lady Capture Suite.",
                    "FFmpeg Installation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(
                    this,
                    "FFmpeg is installed and the recording engine is ready.",
                    "FFmpeg Installation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Install FFmpeg",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _installFfmpegButton.Enabled = true;
        }
    }

    private void RefreshDevices()
    {
        var priorVideo = _videoDeviceCombo.SelectedItem?.ToString();
        var priorAudio = _audioDeviceCombo.SelectedItem?.ToString();

        _videoDeviceCombo.BeginUpdate();
        _videoDeviceCombo.Items.Clear();

        foreach (var device in DeviceService.GetVideoDevices())
        {
            _videoDeviceCombo.Items.Add(device);
        }

        _videoDeviceCombo.EndUpdate();

        if (_videoDeviceCombo.Items.Count == 0)
        {
            _videoDeviceCombo.Items.Add(
                new CaptureDeviceInfo(-1, "No video devices found", string.Empty));
        }

        SelectPreferredVideoDevice(_videoDeviceCombo, priorVideo);

        RefreshAudioSources(priorAudio);
    }

    private void RefreshAudioSources(string? previous = null)
    {
        previous ??= _audioDeviceCombo.SelectedItem?.ToString();

        _audioDeviceCombo.BeginUpdate();
        _audioDeviceCombo.Items.Clear();
        _audioDeviceCombo.Items.Add(new AudioSourceInfo(
            AudioSourceKind.None,
            "(No audio)",
            string.Empty));

        if (_videoDeviceCombo.SelectedItem is CaptureDeviceInfo videoDevice &&
            videoDevice.Index >= 0)
        {
            // Do not invent an FFmpeg audio device from a pin label. ArcSoft's
            // "Audio Pin Source" is a driver control, not necessarily the name
            // of a DirectShow audio-capture device that FFmpeg can open.
            // Milestone 5.1 incorrectly combined video=<device>:audio=<device>,
            // which caused an immediate I/O error on this EZCAP driver.
        }

        var audioDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var deviceName in DeviceService.GetAudioCaptureDevices())
        {
            audioDevices.Add(deviceName);
        }

        if (!string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            foreach (var deviceName in DeviceService.GetFfmpegAudioCaptureDevices(_ffmpegPath))
            {
                audioDevices.Add(deviceName);
            }
        }

        foreach (var deviceName in audioDevices.OrderBy(name => name))
        {
            _audioDeviceCombo.Items.Add(new AudioSourceInfo(
                AudioSourceKind.WindowsDevice,
                deviceName,
                deviceName));
        }

        _audioDeviceCombo.EndUpdate();
        SelectPreferredAudioDevice(_audioDeviceCombo, previous);
    }

    private static void SelectPreferredVideoDevice(
        ComboBox combo,
        string? previous)
    {
        if (TrySelectText(combo, previous))
        {
            return;
        }

        var preferredWords = new[]
        {
            "EZCAP", "USB Video", "Video Grabber", "AV TO USB", "OEM Device"
        };

        for (var index = 0; index < combo.Items.Count; index++)
        {
            var name = combo.Items[index]?.ToString() ?? string.Empty;
            if (preferredWords.Any(word =>
                    name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedIndex = index;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static void SelectPreferredAudioDevice(
        ComboBox combo,
        string? previous)
    {
        if (TrySelectText(combo, previous))
        {
            return;
        }

        var preferredWords = new[]
        {
            "Audio Pin Source", "EZCAP", "USB", "Digital Audio Interface", "Audio Grabber"
        };

        for (var index = 1; index < combo.Items.Count; index++)
        {
            var name = combo.Items[index]?.ToString() ?? string.Empty;
            if (preferredWords.Any(word =>
                    name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                combo.SelectedIndex = index;
                return;
            }
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static bool TrySelectText(ComboBox combo, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (string.Equals(
                    combo.Items[index]?.ToString(),
                    text,
                    StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return true;
            }
        }

        return false;
    }

    private async Task StartSelectedPreviewAsync()
    {
        if (_videoDeviceCombo.SelectedItem is not CaptureDeviceInfo device ||
            device.Index < 0)
        {
            SetUiState(CaptureUiState.Ready);
            return;
        }

        try
        {
            _startPreviewButton.Enabled = false;
            _statusText.Text = "CONNECTING...";
            _statusLamp.LampColor = Color.Goldenrod;

            await _previewService.StopAsync();

            var selectedInput = _inputCombo.SelectedIndex == 1
                ? VideoInputKind.SVideo
                : VideoInputKind.Composite;

            DeviceService.TrySetVideoInput(
                device,
                selectedInput,
                out var inputMessage);

            _engineText.Text = inputMessage;
            _engineText.ForeColor = Color.FromArgb(190, 195, 200);

            await _previewService.StartAsync(device.Index);

            _startPreviewButton.Text = "Stop";
            SetUiState(CaptureUiState.Preview);
        }
        catch (Exception ex)
        {
            SetUiState(CaptureUiState.Ready);
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Start Preview",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _startPreviewButton.Enabled = true;
        }
    }

    private async Task StopPreviewAsync()
    {
        _startPreviewButton.Enabled = false;
        _statusText.Text = "STOPPING...";
        _statusLamp.LampColor = Color.Goldenrod;

        await _previewService.StopAsync();
        ClearPreviewImage();

        _startPreviewButton.Text = "Start";
        SetUiState(CaptureUiState.Ready);
        _startPreviewButton.Enabled = true;
    }

    private async Task BeginRecordingAsync()
    {
        if (_captureState == CaptureUiState.Paused)
        {
            await ResumeRecordingAsync();
            return;
        }

        if (_captureState != CaptureUiState.Preview)
        {
            return;
        }

        RefreshFfmpegStatus();

        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            MessageBox.Show(
                this,
                "Install FFmpeg before recording. Use the Install FFmpeg button at the top.",
                "Recording Engine Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (_videoDeviceCombo.SelectedItem is not CaptureDeviceInfo videoDevice ||
            videoDevice.Index < 0)
        {
            MessageBox.Show(
                this,
                "Select a video capture device.",
                "Video Device Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_tapeLabelText.Text))
        {
            _tapeLabelText.Focus();
            MessageBox.Show(
                this,
                "Enter the tape label before recording. This becomes the MP4 filename.",
                "Tape Label Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var outputPath = BuildOutputPath();
        if (File.Exists(outputPath))
        {
            var overwrite = MessageBox.Show(
                this,
                $"A file named '{Path.GetFileName(outputPath)}' already exists.\n\nReplace it?",
                "File Already Exists",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (overwrite != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            LockRecordingControls(true);

            await _previewService.StopAsync();
            ClearPreviewImage();

            _activeOutputPath = outputPath;
            _recordingStarted = DateTime.Now;
            _pausedDuration = TimeSpan.Zero;
            _pauseStarted = null;

            var audioSource = _audioDeviceCombo.SelectedItem as AudioSourceInfo;

            await _recordingService.StartSessionAsync(
                _ffmpegPath,
                videoDevice.Name,
                audioSource,
                outputPath);

            _recordingTimer.Start();
            _fileTimer.Start();
            SetUiState(CaptureUiState.Recording);
        }
        catch (Exception ex)
        {
            _activeOutputPath = null;
            SetUiState(CaptureUiState.Ready);

            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Start Recording",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            await StartSelectedPreviewAsync();
        }
        finally
        {
            LockRecordingControls(false);
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_captureState == CaptureUiState.Recording)
        {
            await PauseRecordingAsync();
        }
        else if (_captureState == CaptureUiState.Paused)
        {
            await ResumeRecordingAsync();
        }
    }

    private async Task PauseRecordingAsync()
    {
        try
        {
            LockRecordingControls(true);
            _pauseStarted = DateTime.Now;
            await _recordingService.PauseAsync();
            SetUiState(CaptureUiState.Paused);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Pause",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            LockRecordingControls(false);
        }
    }

    private async Task ResumeRecordingAsync()
    {
        try
        {
            LockRecordingControls(true);

            if (_pauseStarted.HasValue)
            {
                _pausedDuration += DateTime.Now - _pauseStarted.Value;
                _pauseStarted = null;
            }

            await _recordingService.ResumeAsync();
            SetUiState(CaptureUiState.Recording);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Resume",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            LockRecordingControls(false);
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_captureState is not CaptureUiState.Recording
            and not CaptureUiState.Paused)
        {
            return;
        }

        try
        {
            LockRecordingControls(true);
            SetUiState(CaptureUiState.Finalizing);

            if (_pauseStarted.HasValue)
            {
                _pausedDuration += DateTime.Now - _pauseStarted.Value;
                _pauseStarted = null;
            }

            _recordingTimer.Stop();
            _fileTimer.Stop();

            var savedPath = await _recordingService.StopSessionAsync();
            _activeOutputPath = savedPath;
            UpdateRecordingStatistics();
            CompleteActiveQueueItem(savedPath);

            MessageBox.Show(
                this,
                $"Capture complete.\n\n{savedPath}",
                "Tape Lady Capture Suite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to Finalize Recording",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _activeOutputPath = null;
            ClearPreviewImage();
            SetUiState(CaptureUiState.Ready);
            LockRecordingControls(false);
            await StartSelectedPreviewAsync();
        }
    }

    private string BuildOutputPath()
    {
        var baseFolder = string.IsNullOrWhiteSpace(_saveFolderText.Text)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "Tape Lady Captures")
            : _saveFolderText.Text.Trim();

        var customer = SanitizeFileName(_customerText.Text.Trim());
        var tape = SanitizeFileName(_tapeLabelText.Text.Trim());

        var folder = string.IsNullOrWhiteSpace(customer)
            ? baseFolder
            : Path.Combine(baseFolder, customer);

        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{tape}.mp4");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(
            value.Select(character =>
                    invalid.Contains(character) ? '_' : character)
                .ToArray());

        return cleaned.Trim().TrimEnd('.');
    }

    private void BrowseForSaveFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where Tape Lady Capture Suite saves MP4 files",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_saveFolderText.Text)
                ? _saveFolderText.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _saveFolderText.Text = dialog.SelectedPath;
        }
    }

    private void UpdateRecordingClock()
    {
        if (_captureState is not CaptureUiState.Recording
            and not CaptureUiState.Paused
            and not CaptureUiState.Finalizing)
        {
            return;
        }

        var now = _pauseStarted ?? DateTime.Now;
        var elapsed = now - _recordingStarted - _pausedDuration;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        _timerText.Text =
            $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private void UpdateRecordingStatistics()
    {
        _droppedFramesText.Text = _recordingService.DroppedFrames.ToString();

        if (!string.IsNullOrWhiteSpace(_activeOutputPath) &&
            File.Exists(_activeOutputPath))
        {
            _fileSizeText.Text = FormatFileSize(new FileInfo(_activeOutputPath).Length);
            return;
        }

        _fileSizeText.Text = _captureState is CaptureUiState.Recording
            or CaptureUiState.Paused
            or CaptureUiState.Finalizing
            ? "Recording..."
            : "0 MB";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.0} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        return $"{bytes / 1024d / 1024d / 1024d:0.00} GB";
    }

    private void SetUiState(CaptureUiState state)
    {
        _captureState = state;

        switch (state)
        {
            case CaptureUiState.Ready:
                _statusText.Text = "READY";
                _statusLamp.LampColor = Color.Gray;
                _recordButton.Enabled = false;
                _pauseButton.Enabled = false;
                _stopButton.Enabled = false;
                _pauseButton.Text = "Ⅱ  PAUSE";
                _timerText.Text = "00:00:00";
                break;

            case CaptureUiState.Preview:
                _statusText.Text = "PREVIEW";
                _statusLamp.LampColor = Color.LimeGreen;
                _recordButton.Enabled = true;
                _pauseButton.Enabled = false;
                _stopButton.Enabled = false;
                _pauseButton.Text = "Ⅱ  PAUSE";
                break;

            case CaptureUiState.Recording:
                _statusText.Text = "RECORDING";
                _statusLamp.LampColor = Color.Red;
                _recordButton.Enabled = false;
                _pauseButton.Enabled = true;
                _stopButton.Enabled = true;
                _pauseButton.Text = "Ⅱ  PAUSE";
                break;

            case CaptureUiState.Paused:
                _statusText.Text = "PAUSED";
                _statusLamp.LampColor = Color.Goldenrod;
                _recordButton.Enabled = true;
                _pauseButton.Enabled = true;
                _stopButton.Enabled = true;
                _pauseButton.Text = "▶  RESUME";
                break;

            case CaptureUiState.Finalizing:
                _statusText.Text = "SAVING MP4...";
                _statusLamp.LampColor = Color.DeepSkyBlue;
                _recordButton.Enabled = false;
                _pauseButton.Enabled = false;
                _stopButton.Enabled = false;
                break;
        }

        var sessionActive = state is CaptureUiState.Recording
            or CaptureUiState.Paused
            or CaptureUiState.Finalizing;

        _videoDeviceCombo.Enabled = !sessionActive;
        _audioDeviceCombo.Enabled = !sessionActive;
        _inputCombo.Enabled = !sessionActive;
        _refreshButton.Enabled = !sessionActive;
        _startPreviewButton.Enabled = !sessionActive;
        _customerText.Enabled = !sessionActive;
        _tapeLabelText.Enabled = !sessionActive;
        _saveFolderText.Enabled = !sessionActive;
        _browseButton.Enabled = !sessionActive;
        SetWorkflowEditingEnabled(!sessionActive);
    }

    private void LockRecordingControls(bool locked)
    {
        UseWaitCursor = locked;

        if (locked)
        {
            _recordButton.Enabled = false;
            _pauseButton.Enabled = false;
            _stopButton.Enabled = false;
        }
        else
        {
            SetUiState(_captureState);
        }
    }

    private void PreviewService_FrameReady(object? sender, Bitmap frame)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            frame.Dispose();
            return;
        }

        SafeBeginInvoke(() =>
        {
            var old = _previewBox.Image;
            _previewBox.Image = frame;
            old?.Dispose();

            if (_fullScreenForm is { IsDisposed: false } &&
                _fullScreenForm.Controls
                    .OfType<PictureBox>()
                    .FirstOrDefault() is { } fullScreenBox)
            {
                var fullOld = fullScreenBox.Image;
                fullScreenBox.Image = new Bitmap(frame);
                fullOld?.Dispose();
            }
        });
    }

    private void ClearPreviewImage()
    {
        var old = _previewBox.Image;
        _previewBox.Image = null;
        old?.Dispose();
    }

    private void SafeBeginInvoke(Action action)
    {
        if (_closing || IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Window is closing.
        }
    }

    private void ShowFullScreenPreview()
    {
        if (_fullScreenForm is { IsDisposed: false })
        {
            _fullScreenForm.Activate();
            return;
        }

        var fullScreenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        if (_previewBox.Image is not null)
        {
            fullScreenBox.Image = new Bitmap(_previewBox.Image);
        }

        _fullScreenForm = new Form
        {
            Text = "Tape Lady Capture Suite — Full Screen Preview",
            BackColor = Color.Black,
            FormBorderStyle = FormBorderStyle.None,
            WindowState = FormWindowState.Maximized,
            KeyPreview = true,
            TopMost = true
        };

        _fullScreenForm.Controls.Add(fullScreenBox);
        _fullScreenForm.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Escape or Keys.F11)
            {
                _fullScreenForm.Close();
            }
        };

        _fullScreenForm.FormClosed += (_, _) =>
        {
            fullScreenBox.Image?.Dispose();
            _fullScreenForm = null;
        };

        _fullScreenForm.Show(this);
    }

    private async void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            ShowFullScreenPreview();
            e.Handled = true;
            return;
        }

        // Recording is controlled only by the on-screen buttons.
        // Do not use Space as a global shortcut because typing in Customer,
        // Tape Label, or Notes must never start or pause a recording.
    }

    private async void MainForm_FormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        if (_closing)
        {
            return;
        }

        if (_captureState is CaptureUiState.Recording or CaptureUiState.Paused)
        {
            var result = MessageBox.Show(
                this,
                "A recording is still active.\n\nStop and save it before closing?",
                "Recording in Progress",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == DialogResult.Yes)
            {
                e.Cancel = true;
                await StopRecordingAsync();
                Close();
                return;
            }

            await _recordingService.CancelSessionAsync();
        }

        PersistWorkflowState();
        _closing = true;
        _recordingTimer.Stop();
        _fileTimer.Stop();
        await _previewService.StopAsync();

        ClearPreviewImage();
        _previewService.Dispose();
        _recordingService.Dispose();
    }
}
