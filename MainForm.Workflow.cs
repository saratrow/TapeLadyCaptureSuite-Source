using System.Diagnostics;
using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;

namespace TapeLadyCaptureSuite;

internal sealed partial class MainForm
{
    private readonly List<Customer> _customers = [];
    private readonly List<CustomerProject> _projects = [];
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
    private readonly Button _editDetailsButton = new();
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
        _queueList.Columns.Add("Project", 92);
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
        _historyList.Columns.Add("Project", 92);
        _historyList.Columns.Add("Tape", 115);
        root.Controls.Add(_historyList, 0, 1);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        ConfigureWorkflowButton(_openFileButton, "Play File");
        ConfigureWorkflowButton(_openFolderButton, "Open Folder");
        ConfigureWorkflowButton(_editDetailsButton, "Edit Details");
        buttons.Controls.Add(_openFileButton, 0, 0);
        buttons.Controls.Add(_openFolderButton, 1, 0);
        buttons.Controls.Add(_editDetailsButton, 2, 0);
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
        _editDetailsButton.Click += (_, _) => EditSelectedHistoryDetails();
        _historyList.DoubleClick += (_, _) => OpenSelectedHistoryFile(false);
        _reviewVideosButton.Click += (_, _) => ShowReviewVideos();
        _saveFolderText.TextChanged += (_, _) => UpdateDiskSpaceLabel();
    }

    private void RestoreWorkflowState()
    {
        var state = AppStateService.Load();
        _customers.Clear();
        _customers.AddRange(state.Customers);
        _projects.Clear();
        _projects.AddRange(state.Projects);
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
        MigrateLegacyOwnership();
        RefreshCustomerProjectSelectors(null, null);
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
                Customers = _customers,
                Projects = _projects,
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
        var customer = SelectedCustomer;
        var project = SelectedProject;
        var tape = _tapeLabelText.Text.Trim();
        if (customer is null || project is null || string.IsNullOrWhiteSpace(tape))
        {
            MessageBox.Show(this, "Select a customer, project, and tape title first.", "Queue Item", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _queueItems.Add(new QueueItem
        {
            CustomerId = customer.Id,
            ProjectId = project.Id,
            Customer = customer.FullName,
            ProjectName = project.DisplayName,
            TapeLabel = tape,
            Notes = _notesText.Text.Trim()
        });
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
        SelectCustomer(_customers.FirstOrDefault(customer => customer.Id == item.CustomerId));
        SelectProject(_projects.FirstOrDefault(project => project.Id == item.ProjectId));
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
        var customer = _activeRecordingCustomer ?? SelectedCustomer;
        var project = _activeRecordingProject ?? SelectedProject;
        if (customer is null || project is null)
        {
            return;
        }

        var tape = _tapeLabelText.Text.Trim();
        var item = _activeQueueItem ?? _queueItems.FirstOrDefault(q =>
            q.Status != "Completed" &&
            q.CustomerId == customer.Id &&
            q.ProjectId == project.Id &&
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
            CustomerId = customer.Id,
            ProjectId = project.Id,
            Customer = customer.FullName,
            ProjectName = project.DisplayName,
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
            row.SubItems.Add(item.ProjectName);
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
            row.SubItems.Add(item.ProjectName);
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

    private void EditSelectedHistoryDetails()
    {
        var item = SelectedHistoryItem();
        if (item is null)
        {
            return;
        }

        using var details = new RecordingDetailsForm(_customers, _projects, item);
        if (details.ShowDialog(this) != DialogResult.OK || details.Customer is null || details.Project is null)
        {
            return;
        }

        var targetPath = BuildOutputPath(details.Customer, details.Project, details.TapeTitle);
        if (!string.Equals(item.OutputPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(targetPath))
            {
                MessageBox.Show(this, "A recording already exists at the corrected customer, project, and title. Details were not changed.", "Edit Details", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var titleChanged = !string.Equals(item.TapeLabel, details.TapeTitle, StringComparison.Ordinal);
            var locationChanged = item.CustomerId != details.Customer.Id || item.ProjectId != details.Project.Id;
            var confirmation = titleChanged && locationChanged
                ? "This will move and rename the saved recording. Continue?"
                : locationChanged
                    ? "This will move the saved recording to the selected customer/project folder. Continue?"
                    : "This will rename the saved recording. Continue?";

            if (MessageBox.Show(this, confirmation, "Update Recording", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                MoveRecordingFiles(item, targetPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Unable to Move Recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        item.CustomerId = details.Customer.Id;
        item.ProjectId = details.Project.Id;
        item.Customer = details.Customer.FullName;
        item.ProjectName = details.Project.DisplayName;
        item.TapeLabel = details.TapeTitle;
        PersistWorkflowState();
        RefreshHistoryList();
    }

    private static void MoveRecordingFiles(CaptureHistoryItem item, string targetPath)
    {
        if (!File.Exists(item.OutputPath))
        {
            throw new FileNotFoundException("The saved recording is no longer available.", item.OutputPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var sourcePath = item.OutputPath;
        var sourceBackupPath = item.OriginalBackupPath;
        string? targetBackupPath = null;
        if (!string.IsNullOrWhiteSpace(sourceBackupPath) && File.Exists(sourceBackupPath))
        {
            targetBackupPath = Path.Combine(Path.GetDirectoryName(targetPath)!, "Originals", Path.GetFileName(targetPath));
            if (File.Exists(targetBackupPath))
            {
                throw new IOException("An original backup already exists at the corrected project location.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetBackupPath)!);
        }

        File.Move(sourcePath, targetPath);
        try
        {
            if (targetBackupPath is not null && sourceBackupPath is not null)
            {
                File.Move(sourceBackupPath, targetBackupPath);
                item.OriginalBackupPath = targetBackupPath;
            }
            item.OutputPath = targetPath;
        }
        catch
        {
            if (File.Exists(targetPath) && !File.Exists(sourcePath))
            {
                File.Move(targetPath, sourcePath);
            }
            throw;
        }
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

    private Customer? SelectedCustomer => _customerText.SelectedItem as Customer;

    private CustomerProject? SelectedProject => _projectCombo.SelectedItem as CustomerProject;

    private void RefreshCustomerProjectSelectors(Guid? customerId, Guid? projectId)
    {
        _customerText.BeginUpdate();
        _customerText.Items.Clear();
        foreach (var customer in _customers.OrderBy(customer => customer.FullName))
        {
            _customerText.Items.Add(customer);
        }
        _customerText.EndUpdate();

        SelectComboItem<Customer>(_customerText, customerId, customer => customer.Id);
        RefreshProjectSelector(customerId, projectId);
    }

    private void RefreshProjectSelector(Guid? customerId, Guid? projectId)
    {
        _projectCombo.BeginUpdate();
        _projectCombo.Items.Clear();
        if (customerId.HasValue)
        {
            foreach (var project in _projects
                         .Where(project => project.CustomerId == customerId.Value)
                         .OrderByDescending(project => project.DropOffDate))
            {
                _projectCombo.Items.Add(project);
            }
        }
        _projectCombo.EndUpdate();
        SelectComboItem<CustomerProject>(_projectCombo, projectId, project => project.Id);
    }

    private static void SelectComboItem<T>(ComboBox combo, Guid? id, Func<T, Guid> getId)
    {
        combo.SelectedIndex = -1;
        if (!id.HasValue)
        {
            return;
        }

        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is T item && getId(item) == id.Value)
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }

    private void SelectCustomer(Customer? customer)
    {
        SelectComboItem<Customer>(_customerText, customer?.Id, item => item.Id);
        RefreshProjectSelector(customer?.Id, null);
    }

    private void SelectProject(CustomerProject? project)
    {
        if (project is null)
        {
            SelectComboItem<CustomerProject>(_projectCombo, null, item => item.Id);
            return;
        }

        var customer = _customers.FirstOrDefault(item => item.Id == project.CustomerId);
        SelectComboItem<Customer>(_customerText, customer?.Id, item => item.Id);
        RefreshProjectSelector(project.CustomerId, project.Id);
    }

    private void ShowCustomerProjectManager()
    {
        using var manager = new CustomerProjectForm(
            _customers,
            _projects,
            SelectedCustomer,
            SelectedProject);
        var result = manager.ShowDialog(this);

        if (manager.StateChanged)
        {
            PersistWorkflowState();
        }

        if (result == DialogResult.OK &&
            manager.SelectedCustomer is not null &&
            manager.SelectedProject is not null)
        {
            RefreshCustomerProjectSelectors(manager.SelectedCustomer.Id, manager.SelectedProject.Id);
        }
        else
        {
            RefreshCustomerProjectSelectors(SelectedCustomer?.Id, SelectedProject?.Id);
        }
    }

    private void MigrateLegacyOwnership()
    {
        foreach (var item in _queueItems)
        {
            var customerId = item.CustomerId;
            var projectId = item.ProjectId;
            EnsureOwnership(item.Customer, item.ProjectName, item.CreatedAt, ref customerId, ref projectId);
            item.CustomerId = customerId;
            item.ProjectId = projectId;
        }

        foreach (var item in _historyItems)
        {
            var customerId = item.CustomerId;
            var projectId = item.ProjectId;
            EnsureOwnership(item.Customer, item.ProjectName, item.CapturedAt, ref customerId, ref projectId);
            item.CustomerId = customerId;
            item.ProjectId = projectId;
        }
    }

    private void EnsureOwnership(
        string customerName,
        string projectName,
        DateTime date,
        ref Guid? customerId,
        ref Guid? projectId)
    {
        if (!customerId.HasValue)
        {
            var customer = _customers.FirstOrDefault(item =>
                string.Equals(item.FullName, customerName, StringComparison.OrdinalIgnoreCase));
            if (customer is null)
            {
                customer = new Customer { FullName = string.IsNullOrWhiteSpace(customerName) ? "Unassigned" : customerName };
                _customers.Add(customer);
            }
            customerId = customer.Id;
        }

        if (!projectId.HasValue)
        {
            var resolvedCustomerId = customerId.Value;
            var project = _projects.FirstOrDefault(item =>
                item.CustomerId == resolvedCustomerId &&
                string.Equals(item.DisplayName, projectName, StringComparison.OrdinalIgnoreCase));
            if (project is null)
            {
                project = new CustomerProject
                {
                    CustomerId = resolvedCustomerId,
                    DropOffDate = date.Date,
                    Name = string.IsNullOrWhiteSpace(projectName) ? null : projectName
                };
                _projects.Add(project);
            }
            projectId = project.Id;
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
