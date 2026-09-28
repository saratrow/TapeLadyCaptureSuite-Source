using TapeLadyCaptureSuite.Models;

namespace TapeLadyCaptureSuite;

internal sealed class RecordingDetailsForm : Form
{
    private readonly List<Customer> _customers;
    private readonly List<CustomerProject> _projects;
    private readonly ComboBox _customerCombo = new();
    private readonly ComboBox _projectCombo = new();
    private readonly TextBox _titleText = new();

    public Customer? Customer => _customerCombo.SelectedItem as Customer;
    public CustomerProject? Project => _projectCombo.SelectedItem as CustomerProject;
    public string TapeTitle => _titleText.Text;

    public RecordingDetailsForm(
        List<Customer> customers,
        List<CustomerProject> projects,
        CaptureHistoryItem recording)
    {
        _customers = customers;
        _projects = projects;

        Text = "Edit Recording Details";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(34, 36, 39);
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9F);

        BuildInterface();
        PopulateCustomers(recording.CustomerId);
        PopulateProjects((recording.CustomerId ?? Customer?.Id), recording.ProjectId);
        _titleText.Text = recording.TapeLabel;
    }

    private void BuildInterface()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddRow(layout, "Customer", _customerCombo, 0);
        AddRow(layout, "Project", _projectCombo, 1);
        AddRow(layout, "Tape Title", _titleText, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = "Save Details", AutoSize = true };
        save.Click += (_, _) =>
        {
            if (Customer is null || Project is null || string.IsNullOrWhiteSpace(TapeTitle))
            {
                MessageBox.Show(this, "Select a customer, project, and tape title.", "Edit Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        layout.Controls.Add(buttons, 1, 3);
        Controls.Add(layout);

        _customerCombo.SelectedIndexChanged += (_, _) => PopulateProjects(Customer?.Id, null);
    }

    private void AddRow(TableLayoutPanel layout, string label, Control control, int row)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Silver }, 0, row);
        control.Dock = DockStyle.Fill;
        control.BackColor = Color.FromArgb(27, 29, 31);
        control.ForeColor = Color.WhiteSmoke;
        control.Margin = new Padding(0, 6, 0, 6);
        if (control is ComboBox combo)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
        }
        layout.Controls.Add(control, 1, row);
    }

    private void PopulateCustomers(Guid? selectedId)
    {
        _customerCombo.BeginUpdate();
        _customerCombo.Items.Clear();
        foreach (var customer in _customers.OrderBy(customer => customer.FullName))
        {
            _customerCombo.Items.Add(customer);
        }
        _customerCombo.EndUpdate();
        Select<Customer>(_customerCombo, selectedId, customer => customer.Id);
    }

    private void PopulateProjects(Guid? customerId, Guid? selectedId)
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
        Select<CustomerProject>(_projectCombo, selectedId, project => project.Id);
    }

    private static void Select<T>(ComboBox combo, Guid? id, Func<T, Guid> getId)
    {
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
}
