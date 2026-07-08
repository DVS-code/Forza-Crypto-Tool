using System.Diagnostics;
using System.Reflection;

namespace ForzaCryptoTool;

internal sealed class AboutForm : Form
{
    public AboutForm()
    {
        BuildUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    private void BuildUi()
    {
        Text = $"About {BuildConfig.AppName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(440, 320);
        BackColor = Theme.Bg;
        Font = Theme.Body;

        var logo = LoadLogo();
        if (logo is not null)
            Controls.Add(new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64),
                Location = new Point(Theme.Sp3, Theme.Sp3), BackColor = Color.Transparent,
            });

        Controls.Add(new Label
        {
            Text = BuildConfig.AppName, Font = Theme.Title, ForeColor = Theme.TextMain, AutoSize = true,
            Location = new Point(Theme.Sp3 + 80, Theme.Sp3 + 4), BackColor = Color.Transparent,
        });
        Controls.Add(new Label
        {
            Text = $"Version {BuildConfig.VersionLabel}", Font = Theme.Body, ForeColor = Theme.TextMuted, AutoSize = true,
            Location = new Point(Theme.Sp3 + 80, Theme.Sp3 + 32), BackColor = Color.Transparent,
        });

        Controls.Add(new Label
        {
            Location = new Point(Theme.Sp3, 96), Size = new Size(400, 40), Font = Theme.Body, ForeColor = Theme.TextMain,
            BackColor = Color.Transparent,
            Text = "Decrypt, edit, and re-encrypt Forza Horizon 6 files —\nGameDB, profiles, and Method 22 assets.",
        });

        var divider = new Panel { Location = new Point(Theme.Sp3, 150), Size = new Size(392, 1), BackColor = Theme.Border };
        Controls.Add(divider);

        Controls.Add(new Label
        {
            Text = "CREDITS", Font = new Font("Segoe UI Semibold", 8.5f), ForeColor = Theme.TextMuted, AutoSize = true,
            Location = new Point(Theme.Sp3, 166), BackColor = Color.Transparent,
        });
        Controls.Add(MakeCredit("DVS", "Crypto Tool Dev", 190));
        Controls.Add(MakeLink("youtube.com/@dvssquad", "https://www.youtube.com/@dvssquad", 212));
        Controls.Add(MakeCredit("Ariza", "Save Swap Help", 240));
        Controls.Add(MakeCredit("xxd20xxx", "Crypto Work", 264));
        Controls.Add(MakeLink("ko-fi.com/grayenjoyer50", "https://ko-fi.com/grayenjoyer50", 286));
    }

    private static LinkLabel MakeLink(string text, string url, int y)
    {
        var link = new LinkLabel
        {
            Text = text, Font = Theme.Small, AutoSize = true,
            LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline, BackColor = Color.Transparent,
            Location = new Point(Theme.Sp3 + 110, y),
        };
        link.Links.Add(0, link.Text.Length, url);
        link.LinkClicked += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo((string)e.Link!.LinkData!) { UseShellExecute = true }); }
            catch {  }
        };
        return link;
    }

    private static Control MakeCredit(string name, string role, int y)
    {
        var panel = new Panel { Location = new Point(Theme.Sp3, y), Size = new Size(422, 22), BackColor = Color.Transparent };
        panel.Controls.Add(new Label
        {
            Text = name, Font = Theme.BodyStrong, ForeColor = Theme.Accent, AutoSize = true,
            Location = new Point(0, 0), BackColor = Color.Transparent,
        });
        panel.Controls.Add(new Label
        {
            Text = "— " + role, Font = Theme.Body, ForeColor = Theme.TextMain, AutoSize = true,
            Location = new Point(110, 0), BackColor = Color.Transparent,
        });
        return panel;
    }

    private static Image? LoadLogo()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ForzaCryptoTool.Assets.logo.png");
            return stream is null ? null : Image.FromStream(stream);
        }
        catch { return null; }
    }
}
