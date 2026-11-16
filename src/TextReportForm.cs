using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Read-only text report dialog (plugin details, load errors).</summary>
    internal sealed class TextReportForm : Form
    {
        public TextReportForm(string title, string subtitle, string body)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = title;
            ClientSize = new Size(Theme.S(720), Theme.S(520));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(Theme.MakeLine(title, Theme.Title, Theme.TextPrimary,
                Theme.S(24), Theme.S(18), Theme.S(660)));
            Controls.Add(Theme.MakeLine(subtitle, Theme.Caption, Theme.TextTertiary,
                Theme.S(24), Theme.S(48), Theme.S(660)));

            MCard card = new MCard();
            card.Padding = new Padding(1);
            card.SetBounds(Theme.S(24), Theme.S(78), ClientSize.Width - Theme.S(48),
                ClientSize.Height - Theme.S(78) - Theme.S(76));
            Controls.Add(card);

            MTextView box = new MTextView();
            box.Monospace = true;
            box.BackColor = Theme.Surface;
            box.Text = body == null ? "" : body;
            card.Controls.Add(box);
            box.SetBounds(Theme.S(8), Theme.S(8),
                Math.Max(Theme.S(40), card.ClientSize.Width - Theme.S(16)),
                Math.Max(Theme.S(40), card.ClientSize.Height - Theme.S(16)));
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            MButton copy = new MButton();
            copy.Text = "复制";
            copy.Kind = MButtonKind.Secondary;
            copy.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76) - Theme.S(8) - Theme.S(76),
                ClientSize.Height - Theme.S(62), Theme.S(76), Theme.S(34));
            copy.Click += delegate
            {
                try { Clipboard.SetText(box.Text); } catch { }
            };
            Controls.Add(copy);

            MButton close = new MButton();
            close.Text = "关闭";
            close.Kind = MButtonKind.Primary;
            close.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76),
                ClientSize.Height - Theme.S(62), Theme.S(76), Theme.S(34));
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }
    }
}
