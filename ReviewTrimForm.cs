using System.Diagnostics;
using TapeLadyCaptureSuite.Controls;
using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;

namespace TapeLadyCaptureSuite;

internal sealed class ReviewTrimForm : Form
{
    private readonly List<CaptureHistoryItem> _historyItems;
    private readonly string _ffmpegPath;
    private readonly Func<bool> _isCaptureActive;
    private readonly Action _persist;
    private readonly ReviewTrimService _trimService = new();
    private readonly ListView _videos = new();
    private readonly ComboBox _filter = new();
    private readonly WindowsMediaPlayerHost _player = new();
    private readonly PictureBox _scrubFrame = new();
    private readonly Panel _wmpSurface = new();
    private readonly Panel _scrubSurface = new();
    private readonly TrackBar _timeline = new();
    private readonly TrackBar _volume = new();
    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly Label _position = new();
    private readonly Label _trimStart = new();
    private readonly Label _trimEnd = new();
    private readonly Button _play = new();
    private readonly Button _pause = new();
    private readonly Button _mute = new();
    private readonly Button _fastTrim = new();
    private readonly Button _frameTrim = new();
    private readonly Button _noTrim = new();
    private readonly System.Windows.Forms.Timer _playbackTimer = new();
    private CaptureHistoryItem? _selected;
    private TimeSpan _duration;
    private TimeSpan _start;
    private TimeSpan _end;
    private TimeSpan? _cutPreviewStop;
    private bool _scrubbing;
    private TimeSpan _selectedTimelinePosition;
    private bool _hasSelectedTimelinePosition;
    private long _lastScrubFrameRequestTicks;
    private CancellationTokenSource? _scrubFrameCancellation;
    private int _scrubFrameRequestVersion;
    private bool _busy;

