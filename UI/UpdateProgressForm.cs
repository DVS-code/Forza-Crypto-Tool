namespace ForzaCryptoTool;

internal sealed class UpdateProgressForm : Form
{
    private readonly ProgressBar _bar;
    private readonly Label _pct;

    public IProgress<int> Progress { get; }

    public UpdateProgressForm(UpdateInfo info)
    {
        Text = "Updating ForzaCryptoTool";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ControlBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(420, 130);
        BackColor = Theme.Bg;
        Font = Theme.Body;

        Controls.Add(new Label
        {
            Text = $"Downloading version {info.Version}…",
            Font = Theme.BodyStrong, ForeColor = Theme.TextMain, AutoSize = true,
            Location = new Point(Theme.Sp3, Theme.Sp3), BackColor = Color.Transparent,
        });

        _bar = new ProgressBar
        {
            Location = new Point(Theme.Sp3, Theme.Sp3 + 34), Size = new Size(372, 18),
            Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0,
        };
        Controls.Add(_bar);

        _pct = new Label
        {
            Text = "0%", Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = true,
            Location = new Point(Theme.Sp3, Theme.Sp3 + 58), BackColor = Color.Transparent,
        };
        Controls.Add(_pct);

        Controls.Add(new Label
        {
            Text = "The app will close and reopen automatically when finished.",
            Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = true,
            Location = new Point(Theme.Sp3, Theme.Sp3 + 78), BackColor = Color.Transparent,
        });

        Progress = new Progress<int>(p =>
        {
            var v = Math.Clamp(p, 0, 100);
            _bar.Value = v;
            _pct.Text = $"{v}%";
        });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }
}
