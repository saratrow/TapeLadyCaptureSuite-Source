using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite;

internal sealed class CustomerProjectForm : Form
{
    private readonly List<Customer> _customers;
    private readonly List<CustomerProject> _projects;
    private readonly List<CaptureHistoryItem> _historyItems;
    private readonly Action? _projectStateChanged;
    private readonly ListBox _customerList = new();
    private readonly ListBox _projectList = new();
    private readonly Button _newCustomerButton = new();
    private readonly Button _newProjectButton = new();
    private readonly Button _renameProjectButton = new();
    private readonly Button _completeProjectButton = new();
    private readonly Button _reopenProjectButton = new();
    private readonly Button _resumeButton = new();
    private readonly bool _chooseNewProjectDate;

    public Customer? SelectedCustomer { get; private set; }
    public CustomerProject? SelectedProject { get; private set; }
    public bool StateChanged { get; private set; }

    public CustomerProjectForm(
        List<Customer> customers,
        List<CustomerProject> projects,
        List<CaptureHistoryItem> historyItems,
        Customer? selectedCustomer,
        CustomerProject? selectedProject,
        bool chooseNewProjectDate = false,
        Action? projectStateChanged = null)
    {
        _customers = customers;
        _projects = projects;
        _historyItems = historyItems;
        _chooseNewProjectDate = chooseNewProjectDate;
        _projectStateChanged = projectStateChanged;

        Text = "Customers / Projects";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 430);
        Size = new Size(780, 500);
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        RefreshCustomers(selectedCustomer?.Id);
        if (selectedCustomer is not null)
        {
            RefreshProjects(selectedCustomer.Id, selectedProject?.Id);
        }
        if (_chooseNewProjectDate && selectedCustomer is not null)
        {
            Shown += (_, _) => CreateProjectFromDatePicker(selectedCustomer);
        }
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(16),
            BackColor = BackColor
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        root.Controls.Add(CreateHeader("CUSTOMERS"), 0, 0);
        root.Controls.Add(CreateHeader("PROJECT HISTORY"), 1, 0);

        ConfigureList(_customerList);
        ConfigureList(_projectList);
        root.Controls.Add(_customerList, 0, 1);
        root.Controls.Add(_projectList, 1, 1);

        var customerButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        ConfigureButton(_newCustomerButton, "New Customer");
        customerButtons.Controls.Add(_newCustomerButton);
        root.Controls.Add(customerButtons, 0, 2);

        var projectButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        ConfigureButton(_resumeButton, "Use Project");
        ConfigureButton(_reopenProjectButton, "Reopen Project");
        ConfigureButton(_completeProjectButton, "Mark Project Complete");
        ConfigureButton(_renameProjectButton, "Rename");
        ConfigureButton(_newProjectButton, "New Project");
        projectButtons.Controls.Add(_resumeButton);
        projectButtons.Controls.Add(_reopenProjectButton);
        projectButtons.Controls.Add(_completeProjectButton);
        projectButtons.Controls.Add(_renameProjectButton);
        projectButtons.Controls.Add(_newProjectButton);
        root.Controls.Add(projectButtons, 1, 2);

        Controls.Add(root);