    public ReviewTrimForm(
        List<CaptureHistoryItem> historyItems,
        string ffmpegPath,
        Func<bool> isCaptureActive,
        Action persist)
    {
        _historyItems = historyItems;
        _ffmpegPath = ffmpegPath;
        _isCaptureActive = isCaptureActive;
        _persist = persist;

        Text = "Tape Lady - Review & Trim";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1420, 880);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        _playbackTimer.Interval = 200;
        _playbackTimer.Tick += (_, _) => UpdatePlaybackUi();
        _playbackTimer.Start();
        FormClosed += (_, _) =>
        {
            CancelScrubFrameRequest();
            _player.Stop();
        };
        RefreshVideoList();
    }

    public event EventHandler<QueuedTrimRequest>? FrameAccurateTrimRequested;

    public void RefreshFromOwner()
    {
        RefreshVideoList();
        if (_selected is not null)
        {
            UpdateSelectedLabels();
        }
    }

    private void BuildInterface()
    {
        var root = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 365,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(20, 21, 23),
            FixedPanel = FixedPanel.Panel1
        };
        root.Panel1.Padding = new Padding(10);
        root.Panel2.Padding = new Padding(10);
        root.Panel1.Controls.Add(BuildReviewList());
        root.Panel2.Controls.Add(BuildEditor());
        Controls.Add(root);
    }

    private Control BuildReviewList()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        header.Controls.Add(new Label
        {
            Text = "REVIEW VIDEOS",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _filter.Items.AddRange(["Needs Review", "Complete", "All"]);
        _filter.SelectedIndex = 0;
        _filter.Dock = DockStyle.Fill;
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.BackColor = Color.FromArgb(27, 29, 31);
        _filter.ForeColor = Color.WhiteSmoke;
        _filter.SelectedIndexChanged += (_, _) => RefreshVideoList();
        header.Controls.Add(_filter, 0, 1);

        ConfigureList(_videos);
        _videos.Columns.Add("Status", 104);
        _videos.Columns.Add("Customer", 105);
        _videos.Columns.Add("Tape", 125);
        _videos.SelectedIndexChanged += (_, _) => OpenSelectedVideo();
        _videos.DoubleClick += (_, _) => OpenSelectedVideo();

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.Silver;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "Select a completed recording.";

        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(_videos, 0, 1);
        layout.Controls.Add(_status, 0, 2);
        return layout;
    }

    private Control BuildEditor()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        _title.Dock = DockStyle.Fill;
        _title.ForeColor = Color.White;
        _title.Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold);
        _title.Text = "No recording selected";
        _title.TextAlign = ContentAlignment.MiddleLeft;

        var playerSurface = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Padding = new Padding(1) };
        _wmpSurface.Dock = DockStyle.Fill;
        _wmpSurface.BackColor = Color.Black;
        _wmpSurface.Controls.Add(_player);
        _scrubSurface.Dock = DockStyle.Fill;
        _scrubSurface.BackColor = Color.Black;
        _scrubFrame.Dock = DockStyle.Fill;
        _scrubFrame.BackColor = Color.Black;
        _scrubFrame.SizeMode = PictureBoxSizeMode.Zoom;
        _scrubSurface.Controls.Add(_scrubFrame);
        playerSurface.Controls.Add(_wmpSurface);
        playerSurface.Controls.Add(_scrubSurface);
        _scrubSurface.Visible = false;

        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 2 };
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 55));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        ConfigureButton(_play, "Play");
        ConfigureButton(_pause, "Pause");
        ConfigureButton(_mute, "Mute");
        _play.Click += (_, _) => ResumePlayback();
        _pause.Click += (_, _) => _player.Pause();
        _mute.Click += (_, _) => { _player.Muted = !_player.Muted; _mute.Text = _player.Muted ? "Unmute" : "Mute"; };
        _position.Dock = DockStyle.Fill;
        _position.ForeColor = Color.WhiteSmoke;
        _position.TextAlign = ContentAlignment.MiddleCenter;
        _timeline.Dock = DockStyle.Fill;
        _timeline.Minimum = 0;
        _timeline.Maximum = 10_000;
        _timeline.TickStyle = TickStyle.None;
        _timeline.MouseDown += (_, _) =>
        {
            _scrubbing = true;
            _lastScrubFrameRequestTicks = 0;
            RequestScrubFrame(force: true);
        };
        _timeline.Scroll += (_, _) => RequestScrubFrame(force: false);
        _timeline.MouseUp += (_, _) =>
        {
            RequestScrubFrame(force: true);
            Seek(_selectedTimelinePosition);
            _scrubbing = false;
        };
        _volume.Dock = DockStyle.Fill;
        _volume.Minimum = 0;
        _volume.Maximum = 100;
        _volume.Value = 80;
        _volume.TickStyle = TickStyle.None;
        _volume.ValueChanged += (_, _) => _player.Volume = _volume.Value;
        playback.Controls.Add(_play, 0, 0);
        playback.Controls.Add(_pause, 1, 0);
        playback.Controls.Add(_timeline, 2, 0);
        playback.Controls.Add(_position, 3, 0);
        playback.Controls.Add(_mute, 4, 0);
        playback.Controls.Add(_volume, 5, 0);

        var trimPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 4) };
        trimPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        trimPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        trimPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        trimPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        trimPanel.Controls.Add(BuildTrimGroup("TRIM START", _trimStart, true), 0, 0);
        trimPanel.Controls.Add(BuildTrimGroup("TRIM END", _trimEnd, false), 1, 0);
        trimPanel.SetRowSpan(trimPanel.GetControlFromPosition(0, 0)!, 2);
        trimPanel.SetRowSpan(trimPanel.GetControlFromPosition(1, 0)!, 2);

        var cuts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 4) };
        cuts.Controls.Add(CreateButton("Preview Start Cut", (_, _) => PreviewCut(_start)));
        cuts.Controls.Add(CreateButton("Preview End Cut", (_, _) => PreviewCut(_end)));
        cuts.Controls.Add(CreateButton("Jump to Start", (_, _) => ShowSelectedFrame(_start)));
        cuts.Controls.Add(CreateButton("Jump to End", (_, _) => ShowSelectedFrame(_end)));

        var operations = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 4) };
        _fastTrim.Text = "Fast / Lossless Trim";
        _frameTrim.Text = "Frame-Accurate Trim";
        _noTrim.Text = "No Trim Needed";
        ConfigureButton(_fastTrim, _fastTrim.Text);
        ConfigureButton(_frameTrim, _frameTrim.Text);
        ConfigureButton(_noTrim, _noTrim.Text);
        _fastTrim.Click += async (_, _) => await RunFastTrimAsync();
        _frameTrim.Click += (_, _) => QueueFrameAccurateTrim();
        _noTrim.Click += (_, _) => MarkNoTrimNeeded();
        operations.Controls.Add(_fastTrim);
        operations.Controls.Add(_frameTrim);
        operations.Controls.Add(_noTrim);

        layout.Controls.Add(_title, 0, 0);
        layout.Controls.Add(playerSurface, 0, 1);
        layout.Controls.Add(playback, 0, 2);
        layout.Controls.Add(trimPanel, 0, 3);
        layout.Controls.Add(cuts, 0, 4);
        layout.Controls.Add(operations, 0, 5);
        return layout;
    }

    private Control BuildTrimGroup(string caption, Label value, bool isStart)
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(4) };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        group.BackColor = Color.FromArgb(45, 47, 50);
        group.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Color.Silver, TextAlign = ContentAlignment.MiddleCenter }, 0, 0);
        value.Dock = DockStyle.Fill;
        value.ForeColor = Color.White;
        value.Font = new Font("Consolas", 12F, FontStyle.Bold);
        value.TextAlign = ContentAlignment.MiddleCenter;
        group.Controls.Add(value, 0, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        controls.Controls.Add(CreateButton("Set Current", (_, _) => SetTrimAtCurrent(isStart)));
        foreach (double seconds in new[] { -30d, -5d, -1d, 1d, 5d, 30d })
        {
            string text = seconds > 0 ? $"+{seconds:0}" : seconds.ToString("0");
            controls.Controls.Add(CreateButton(text, (_, _) => NudgeTrim(isStart, seconds)));
        }
        group.Controls.Add(controls, 0, 2);
        return group;
    }

    private void RefreshVideoList()
    {
        CaptureHistoryItem? desired = _selected;
        _videos.BeginUpdate();
        _videos.Items.Clear();
        foreach (CaptureHistoryItem item in FilteredItems())
        {
            var row = new ListViewItem(StatusText(item.ReviewStatus)) { Tag = item };
            row.SubItems.Add(item.Customer);
            row.SubItems.Add(item.TapeLabel);
            if (item.ReviewStatus == CaptureReviewStatus.NeedsReview)
            {
                row.ForeColor = Color.FromArgb(245, 190, 105);
            }
            _videos.Items.Add(row);
            if (ReferenceEquals(item, desired))
            {
                row.Selected = true;
            }
        }
        _videos.EndUpdate();
    }

    private IEnumerable<CaptureHistoryItem> FilteredItems()
    {
        return _historyItems
            .OrderBy(item => item.ReviewStatus == CaptureReviewStatus.NeedsReview ? 0 : 1)
            .ThenByDescending(item => item.CapturedAt)
            .Where(item => _filter.SelectedIndex switch
            {
                0 => item.ReviewStatus == CaptureReviewStatus.NeedsReview,
                1 => item.ReviewStatus != CaptureReviewStatus.NeedsReview,
                _ => true
            });
    }

    private void OpenSelectedVideo()
    {
        if (_videos.SelectedItems.Count == 0 || _videos.SelectedItems[0].Tag is not CaptureHistoryItem item)
        {
            return;
        }

        if (!File.Exists(item.OutputPath))
        {
            _status.Text = "The selected completed recording is unavailable.";
            return;
        }

        _selected = item;
        _duration = TimeSpan.Zero;
        _start = TimeSpan.Zero;
        _end = TimeSpan.Zero;
        _cutPreviewStop = null;
        _hasSelectedTimelinePosition = false;
        CancelScrubFrameRequest();
        ClearScrubFrame();
        ShowWmpSurface();
        _player.Open(item.OutputPath);
        UpdateSelectedLabels();
        _status.Text = _isCaptureActive()
            ? "Reviewing a completed file while capture is active."
            : "Ready to review.";
    }

    private void UpdatePlaybackUi()
    {
        if (_selected is null)
        {
            return;
        }

        double playerDuration = _player.Duration;
        if (playerDuration > 0 && _duration == TimeSpan.Zero)
        {
            _duration = TimeSpan.FromSeconds(playerDuration);
            _end = _duration;
            UpdateSelectedLabels();
        }

        TimeSpan current = TimeSpan.FromSeconds(Math.Max(0, _player.Position));
        if (!_scrubbing && _duration > TimeSpan.Zero)
        {
            _timeline.Value = Math.Clamp((int)Math.Round(current.TotalSeconds / _duration.TotalSeconds * _timeline.Maximum), _timeline.Minimum, _timeline.Maximum);
        }
        if (_scrubbing)
        {
            TimeSpan selected = TimelinePosition();
            _position.Text = $"{FormatTime(selected)} / {FormatTime(_duration)}";
        }
        else
        {
            _position.Text = $"{FormatTime(current)} / {FormatTime(_duration)}";
        }

        if (_cutPreviewStop.HasValue && current >= _cutPreviewStop.Value)
        {
            _player.Pause();
            _cutPreviewStop = null;
        }
    }

    private void SetTrimAtCurrent(bool isStart)
    {
        TimeSpan current = _hasSelectedTimelinePosition
            ? _selectedTimelinePosition
            : TimeSpan.FromSeconds(Math.Max(0, _player.Position));
        if (isStart)
        {
            _start = ClampStart(current);
        }
        else
        {
            _end = ClampEnd(current);
        }
        UpdateSelectedLabels();
    }

    private void NudgeTrim(bool isStart, double seconds)
    {
        if (isStart)
        {
            _start = ClampStart(_start + TimeSpan.FromSeconds(seconds));
        }
        else
        {
            _end = ClampEnd(_end + TimeSpan.FromSeconds(seconds));
        }
        UpdateSelectedLabels();
    }

    private TimeSpan ClampStart(TimeSpan value) =>
        _duration <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Clamp(value.Ticks, 0, Math.Max(0, _end.Ticks - TimeSpan.TicksPerMillisecond)));

    private TimeSpan ClampEnd(TimeSpan value) =>
        _duration <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Clamp(value.Ticks, Math.Min(_duration.Ticks, _start.Ticks + TimeSpan.TicksPerMillisecond), _duration.Ticks));

    private TimeSpan TimelinePosition()
    {
        return _duration <= TimeSpan.Zero
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(_duration.TotalSeconds * _timeline.Value / _timeline.Maximum);
    }

    private void RequestScrubFrame(bool force)
    {
        if (!_scrubbing || _duration <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan target = TimelinePosition();
        _selectedTimelinePosition = target;
        _hasSelectedTimelinePosition = true;
        _position.Text = $"{FormatTime(target)} / {FormatTime(_duration)}";

        const long minimumRequestIntervalMilliseconds = 180;
        long now = Environment.TickCount64;
        if (!force && now - _lastScrubFrameRequestTicks < minimumRequestIntervalMilliseconds)
        {
            return;
        }

        _lastScrubFrameRequestTicks = now;
        _ = ExtractScrubFrameAsync(target);
    }

    private void Seek(TimeSpan position)
    {
        _player.Position = Math.Clamp(position.TotalSeconds, 0, _duration.TotalSeconds);
    }

    private void ResumePlayback()
    {
        if (_hasSelectedTimelinePosition)
        {
            Seek(_selectedTimelinePosition);
        }

        CancelScrubFrameRequest();
        ClearScrubFrame();
        _hasSelectedTimelinePosition = false;
        ShowWmpSurface();
        _player.Play();
    }

    private void ShowSelectedFrame(TimeSpan position)
    {
        if (_duration <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan target = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, _duration.Ticks));
        _selectedTimelinePosition = target;
        _hasSelectedTimelinePosition = true;
        _position.Text = $"{FormatTime(target)} / {FormatTime(_duration)}";
        _timeline.Value = Math.Clamp(
            (int)Math.Round(target.TotalSeconds / _duration.TotalSeconds * _timeline.Maximum),
            _timeline.Minimum,
            _timeline.Maximum);
        Seek(target);
        _ = ExtractScrubFrameAsync(target);
    }

    private async Task ExtractScrubFrameAsync(TimeSpan target)
    {
        if (_selected is null || !File.Exists(_selected.OutputPath))
        {
            return;
        }

        CancelScrubFrameRequest();
        var cancellation = new CancellationTokenSource();
        _scrubFrameCancellation = cancellation;
        int requestVersion = Interlocked.Increment(ref _scrubFrameRequestVersion);
        string path = _selected.OutputPath;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(target.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(path);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("image2pipe");
            startInfo.ArgumentList.Add("-vcodec");
            startInfo.ArgumentList.Add("mjpeg");
            startInfo.ArgumentList.Add("pipe:1");

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("FFmpeg could not start scrub-frame extraction.");
            using var registration = cancellation.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            });
            using var imageBytes = new MemoryStream();
            Task copy = process.StandardOutput.BaseStream.CopyToAsync(imageBytes, cancellation.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellation.Token);
            await Task.WhenAll(copy, errors, process.WaitForExitAsync(cancellation.Token));
            if (process.ExitCode != 0 || imageBytes.Length == 0)
            {
                return;
            }

            imageBytes.Position = 0;
            using var extracted = Image.FromStream(imageBytes);
            var frame = new Bitmap(extracted);
            if (IsDisposed || cancellation.IsCancellationRequested ||
                requestVersion != Volatile.Read(ref _scrubFrameRequestVersion) ||
                !string.Equals(path, _selected?.OutputPath, StringComparison.OrdinalIgnoreCase))
            {
                frame.Dispose();
                return;
            }

            BeginInvoke(() => ShowScrubFrame(frame, requestVersion));
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Retain the previous frame rather than replacing it with an error image.
        }
        finally
        {
            if (ReferenceEquals(_scrubFrameCancellation, cancellation))
            {
                _scrubFrameCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void ShowScrubFrame(Bitmap frame, int requestVersion)
    {
        if (IsDisposed || requestVersion != Volatile.Read(ref _scrubFrameRequestVersion))
        {
            frame.Dispose();
            return;
        }

        var old = _scrubFrame.Image;
        _scrubFrame.Image = frame;
        _wmpSurface.Visible = false;
        _scrubSurface.Visible = true;
        _scrubSurface.BringToFront();
        old?.Dispose();
    }

    private void ClearScrubFrame()
    {
        var old = _scrubFrame.Image;
        _scrubFrame.Image = null;
        old?.Dispose();
    }

    private void ShowWmpSurface()
    {
        _scrubSurface.Visible = false;
        _wmpSurface.Visible = true;
        _wmpSurface.BringToFront();
    }

    private void CancelScrubFrameRequest()
    {
        Interlocked.Increment(ref _scrubFrameRequestVersion);
        _scrubFrameCancellation?.Cancel();
        _scrubFrameCancellation = null;
    }

    private void PreviewCut(TimeSpan cut)
    {
        if (_duration <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan start = TimeSpan.FromSeconds(Math.Max(0, cut.TotalSeconds - 5));
        _cutPreviewStop = TimeSpan.FromSeconds(Math.Min(_duration.TotalSeconds, cut.TotalSeconds + 5));
        Seek(start);
        _player.Play();
    }

    private async Task RunFastTrimAsync()
    {
        if (!CanTrim() || !ConfirmTrim(TrimMethod.FastLossless))
        {
            return;
        }

        SetBusy(true, "Fast / Lossless trim is running on the completed file...");
        try
        {
            var request = CreateTrimRequest(TrimMethod.FastLossless);
            TrimResult result = await _trimService.TrimAsync(_ffmpegPath, request, CancellationToken.None);
            ApplyTrimCompletion(request, result);
            ReloadTrimmedVideo(result.FinalDurationSeconds);
            _status.Text = "Trim complete - original preserved in Originals.";
        }
        catch (Exception ex)
        {
            _status.Text = "Trim failed. The original recording was preserved.";
            MessageBox.Show(this, ex.Message, "Unable to Trim Recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void QueueFrameAccurateTrim()
    {
        if (!CanTrim() || !ConfirmTrim(TrimMethod.FrameAccurate))
        {
            return;
        }

        var queued = new QueuedTrimRequest(_ffmpegPath, CreateTrimRequest(TrimMethod.FrameAccurate));
        FrameAccurateTrimRequested?.Invoke(this, queued);
        _status.Text = _isCaptureActive()
            ? "Frame-accurate trim queued - waiting for active recording to finish."
            : "Frame-accurate trim queued.";
        SetBusy(true, null);
    }

    private void MarkNoTrimNeeded()
    {
        if (_selected is null || _busy)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "Mark this video as No Trim Needed?\n\nThe video file will not be modified. " +
                "This item will be marked Complete - No Trim Needed and removed from Needs Review.",
                "No Trim Needed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _selected.ReviewStatus = CaptureReviewStatus.CompleteNoTrimNeeded;
        _selected.ReviewedAt = DateTime.Now;
        _selected.TrimMethod = null;
        _persist();
        _status.Text = "Marked complete - no trim needed.";
        RefreshVideoList();
    }

    private bool CanTrim()
    {
        if (_selected is null || _busy || _duration <= TimeSpan.Zero || _end <= _start)
        {
            return false;
        }

        return true;
    }

    private TrimRequest CreateTrimRequest(TrimMethod method) =>
        new(_selected!, _start, _end, method, _duration.TotalSeconds);

    public void CompleteQueuedTrim(QueuedTrimCompletedEventArgs result)
    {
        if (result.Error is not null)
        {
            if (_selected?.Id == result.Request.TrimRequest.HistoryItem.Id)
            {
                _status.Text = "Frame-accurate trim failed. The original recording was preserved.";
                MessageBox.Show(this, result.Error.Message, "Unable to Trim Recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else if (result.Result is not null)
        {
            if (_selected?.Id == result.Request.TrimRequest.HistoryItem.Id)
            {
                ReloadTrimmedVideo(result.Result.FinalDurationSeconds);
                _status.Text = "Trim complete - original preserved in Originals.";
            }
            RefreshVideoList();
            if (_selected is not null)
            {
                UpdateSelectedLabels();
            }
        }
        SetBusy(false, null);
    }

    private void ApplyTrimCompletion(TrimRequest request, TrimResult result)
    {
        CaptureHistoryItem item = request.HistoryItem;
        item.ReviewStatus = CaptureReviewStatus.CompleteTrimmed;
        item.OriginalBackupPath = result.BackupPath;
        item.OriginalDurationSeconds = request.OriginalDurationSeconds;
        item.FinalDurationSeconds = result.FinalDurationSeconds;
        item.TrimStartSeconds = request.Start.TotalSeconds;
        item.TrimEndSeconds = request.End.TotalSeconds;
        item.TrimMethod = request.Method;
        item.ReviewedAt = DateTime.Now;
        item.FileSizeBytes = File.Exists(item.OutputPath) ? new FileInfo(item.OutputPath).Length : 0;
        _persist();
        RefreshVideoList();
    }

    private bool ConfirmTrim(TrimMethod method)
    {
        TimeSpan resulting = _end - _start;
        string mode = method == TrimMethod.FastLossless ? "Fast / Lossless" : "Frame-Accurate";
        string message =
            $"Trim this video?\n\nStart: {FormatTime(_start)}\nEnd: {FormatTime(_end)}\n" +
            $"Resulting duration: approximately {FormatTime(resulting)}\n\nMode: {mode}\n\n" +
            "The untouched original recording will be preserved in the Originals folder.";
        return MessageBox.Show(
            this,
            message,
            "Confirm Trim",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void ReloadTrimmedVideo(double finalDurationSeconds)
    {
        if (_selected is null)
        {
            return;
        }

        CancelScrubFrameRequest();
        ClearScrubFrame();
        ShowWmpSurface();
        _player.Stop();
        _duration = TimeSpan.FromSeconds(Math.Max(0, finalDurationSeconds));
        _start = TimeSpan.Zero;
        _end = _duration;
        _selectedTimelinePosition = TimeSpan.Zero;
        _hasSelectedTimelinePosition = false;
        _timeline.Value = _timeline.Minimum;
        _position.Text = $"{FormatTime(TimeSpan.Zero)} / {FormatTime(_duration)}";
        _player.Open(_selected.OutputPath);
        UpdateSelectedLabels();
        RefreshVideoList();
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        _fastTrim.Enabled = !busy;
        _frameTrim.Enabled = !busy;
        _noTrim.Enabled = !busy;
        if (status is not null)
        {
            _status.Text = status;
        }
    }

    private void UpdateSelectedLabels()
    {
        if (_selected is null)
        {
            return;
        }

        _title.Text = $"{_selected.Customer} - {_selected.TapeLabel} ({StatusText(_selected.ReviewStatus)})";
        _trimStart.Text = FormatTime(_start);
        _trimEnd.Text = FormatTime(_end);
    }

    private static void ConfigureList(ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.BackColor = Color.FromArgb(24, 26, 28);
        list.ForeColor = Color.WhiteSmoke;
        list.BorderStyle = BorderStyle.FixedSingle;
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button();
        ConfigureButton(button, text);
        button.AutoSize = true;
        button.Click += handler;
        return button;
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(112, 116, 122);
        button.BackColor = Color.FromArgb(72, 75, 80);
        button.ForeColor = Color.White;
        button.Margin = new Padding(3);
    }

    private static string StatusText(CaptureReviewStatus status) => status switch
    {
        CaptureReviewStatus.NeedsReview => "Needs Review",
        CaptureReviewStatus.CompleteTrimmed => "Complete - Trimmed",
        _ => "Complete - No Trim"
    };

    private static string FormatTime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}