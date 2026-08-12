using System.Diagnostics;
using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;

namespace TapeLadyCaptureSuite;

internal sealed partial class MainForm
{
    private readonly List<QueueItem> _queueItems = [];
    private readonly List<CaptureHistoryItem> _historyItems = [];
    private readonly ListView _queueList = new();
    private readonly ListView _historyList = new();
    private readonly TextBox _notesText = new();
    private readonly Button _addQueueButton = new();
    private readonly Button _loadQueueButton = new();
    private readonly Button _completeQueueButton = new();
    private readonly Button _deleteQueueButton = new();
    private readonly Button _openFileButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _reviewVideosButton = new();
    private readonly Label _diskSpaceLabel = new();
    private QueueItem? _activeQueueItem;
    private ReviewTrimForm? _reviewTrimForm;

    private Control BuildWorkflowPanel()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            Padding = new Point(14, 6)
        };

        var queuePage = new TabPage("Tape Queue") { BackColor = Color.FromArgb(34, 36, 39), ForeColor = Color.White };
        var historyPage = new TabPage("Capture History") { BackColor = Color.FromArgb(34, 36, 39), ForeColor = Color.White };
        queuePage.Controls.Add(BuildQueuePage());
        historyPage.Controls.Add(BuildHistoryPage());
        tabs.TabPages.Add(queuePage);
        tabs.TabPages.Add(historyPage);
        return tabs;
    }

    private Control BuildQueuePage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 1,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(34, 36, 39)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var title = new Label
        {
            Text = "TODAY'S TAPE QUEUE",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        ConfigureListView(_queueList);
        _queueList.Columns.Add("Status", 74);
        _queueList.Columns.Add("Customer", 125);
        _queueList.Columns.Add("Tape", 120);

        var notesLabel = new Label
        {
            Text = "Tape Notes",
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
        };
        ConfigureTextBox(_notesText);
        _notesText.Multiline = true;
        _notesText.ScrollBars = ScrollBars.Vertical;
        _notesText.Margin = new Padding(0, 2, 0, 6);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        ConfigureWorkflowButton(_addQueueButton, "+ Add Current");
        ConfigureWorkflowButton(_loadQueueButton, "Load Selected");
        ConfigureWorkflowButton(_completeQueueButton, "Mark Complete");
        ConfigureWorkflowButton(_deleteQueueButton, "Delete");
        buttons.Controls.Add(_addQueueButton, 0, 0);
        buttons.Controls.Add(_loadQueueButton, 1, 0);
        buttons.Controls.Add(_completeQueueButton, 0, 1);
        buttons.Controls.Add(_deleteQueueButton, 1, 1);

        _diskSpaceLabel.Dock = DockStyle.Fill;
        _diskSpaceLabel.ForeColor = Color.Silver;
        _diskSpaceLabel.TextAlign = ContentAlignment.MiddleLeft;

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_queueList, 0, 1);
        root.Controls.Add(notesLabel, 0, 2);
        root.Controls.Add(_notesText, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        root.Controls.Add(_diskSpaceLabel, 0, 5);
        return root;
    }

    private Control BuildHistoryPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(34, 36, 39)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        header.Controls.Add(new Label
        {
            Text = "RECENT CAPTURES",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        ConfigureWorkflowButton(_reviewVideosButton, "Review Videos (0)");
        _reviewVideosButton.Dock = DockStyle.Fill;
        header.Controls.Add(_reviewVideosButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        ConfigureListView(_historyList);
        _historyList.Columns.Add("Date", 112);
        _historyList.Columns.Add("Customer", 115);
        _historyList.Columns.Add("Tape", 115);
        root.Controls.Add(_historyList, 0, 1);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ConfigureWorkflowButton(_openFileButton, "Play File");
        ConfigureWorkflowButton(_openFolderButton, "Open Folder");
        buttons.Controls.Add(_openFileButton, 0, 0);
        buttons.Controls.Add(_openFolderButton, 1, 0);
        root.Controls.Add(buttons, 0, 2);
        return root;
    }

    private static void ConfigureListView(ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.BackColor = Color.FromArgb(24, 26, 28);
        list.ForeColor = Color.WhiteSmoke;
        list.BorderStyle = BorderStyle.FixedSingle;
        list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
    }

    private static void ConfigureWorkflowButton(Button button, string text)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 104, 110);
        button.BackColor = Color.FromArgb(65, 68, 73);
        button.ForeColor = Color.White;
        button.Margin = new Padding(3);
    }

    private void WireWorkflowEvents()
    {
        _addQueueButton.Click += (_, _) => AddCurrentToQueue();
        _loadQueueButton.Click += (_, _) => LoadSelectedQueueItem();
        _completeQueueButton.Click += (_, _) => MarkSelectedQueueComplete();
        _deleteQueueButton.Click += (_, _) => DeleteSelectedQueueItem();
        _queueList.DoubleClick += (_, _) => LoadSelectedQueueItem();
        _queueList.SelectedIndexChanged += (_, _) => ShowSelectedQueueNotes();
        _notesText.TextChanged += (_, _) => SaveNotesForSelection();
        _openFileButton.Click += (_, _) => OpenSelectedHistoryFile(false);
        _openFolderButton.Click += (_, _) => OpenSelectedHistoryFile(true);
        _historyList.DoubleClick += (_, _) => OpenSelectedHistoryFile(false);
        _reviewVideosButton.Click += (_, _) => ShowReviewVideos();
        _saveFolderText.TextChanged += (_, _) => UpdateDiskSpaceLabel();
    }

    private void RestoreWorkflowState()
    {
        var state = AppStateService.Load();
        _queueItems.Clear();
        _queueItems.AddRange(state.Queue);
        _historyItems.Clear();
        _historyItems.AddRange(state.History.OrderByDescending(item => item.CapturedAt).Take(500));

        if (!string.IsNullOrWhiteSpace(state.SaveFolder))
        {
            _saveFolderText.Text = state.SaveFolder;
        }
        TrySelectText(_videoDeviceCombo, state.PreferredVideoDevice);
        TrySelectText(_audioDeviceCombo, state.PreferredAudioDevice);
        RefreshQueueList();
        RefreshHistoryList();
        UpdateReviewVideosButton();
        UpdateDiskSpaceLabel();
    }

    private void PersistWorkflowState()
    {
        try
        {
            AppStateService.Save(new AppState
            {
                SaveFolder = _saveFolderText.Text.Trim(),
                PreferredVideoDevice = _videoDeviceCombo.SelectedItem?.ToString() ?? string.Empty,
                PreferredAudioDevice = _audioDeviceCombo.SelectedItem?.ToString() ?? string.Empty,
                Queue = _queueItems,
                History = _historyItems
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void AddCurrentToQueue()
    {
        var customer = _customerText.Text.Trim();
        var tape = _tapeLabelText.Text.Trim();
        if (string.IsNullOrWhiteSpace(customer) || string.IsNullOrWhiteSpace(tape))
        {
            MessageBox.Show(this, "Enter both a customer and tape label first.", "Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _queueItems.Add(new QueueItem { Customer = customer, TapeLabel = tape, Notes = _notesText.Text.Trim() });
        RefreshQueueList();
        PersistWorkflowState();
    }

    private QueueItem? SelectedQueueItem() => _queueList.SelectedItems.Count == 0
        ? null
        : _queueList.SelectedItems[0].Tag as QueueItem;

    private CaptureHistoryItem? SelectedHistoryItem() => _historyList.SelectedItems.Count == 0
        ? null
        : _historyList.SelectedItems[0].Tag as CaptureHistoryItem;

    private void LoadSelectedQueueItem()
    {
        var item = SelectedQueueItem();
        if (item is null) return;
        _activeQueueItem = item;
        _customerText.Text = item.Customer;
        _tapeLabelText.Text = item.TapeLabel;
        _notesText.Text = item.Notes;
    }

    private void ShowSelectedQueueNotes()
    {
        var item = SelectedQueueItem();
        if (item is not null && !ReferenceEquals(item, _activeQueueItem))
        {
            _notesText.Text = item.Notes;
        }
    }

    private void SaveNotesForSelection()
    {
        var item = SelectedQueueItem();
        if (item is null) return;
        item.Notes = _notesText.Text;
        PersistWorkflowState();
    }

    private void MarkSelectedQueueComplete()
    {
        var item = SelectedQueueItem();
        if (item is null) return;
        item.Status = "Completed";
        item.CompletedAt ??= DateTime.Now;
        RefreshQueueList();
        PersistWorkflowState();
    }

    private void DeleteSelectedQueueItem()
    {
        var item = SelectedQueueItem();
        if (item is null) return;
        if (MessageBox.Show(this, $"Delete {item.Customer} — {item.TapeLabel} from the queue?", "Delete Queue Item", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _queueItems.Remove(item);
        if (ReferenceEquals(_activeQueueItem, item)) _activeQueueItem = null;
        RefreshQueueList();
        PersistWorkflowState();
    }

    private void CompleteActiveQueueItem(string savedPath)
    {
        var customer = _customerText.Text.Trim();
        var tape = _tapeLabelText.Text.Trim();
        var item = _activeQueueItem ?? _queueItems.FirstOrDefault(q =>
            q.Status != "Completed" &&
            string.Equals(q.Customer, customer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(q.TapeLabel, tape, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            item.Status = "Completed";
            item.CompletedAt = DateTime.Now;
            item.OutputPath = savedPath;
        }

        _historyItems.Insert(0, new CaptureHistoryItem
        {
            CapturedAt = DateTime.Now,
            Customer = customer,
            TapeLabel = tape,
            Notes = _notesText.Text.Trim(),
            OutputPath = savedPath,
            FileSizeBytes = File.Exists(savedPath) ? new FileInfo(savedPath).Length : 0
        });
        _activeQueueItem = null;
        RefreshQueueList();
        RefreshHistoryList();
        UpdateReviewVideosButton();
        PersistWorkflowState();
    }

    private void RefreshQueueList()
    {
        _queueList.BeginUpdate();
        _queueList.Items.Clear();
        foreach (var item in _queueItems.OrderBy(q => q.Status == "Completed").ThenBy(q => q.CreatedAt))
        {
            var row = new ListViewItem(item.Status == "Completed" ? "✓ Done" : "Pending") { Tag = item };
            row.SubItems.Add(item.Customer);
            row.SubItems.Add(item.TapeLabel);
            if (item.Status == "Completed") row.ForeColor = Color.FromArgb(150, 205, 155);
            _queueList.Items.Add(row);
        }
        _queueList.EndUpdate();
    }

    private void RefreshHistoryList()
    {
        _historyList.BeginUpdate();
        _historyList.Items.Clear();
        foreach (var item in _historyItems.OrderByDescending(h => h.CapturedAt).Take(500))
        {
            var row = new ListViewItem(item.CapturedAt.ToString("MM/dd/yy HH:mm")) { Tag = item };
            row.SubItems.Add(item.Customer);
            row.SubItems.Add(item.TapeLabel);
            _historyList.Items.Add(row);
        }
        _historyList.EndUpdate();
        UpdateReviewVideosButton();
    }

    private void UpdateReviewVideosButton()
    {
        int needsReview = _historyItems.Count(item => item.ReviewStatus == CaptureReviewStatus.NeedsReview);
        _reviewVideosButton.Text = $"Review Videos ({needsReview})";
    }

    private void ShowReviewVideos()
    {
        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            RefreshFfmpegStatus();
        }

        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            MessageBox.Show(this, "FFmpeg is required to trim completed recordings.", "Review & Trim", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_reviewTrimForm is { IsDisposed: false })
        {
            _reviewTrimForm.Activate();
            return;
        }

        _reviewTrimForm = new ReviewTrimForm(
            _historyItems,
            _ffmpegPath,
            () => _captureState is CaptureUiState.Recording or CaptureUiState.Paused or CaptureUiState.Finalizing,
            () =>
            {
                PersistWorkflowState();
                RefreshHistoryList();
            });
        _reviewTrimForm.FrameAccurateTrimRequested += (_, request) => _reviewWorkQueue.Enqueue(request);
        _reviewTrimForm.FormClosed += (_, _) => _reviewTrimForm = null;
        _reviewTrimForm.Show(this);
    }

    private void ReviewWorkQueue_Completed(object? sender, QueuedTrimCompletedEventArgs e)
    {
        SafeBeginInvoke(() =>
        {
            if (e.Error is null && e.Result is not null)
            {
                CaptureHistoryItem item = e.Request.TrimRequest.HistoryItem;
                item.ReviewStatus = CaptureReviewStatus.CompleteTrimmed;
                item.OriginalBackupPath = e.Result.BackupPath;
                item.OriginalDurationSeconds = e.Request.TrimRequest.OriginalDurationSeconds;
                item.FinalDurationSeconds = e.Result.FinalDurationSeconds;
                item.TrimStartSeconds = e.Request.TrimRequest.Start.TotalSeconds;
                item.TrimEndSeconds = e.Request.TrimRequest.End.TotalSeconds;
                item.TrimMethod = TrimMethod.FrameAccurate;
                item.ReviewedAt = DateTime.Now;
                item.FileSizeBytes = File.Exists(item.OutputPath) ? new FileInfo(item.OutputPath).Length : 0;
                PersistWorkflowState();
                RefreshHistoryList();
            }

            _reviewTrimForm?.CompleteQueuedTrim(e);
        });
    }

    private void OpenSelectedHistoryFile(bool folder)
    {
        var item = SelectedHistoryItem();
        if (item is null || string.IsNullOrWhiteSpace(item.OutputPath)) return;
        var target = folder ? Path.GetDirectoryName(item.OutputPath) : item.OutputPath;
        if (string.IsNullOrWhiteSpace(target) || (!File.Exists(target) && !Directory.Exists(target)))
        {
            MessageBox.Show(this, "That saved file or folder is no longer available.", "Capture History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private void UpdateDiskSpaceLabel()
    {
        try
        {
            var path = _saveFolderText.Text.Trim();
            var root = Path.GetPathRoot(string.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : path);
            if (string.IsNullOrWhiteSpace(root)) return;
            var drive = new DriveInfo(root);
            _diskSpaceLabel.Text = $"Free space: {FormatFileSize(drive.AvailableFreeSpace)}";
            _diskSpaceLabel.ForeColor = drive.AvailableFreeSpace < 20L * 1024 * 1024 * 1024
                ? Color.FromArgb(245, 190, 105)
                : Color.Silver;
        }
        catch
        {
            _diskSpaceLabel.Text = "Free space: unavailable";
        }
    }

    private void SetWorkflowEditingEnabled(bool enabled)
    {
        _addQueueButton.Enabled = enabled;
        _loadQueueButton.Enabled = enabled;
        _completeQueueButton.Enabled = enabled;
        _deleteQueueButton.Enabled = enabled;
        _queueList.Enabled = enabled;
        _notesText.Enabled = enabled;
    }
}
