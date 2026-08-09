using TapeLadyCaptureSuite.Services.Capture;

namespace TapeLadyCaptureSuite;

/// <summary>
/// Read-only hardware report window for DirectShow capture devices.
/// </summary>
internal sealed class HardwareDiagnosticsForm : Form
{
    private readonly TextBox _reportText = new();
    private readonly Button _refreshButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _previewButton = new();
    private readonly Label _statusLabel = new();

    public HardwareDiagnosticsForm()
    {
        Text = "Tape Lady Capture Suite — Hardware Diagnostics";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, 560);
        Size = new Size(980, 720);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);

        BuildInterface();
        WireEvents();

        Shown += async (_, _) => await RefreshReportAsync();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = BackColor
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "DIRECTSHOW HARDWARE DIAGNOSTICS\r\nThis report shows the devices, pins, and media types Windows exposes to capture software.",
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.Silver;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Ready.";
        root.Controls.Add(_statusLabel, 0, 1);

        _reportText.Dock = DockStyle.Fill;
        _reportText.Multiline = true;
        _reportText.ReadOnly = true;
        _reportText.WordWrap = false;
        _reportText.ScrollBars = ScrollBars.Both;
        _reportText.BackColor = Color.FromArgb(22, 24, 26);
        _reportText.ForeColor = Color.WhiteSmoke;
        _reportText.BorderStyle = BorderStyle.FixedSingle;
        _reportText.Font = new Font("Consolas", 9.5F);
        root.Controls.Add(_reportText, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        ConfigureButton(_refreshButton, "Refresh Report");
        ConfigureButton(_saveButton, "Save Report...");
        ConfigureButton(_copyButton, "Copy Report");
        ConfigureButton(_previewButton, "Test DirectShow Preview...");

        buttons.Controls.Add(_refreshButton);
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_copyButton);
        buttons.Controls.Add(_previewButton);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
    }

    private static void ConfigureButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(100, 104, 110);
        button.BackColor = Color.FromArgb(65, 68, 73);
        button.ForeColor = Color.White;
        button.Margin = new Padding(8, 0, 0, 0);
    }

    private void WireEvents()
    {
        _refreshButton.Click += async (_, _) => await RefreshReportAsync();
        _saveButton.Click += (_, _) => SaveReport();
        _copyButton.Click += (_, _) => CopyReport();
        _previewButton.Click += (_, _) => ShowDirectShowPreview();
    }

    private void ShowDirectShowPreview()
    {
        using var previewForm = new DirectShowPreviewForm();
        previewForm.ShowDialog(this);
    }

    private async Task RefreshReportAsync()
    {
        SetBusy(true, "Inspecting DirectShow capture devices...");

        try
        {
            var report = await Task.Run(DirectShowDeviceExplorer.BuildTextReport);
            _reportText.Text = report;
            _reportText.SelectionStart = 0;
            _reportText.SelectionLength = 0;
            _statusLabel.Text = "Inspection complete. Review the report or save it as a text file.";
        }
        catch (Exception ex)
        {
            _reportText.Text =
                "The DirectShow hardware inspection could not be completed.\r\n\r\n" +
                ex;
            _statusLabel.Text = "Inspection failed.";
        }
        finally
        {
            SetBusy(false, _statusLabel.Text);
        }
    }

    private void SaveReport()
    {
        if (string.IsNullOrWhiteSpace(_reportText.Text))
        {
            MessageBox.Show(this, "Generate the report before saving it.", "Hardware Diagnostics",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save Tape Lady Hardware Diagnostics",
            Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            AddExtension = true,
            FileName = $"TapeLady-Hardware-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _reportText.Text);
            _statusLabel.Text = $"Report saved: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to Save Report",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyReport()
    {
        if (string.IsNullOrWhiteSpace(_reportText.Text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_reportText.Text);
            _statusLabel.Text = "Report copied to the clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to Copy Report",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        UseWaitCursor = busy;
        _refreshButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
        _copyButton.Enabled = !busy;
        _previewButton.Enabled = !busy;
        _statusLabel.Text = status;
    }
}