        _customerList.SelectedIndexChanged += (_, _) =>
        {
            if (_customerList.SelectedItem is Customer customer)
            {
                RefreshProjects(customer.Id, null);
            }
        };
        _newCustomerButton.Click += (_, _) => AddCustomer();
        _newProjectButton.Click += (_, _) => AddProject();
        _renameProjectButton.Click += (_, _) => RenameProject();
        _completeProjectButton.Click += (_, _) => CompleteProject();
        _reopenProjectButton.Click += (_, _) => ReopenProject();
        _resumeButton.Click += (_, _) => ResumeProject();
        _projectList.DoubleClick += (_, _) => ResumeProject();
        _projectList.SelectedIndexChanged += (_, _) => UpdateProjectActions();
        _projectList.FormattingEnabled = true;
        _projectList.Format += (_, eventArgs) =>
        {
            if (eventArgs.ListItem is CustomerProject project && project.IsCompleted)
            {
                eventArgs.Value = $"{project.DisplayName} - Completed";
            }
        };
        UpdateProjectActions();
    }

    private static Label CreateHeader(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
        ForeColor = Color.White,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static void ConfigureList(ListBox list)
    {
        list.Dock = DockStyle.Fill;
        list.BackColor = Color.FromArgb(24, 26, 28);
        list.ForeColor = Color.WhiteSmoke;
        list.BorderStyle = BorderStyle.FixedSingle;
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

    private void RefreshCustomers(Guid? selectId)
    {
        _customerList.BeginUpdate();
        _customerList.Items.Clear();
        foreach (var customer in _customers.OrderBy(customer => customer.FullName))
        {
            _customerList.Items.Add(customer);
        }
        _customerList.EndUpdate();
        SelectItem<Customer>(_customerList, selectId, customer => customer.Id);
    }

    private void RefreshProjects(Guid customerId, Guid? selectId)
    {
        _projectList.BeginUpdate();
        _projectList.Items.Clear();
        foreach (var project in _projects
                     .Where(project => project.CustomerId == customerId)
                     .OrderByDescending(project => project.DropOffDate))
        {
            _projectList.Items.Add(project);
        }
        _projectList.EndUpdate();
        SelectItem<CustomerProject>(_projectList, selectId, project => project.Id);
        UpdateProjectActions();
    }

    private void UpdateProjectActions()
    {
        bool isCompleted = _projectList.SelectedItem is CustomerProject { IsCompleted: true };
        bool isActive = _projectList.SelectedItem is CustomerProject { IsCompleted: false };
        _completeProjectButton.Visible = isActive;
        _completeProjectButton.Enabled = isActive;
        _reopenProjectButton.Visible = isCompleted;
        _reopenProjectButton.Enabled = isCompleted;
    }

    private static void SelectItem<T>(ListBox list, Guid? id, Func<T, Guid> getId)
    {
        if (id is null)
        {
            return;
        }

        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.Items[index] is T item && getId(item) == id)
            {
                list.SelectedIndex = index;
                return;
            }
        }
    }

    private void AddCustomer()
    {
        var name = PromptForText("New Customer", "Full name:", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var existing = _customers.FirstOrDefault(customer =>
            string.Equals(customer.FullName, name.Trim(), StringComparison.OrdinalIgnoreCase));
        var customer = existing ?? new Customer { FullName = name.Trim() };
        if (existing is null)
        {
            _customers.Add(customer);
        }

        var project = CreateProject(customer);
        if (project is null)
        {
            return;
        }
        StateChanged = true;
        RefreshCustomers(customer.Id);
        RefreshProjects(customer.Id, project.Id);
    }

    private void AddProject()
    {
        if (_customerList.SelectedItem is not Customer customer)
        {
            MessageBox.Show(this, "Select a customer first.", "New Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var project = CreateProject(customer);
        if (project is null)
        {
            return;
        }
        StateChanged = true;
        RefreshProjects(customer.Id, project.Id);
    }

    private CustomerProject? CreateProject(Customer customer)
    {
        return CreateProject(customer, DateTime.Today);
    }

    private CustomerProject? CreateProject(Customer customer, DateTime requestedDate)
    {
        while (true)
        {
            var existing = _projects.FirstOrDefault(project =>
                project.CustomerId == customer.Id && project.DropOffDate.Date == requestedDate.Date);
            if (existing is null)
            {
                var project = new CustomerProject
                {
                    CustomerId = customer.Id,
                    DropOffDate = requestedDate
                };
                _projects.Add(project);
                return project;
            }

            switch (ShowDuplicateDateDialog(customer.FullName, requestedDate))
            {
                case DuplicateDateChoice.UseExisting:
                    return existing;
                case DuplicateDateChoice.ChooseDifferentDate:
                    var selectedDate = PromptForProjectDate(requestedDate);
                    if (!selectedDate.HasValue)
                    {
                        return null;
                    }
                    requestedDate = selectedDate.Value.Date;
                    break;
                default:
                    return null;
            }
        }
    }

    private void CreateProjectFromDatePicker(Customer customer)
    {
        DateTime? selectedDate = PromptForProjectDate(DateTime.Today);
        if (!selectedDate.HasValue)
        {
            Close();
            return;
        }

        CustomerProject? project = CreateProject(customer, selectedDate.Value.Date);
        if (project is null)
        {
            Close();
            return;
        }

        StateChanged = true;
        SelectedCustomer = customer;
        SelectedProject = project;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static DuplicateDateChoice ShowDuplicateDateDialog(string customerName, DateTime date)
    {
        using var dialog = new Form
        {
            Text = "Project Already Exists",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(490, 160),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(34, 36, 39),
            ForeColor = Color.WhiteSmoke
        };
        var message = new Label
        {
            Text = $"{customerName} already has a project dated {date:MM-dd-yyyy}.\n\nChoose the existing project or select a different drop-off date.",
            Location = new Point(16, 16),
            Size = new Size(458, 64),
            ForeColor = Color.WhiteSmoke
        };
        var useExisting = new Button { Text = "Use Existing Project", Location = new Point(16, 108), Size = new Size(142, 30) };
        var chooseDate = new Button { Text = "Choose Different Date", Location = new Point(166, 108), Size = new Size(150, 30) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(398, 108), Size = new Size(76, 30) };
        useExisting.Click += (_, _) => dialog.DialogResult = DialogResult.Yes;
        chooseDate.Click += (_, _) => dialog.DialogResult = DialogResult.Retry;
        dialog.Controls.AddRange([message, useExisting, chooseDate, cancel]);
        dialog.CancelButton = cancel;
        return dialog.ShowDialog() switch
        {
            DialogResult.Yes => DuplicateDateChoice.UseExisting,
            DialogResult.Retry => DuplicateDateChoice.ChooseDifferentDate,
            _ => DuplicateDateChoice.Cancel
        };
    }

    private static DateTime? PromptForProjectDate(DateTime initialDate)
    {
        using var dialog = new Form
        {
            Text = "Choose Project Date",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(330, 135),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(34, 36, 39),
            ForeColor = Color.WhiteSmoke
        };
        var label = new Label { Text = "Drop-off date:", Location = new Point(16, 18), AutoSize = true, ForeColor = Color.WhiteSmoke };
        var picker = new DateTimePicker { Value = initialDate, Format = DateTimePickerFormat.Custom, CustomFormat = "MM-dd-yyyy", Location = new Point(16, 44), Width = 150 };
        var select = new Button { Text = "Select", DialogResult = DialogResult.OK, Location = new Point(166, 88), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(247, 88), Width = 75 };
        dialog.Controls.AddRange([label, picker, select, cancel]);
        dialog.AcceptButton = select;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK ? picker.Value.Date : null;
    }

    private enum DuplicateDateChoice
    {
        UseExisting,
        ChooseDifferentDate,
        Cancel
    }

    private void RenameProject()
    {
        if (_projectList.SelectedItem is not CustomerProject project)
        {
            return;
        }

        var name = PromptForText("Rename Project", "Project name:", project.Name ?? project.DisplayName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        project.Name = name.Trim();
        StateChanged = true;
        RefreshProjects(project.CustomerId, project.Id);
    }

    private void CompleteProject()
    {
        if (_projectList.SelectedItem is not CustomerProject project || project.IsCompleted)
        {
            return;
        }

        var customer = _customers.FirstOrDefault(item => item.Id == project.CustomerId);
        var recordings = _historyItems.Where(item => item.ProjectId == project.Id).ToList();
        int needsReview = recordings.Count(item => item.ReviewStatus == CaptureReviewStatus.NeedsReview);
        int missingFile = recordings.Count(item =>
            item.ReviewStatus == CaptureReviewStatus.NeedsReview && !File.Exists(item.OutputPath));
        int complete = recordings.Count(item => item.ReviewStatus is CaptureReviewStatus.CompleteTrimmed
            or CaptureReviewStatus.CompleteNoTrimNeeded);
        int discarded = recordings.Count(item => item.ReviewStatus == CaptureReviewStatus.Discarded);

        var summary =
            $"Customer: {customer?.FullName ?? "Unknown Customer"}\n" +
            $"Project: {project.DisplayName}\n\n" +
            $"Recordings: {recordings.Count}\n" +
            $"Complete: {complete}\n" +
            $"Needs Review: {needsReview}\n" +
            $"Discarded: {discarded}\n" +
            $"Missing File: {missingFile}";

        var warning = needsReview > 0
            ? $"\n\nThis project still has {needsReview} recording(s) marked Needs Review." +
              (missingFile > 0
                  ? $"\n{missingFile} of those recording(s) has a missing file."
                  : string.Empty) +
              "\n\nNeeds Review includes any missing-file recordings shown above. Those records will remain unchanged in Capture History."
            : string.Empty;

        if (MessageBox.Show(
                this,
                summary + warning + "\n\nMark this project complete?",
                "Mark Project Complete",
                MessageBoxButtons.YesNo,
                needsReview > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        project.IsCompleted = true;
        project.CompletedAt = DateTime.Now;
        StateChanged = true;
        _projectStateChanged?.Invoke();
        RefreshProjects(project.CustomerId, project.Id);
    }

    private void ReopenProject()
    {
        if (_projectList.SelectedItem is not CustomerProject project || !project.IsCompleted)
        {
            return;
        }

        if (MessageBox.Show(this, "Reopen this completed project?", "Reopen Project", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        project.IsCompleted = false;
        StateChanged = true;
        _projectStateChanged?.Invoke();
        RefreshProjects(project.CustomerId, project.Id);
    }

    private void ResumeProject()
    {
        if (_customerList.SelectedItem is not Customer customer || _projectList.SelectedItem is not CustomerProject project)
        {
            return;
        }

        if (project.IsCompleted)
        {
            MessageBox.Show(this, "Completed projects must be deliberately reopened before capture can resume.", "Project Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedCustomer = customer;
        SelectedProject = project;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string? PromptForText(string title, string label, string initialValue)
    {
        using var prompt = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(400, 130),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(34, 36, 39),
            ForeColor = Color.WhiteSmoke
        };
        var caption = new Label { Text = label, Location = new Point(16, 14), AutoSize = true, ForeColor = Color.WhiteSmoke };
        var text = new TextBox { Text = initialValue, Location = new Point(16, 38), Width = 368 };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(228, 82), Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(309, 82), Width = 75 };
        prompt.Controls.AddRange([caption, text, ok, cancel]);
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancel;
        return prompt.ShowDialog() == DialogResult.OK ? text.Text : null;
    }
}
