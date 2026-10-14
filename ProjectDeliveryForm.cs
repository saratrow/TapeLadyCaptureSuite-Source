using TapeLadyCaptureSuite.Models;
using TapeLadyCaptureSuite.Services;

namespace TapeLadyCaptureSuite;

internal sealed class ProjectDeliveryForm : Form
{
    private readonly Customer _customer;
    private readonly CustomerProject _project;
    private readonly IReadOnlyList<CaptureHistoryItem> _historyItems;
    private readonly ComboBox _driveCombo = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _driveDetailsLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _refreshButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _cancelButton = new();
    private CancellationTokenSource? _deliveryCancellation;
    private bool _busy;

    public bool DeliverySucceeded { get; private set; }
    public int DeliveredFileCount { get; private set; }
    public string? DestinationDrive { get; private set; }

    public ProjectDeliveryForm(Customer customer, CustomerProject project, IReadOnlyList<CaptureHistoryItem> historyItems)
    {
        _customer = customer;
        _project = project;
        _historyItems = historyItems;

        Text = "Prepare Delivery";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 420);
        MinimumSize = new Size(600, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        RefreshDeliveryState();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(16),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        _summaryLabel.Dock = DockStyle.Fill;
        _summaryLabel.ForeColor = Color.WhiteSmoke;
        _summaryLabel.TextAlign = ContentAlignment.TopLeft;
        root.Controls.Add(_summaryLabel, 0, 0);
        root.SetColumnSpan(_summaryLabel, 2);

        var driveLabel = new Label { Text = "TAPELADY USB", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver };
        root.Controls.Add(driveLabel, 0, 1);
        ConfigureCombo(_driveCombo);
        root.Controls.Add(_driveCombo, 1, 1);

        _driveDetailsLabel.Dock = DockStyle.Fill;
        _driveDetailsLabel.ForeColor = Color.Silver;
        _driveDetailsLabel.TextAlign = ContentAlignment.TopLeft;
        root.Controls.Add(_driveDetailsLabel, 1, 2);

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Minimum = 0;
        root.Controls.Add(_progressBar, 0, 3);
        root.SetColumnSpan(_progressBar, 2);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.Silver;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_statusLabel, 0, 4);
        root.SetColumnSpan(_statusLabel, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        ConfigureButton(_cancelButton, "Close");
        ConfigureButton(_copyButton, "Copy && Verify");
        ConfigureButton(_refreshButton, "Refresh Drives");
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_copyButton);
        buttons.Controls.Add(_refreshButton);
        root.Controls.Add(buttons, 0, 6);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        _refreshButton.Click += (_, _) => RefreshDeliveryState();
        _driveCombo.SelectedIndexChanged += (_, _) => UpdateDriveDetails();
        _copyButton.Click += async (_, _) => await CopyAndVerifyAsync();
        _cancelButton.Click += (_, _) =>
        {
            if (_busy)
            {
                _deliveryCancellation?.Cancel();
            }
            else
            {
                Close();
            }
        };
        FormClosing += (_, eventArgs) =>
        {
            if (_busy)
            {
                eventArgs.Cancel = true;
                _deliveryCancellation?.Cancel();
            }
        };
    }

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = Color.FromArgb(27, 29, 31);
        combo.ForeColor = Color.WhiteSmoke;
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 30;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 104, 110);
        button.BackColor = Color.FromArgb(65, 68, 73);
        button.ForeColor = Color.White;
        button.Margin = new Padding(4, 4, 0, 4);
    }

    private void RefreshDeliveryState()
    {
        if (_busy)
        {
            return;
        }

        DeliveryPlan plan = ProjectDeliveryService.BuildPlan(_project, _historyItems);
        _summaryLabel.Text =
            $"Customer: {_customer.FullName}\n" +
            $"Project: {_project.DisplayName}\n" +
            $"Finished videos: {plan.Candidates.Count}\n" +
            $"Total delivery size: {ProjectDeliveryService.FormatSize(plan.PayloadBytes)}" +
            (_project.DeliveredAt.HasValue
                ? $"\nPreviously delivered: {_project.DeliveredAt.Value:g}"
                : string.Empty);

        _driveCombo.BeginUpdate();
        _driveCombo.Items.Clear();
        foreach (RemovableDriveInfo drive in RemovableDriveService.FindTapeLadyDrives())
        {
            _driveCombo.Items.Add(drive);
        }
        _driveCombo.EndUpdate();
        if (_driveCombo.Items.Count == 1)
        {
            _driveCombo.SelectedIndex = 0;
        }

        _progressBar.Value = 0;
        if (!plan.CanDeliver)
        {
            _statusLabel.ForeColor = Color.FromArgb(245, 190, 105);
            _statusLabel.Text = string.Join(" ", plan.BlockingReasons);
            _copyButton.Enabled = false;
        }
        else if (_driveCombo.Items.Count == 0)
        {
            _statusLabel.ForeColor = Color.FromArgb(245, 190, 105);
            _statusLabel.Text = "Insert a removable drive labeled TAPELADY, then select Refresh Drives.";
            _copyButton.Enabled = false;
        }
        else if (_driveCombo.Items.Count > 1)
        {
            _statusLabel.ForeColor = Color.FromArgb(245, 190, 105);
            _statusLabel.Text = "Select the TAPELADY USB drive for this delivery.";
            _copyButton.Enabled = false;
        }
        else
        {
            _statusLabel.ForeColor = Color.Silver;
            _statusLabel.Text = "Ready to copy and verify finished videos.";
            _copyButton.Enabled = true;
        }

        UpdateDriveDetails();
    }

    private void UpdateDriveDetails()
    {
        if (_driveCombo.SelectedItem is not RemovableDriveInfo drive)
        {
            _driveDetailsLabel.Text = string.Empty;
            if (!_busy && _driveCombo.Items.Count > 1)
            {
                _copyButton.Enabled = false;
            }
            return;
        }

        _driveDetailsLabel.Text =
            $"Label: {drive.VolumeLabel}\n" +
            $"Free space: {ProjectDeliveryService.FormatSize(drive.AvailableFreeSpace)}\n" +
            $"Filesystem: {drive.DriveFormat}";
        if (!_busy && ProjectDeliveryService.BuildPlan(_project, _historyItems).CanDeliver)
        {
            _copyButton.Enabled = true;
        }
    }

    private async Task CopyAndVerifyAsync()
    {
        if (_busy || _driveCombo.SelectedItem is not RemovableDriveInfo selectedDrive)
        {
            return;
        }

        DeliveryPlan plan = ProjectDeliveryService.BuildPlan(_project, _historyItems);
        if (!plan.CanDeliver)
        {
            ShowBlocked(plan.BlockingReasons);
            RefreshDeliveryState();
            return;
        }

        if (!RemovableDriveService.TryGetTapeLadyDrive(selectedDrive.RootPath, out RemovableDriveInfo? currentDrive) || currentDrive is null)
        {
            _statusLabel.Text = "The selected TAPELADY USB drive is no longer available. Refresh drives and try again.";
            return;
        }

        DeliveryDriveCheck driveCheck = ProjectDeliveryService.ValidateDrive(currentDrive, plan.Candidates);
        if (!driveCheck.IsValid)
        {
            _statusLabel.ForeColor = Color.FromArgb(245, 190, 105);
            _statusLabel.Text = driveCheck.ErrorMessage ?? "The selected USB cannot be used for delivery.";
            return;
        }

        List<DeliveryTarget>? targets = ResolveCollisions(currentDrive.RootPath, plan.Candidates);
        if (targets is null)
        {
            return;
        }

        string confirmation = _project.DeliveredAt.HasValue
            ? "This project was previously delivered. Re-delivery will copy and verify the selected finished videos again. Continue?"
            : "Copy and verify all finished videos to the selected TAPELADY USB drive?";
        if (MessageBox.Show(this, confirmation, _project.DeliveredAt.HasValue ? "Confirm Re-Delivery" : "Confirm Delivery",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true, targets.Count);
        _deliveryCancellation = new CancellationTokenSource();
        var progress = new Progress<DeliveryProgress>(UpdateProgress);
        try
        {
            for (int index = 0; index < targets.Count; index++)
            {
                DeliveryTarget target = targets[index];
                if (target.VerifyExisting)
                {
                    await ProjectDeliveryService.VerifyExistingAsync(target.Candidate, target.DestinationPath, index, targets.Count, progress, _deliveryCancellation.Token);
                }
                else
                {
                    await ProjectDeliveryService.CopyAndVerifyAsync(target.Candidate, target.DestinationPath, index, targets.Count, progress, _deliveryCancellation.Token);
                }
            }

            DeliverySucceeded = true;
            DeliveredFileCount = targets.Count;
            DestinationDrive = currentDrive.RootPath;
            SetBusy(false, 0);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            _statusLabel.ForeColor = Color.FromArgb(245, 190, 105);
            _statusLabel.Text = "Delivery canceled. Any previously verified files were left in place; the project was not marked delivered.";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.FromArgb(235, 130, 130);
            _statusLabel.Text = $"Delivery failed: {ex.Message}";
        }
        finally
        {
            _deliveryCancellation?.Dispose();
            _deliveryCancellation = null;
            SetBusy(false, 0);
        }
    }

    private List<DeliveryTarget>? ResolveCollisions(string rootPath, IReadOnlyList<DeliveryCandidate> candidates)
    {
        var targets = new List<DeliveryTarget>();
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeliveryCandidate candidate in candidates)
        {
            string destinationPath = Path.Combine(rootPath, candidate.DestinationFileName);
            if (File.Exists(destinationPath) || reservedPaths.Contains(destinationPath))
            {
                CollisionChoice choice = PromptCollision(candidate.DestinationFileName);
                if (choice == CollisionChoice.Cancel)
                {
                    return null;
                }

                if (choice == CollisionChoice.Skip)
                {
                    targets.Add(new DeliveryTarget(candidate, destinationPath, true));
                    reservedPaths.Add(destinationPath);
                    continue;
                }

                destinationPath = FindUnusedDestinationPath(rootPath, candidate.DestinationFileName, reservedPaths);
            }

            targets.Add(new DeliveryTarget(candidate, destinationPath, false));
            reservedPaths.Add(destinationPath);
        }

        return targets;
    }

    private CollisionChoice PromptCollision(string fileName)
    {
        using var dialog = new Form
        {
            Text = "USB Filename Collision",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(500, 165),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = BackColor,
            ForeColor = ForeColor
        };
        var message = new Label
        {
            Text = $"The USB root already contains {fileName}.\n\nSkip verifies the existing file against the source. Rename creates a new copy without overwriting it.",
            Location = new Point(16, 16),
            Size = new Size(468, 70),
            ForeColor = ForeColor
        };
        var skip = new Button { Text = "Skip", Location = new Point(16, 112), Size = new Size(92, 30) };
        var rename = new Button { Text = "Rename New Copy", Location = new Point(116, 112), Size = new Size(130, 30) };
        var cancel = new Button { Text = "Cancel Delivery", Location = new Point(362, 112), Size = new Size(122, 30), DialogResult = DialogResult.Cancel };
        skip.Click += (_, _) => dialog.DialogResult = DialogResult.Yes;
        rename.Click += (_, _) => dialog.DialogResult = DialogResult.Retry;
        dialog.Controls.AddRange([message, skip, rename, cancel]);
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) switch
        {
            DialogResult.Yes => CollisionChoice.Skip,
            DialogResult.Retry => CollisionChoice.Rename,
            _ => CollisionChoice.Cancel
        };
    }

    private static string FindUnusedDestinationPath(string rootPath, string fileName, ISet<string> reservedPaths)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        int suffix = 2;
        while (true)
        {
            string path = Path.Combine(rootPath, $"{stem} ({suffix++}){extension}");
            if (!File.Exists(path) && !reservedPaths.Contains(path))
            {
                return path;
            }
        }
    }

    private void UpdateProgress(DeliveryProgress progress)
    {
        _progressBar.Value = Math.Min(_progressBar.Maximum, progress.CompletedFiles);
        _statusLabel.ForeColor = Color.Silver;
        _statusLabel.Text = $"{progress.State}: {progress.FileName} ({progress.CompletedFiles} of {progress.TotalFiles})";
    }

    private void SetBusy(bool busy, int totalFiles)
    {
        _busy = busy;
        _refreshButton.Enabled = !busy;
        _driveCombo.Enabled = !busy;
        _copyButton.Enabled = !busy;
        _cancelButton.Text = busy ? "Cancel Delivery" : "Close";
        _progressBar.Maximum = Math.Max(1, totalFiles);
        _progressBar.Value = 0;
    }

    private void ShowBlocked(IReadOnlyList<string> reasons)
    {
        MessageBox.Show(this, string.Join("\n\n", reasons), "Delivery Blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed record DeliveryTarget(DeliveryCandidate Candidate, string DestinationPath, bool VerifyExisting);

    private enum CollisionChoice
    {
        Skip,
        Rename,
        Cancel
    }
}
