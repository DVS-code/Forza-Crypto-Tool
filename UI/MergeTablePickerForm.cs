namespace ForzaCryptoTool;

internal sealed class MergeTablePickerForm : Form
{
    private readonly CheckedListBox _list = new();
    private readonly Label _hint = new();

    public List<string>? SelectedTables { get; private set; }

    public bool OverwriteExisting => _overwrite.Checked;
    private readonly CheckBox _overwrite = new();

    public MergeTablePickerForm(IReadOnlyList<GameDbMerge.SharedTableInfo> tables, string donorName)
    {
        Text = "Merge — choose tables";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        ForeColor = Theme.TextMain;
        Font = Theme.Body;
        ClientSize = Theme.ScaleSize(560, 600);

        var title = new Label
        {
            Text = $"Select tables to merge from\n{donorName}",
            Dock = DockStyle.Top,
            Height = Theme.Scaled(56),
            ForeColor = Theme.TextMain,
            Font = Theme.BodyStrong,
            Padding = Theme.ScalePadding(Theme.Sp2, Theme.Sp2, Theme.Sp2, 0),
        };

        _hint.Text = "Only checked tables are merged. Deselect heavy tables (high donor rows) if the merge " +
                     "ends up too large for the GameDB container.";
        _hint.Dock = DockStyle.Top;
        _hint.Height = Theme.Scaled(58);
        _hint.ForeColor = Theme.TextMuted;
        _hint.Padding = Theme.ScalePadding(Theme.Sp2, 0, Theme.Sp2, Theme.Sp1);

        _list.Dock = DockStyle.Fill;
        _list.BackColor = Theme.Panel;
        _list.ForeColor = Theme.TextMain;
        _list.BorderStyle = BorderStyle.None;
        _list.CheckOnClick = true;
        _list.IntegralHeight = false;
        _list.Font = Theme.Mono;
        foreach (var t in tables)
        {

            var label = $"{t.Table}".PadRight(34) + $"donor {t.DonorRows,7:N0}   base {t.BaseRows,7:N0}";
            int idx = _list.Items.Add(label);
            _list.SetItemChecked(idx, true);
        }
        _tableNames = tables.Select(t => t.Table).ToList();

        _overwrite.Text = "Overwrite existing rows with donor values (uncheck = only add new rows)";
        _overwrite.Dock = DockStyle.Top;
        _overwrite.Height = Theme.Scaled(34);
        _overwrite.Checked = true;
        _overwrite.ForeColor = Theme.TextMain;
        _overwrite.Padding = Theme.ScalePadding(Theme.Sp2, 0, 0, 0);
        _overwrite.FlatStyle = FlatStyle.Flat;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = Theme.Scaled(60),
            Padding = Theme.ScalePadding(Theme.Sp2),
            BackColor = Theme.Bg,
        };
        var ok = Theme.MakeButton("Merge", Theme.IconDatabase, primary: true);
        ok.Width = Theme.Scaled(130);
        ok.Click += (_, _) =>
        {
            SelectedTables = _list.CheckedIndices.Cast<int>().Select(i => _tableNames[i]).ToList();
            if (SelectedTables.Count == 0)
            {
                MessageBox.Show(this, "Select at least one table to merge.", "Merge",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = Theme.MakeButton("Cancel");
        cancel.Width = Theme.Scaled(110);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var none = Theme.MakeButton("None");
        none.Width = Theme.Scaled(86);
        none.Click += (_, _) => SetAll(false);
        var all = Theme.MakeButton("All");
        all.Width = Theme.Scaled(86);
        all.Click += (_, _) => SetAll(true);

        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(none);
        buttons.Controls.Add(all);

        Controls.Add(_list);
        Controls.Add(_overwrite);
        Controls.Add(_hint);
        Controls.Add(title);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private readonly List<string> _tableNames;

    private void SetAll(bool check)
    {
        for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, check);
    }
}
