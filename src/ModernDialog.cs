using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal enum MDialogKind
    {
        Info,
        Warning,
        Question,
        Error
    }

    /// <summary>
    /// Themed replacement for MessageBox. Every alert in the app goes through
    /// here so the buttons, icon and typography match the rest of the window
    /// instead of falling back to the system dialog look.
    /// </summary>
    internal static class MDialog
    {
        public static void Tell(IWin32Window owner, string title, string message)
        {
            Tell(owner, title, message, MDialogKind.Info);
        }

        public static void Tell(IWin32Window owner, string title, string message, MDialogKind kind)
        {
            using (MDialogForm form = new MDialogForm(title, message, kind, false, "确定", "取消"))
                form.ShowDialog(owner);
        }

        public static bool Confirm(IWin32Window owner, string title, string message)
        {
            return Confirm(owner, title, message, "确定", "取消", MDialogKind.Question);
        }

        public static bool Confirm(IWin32Window owner, string title, string message,
            string yesText, string noText)
        {
            return Confirm(owner, title, message, yesText, noText, MDialogKind.Question);
        }

        public static bool Confirm(IWin32Window owner, string title, string message,
            string yesText, string noText, MDialogKind kind)
        {
            using (MDialogForm form = new MDialogForm(title, message, kind, true, yesText, noText))
            {
                form.ShowDialog(owner);
                return form.Accepted;
            }
        }
    }

    /// <summary>
    /// Flat alert sheet: icon + message on the window colour, a hairline above
    /// the action row, then the buttons. No inset card, because a card inside a
    /// short fixed-size dialog reads as a misplaced panel rather than a sheet.
    /// </summary>
    internal sealed class MDialogForm : Form
    {
        private readonly string _message;
        private readonly MDialogKind _kind;
        private readonly bool _confirm;
        private readonly string _yesText;
        private readonly string _noText;
        private bool _accepted;
        private string[] _lines = new string[0];
        private int _pad;
        private int _textLeft;
        private int _textTop;
        private int _lineHeight;
        private int _separatorY;
        private Rectangle _iconBounds;

        public bool Accepted { get { return _accepted; } }

        public MDialogForm(string title, string message, MDialogKind kind,
            bool confirm, string yesText, string noText)
        {
            _message = message == null ? string.Empty : message;
            _kind = kind;
            _confirm = confirm;
            _yesText = yesText;
            _noText = noText;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Body;
            KeyPreview = true;
            DoubleBuffered = true;

            BuildLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle, Theme.IsDark);
            Native.SetRoundedCorners(Handle, true);
        }

        private void BuildLayout()
        {
            _pad = Theme.S(24);
            int iconSize = Theme.S(36);
            int gap = Theme.S(16);
            int messageWidth = Theme.S(392);
            int maxMessageHeight = Theme.S(280);

            _lineHeight = MTextLayout.LineHeight(Theme.Body);
            List<string> lines = MTextLayout.Wrap(_message, Theme.Body, messageWidth, true);

            // Clamp very long messages; the plugin details window is the place
            // for long text, alerts stay compact.
            int maxLines = Math.Max(1, maxMessageHeight / _lineHeight);
            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                lines[maxLines - 1] = lines[maxLines - 1] + "…";
            }
            _lines = lines.ToArray();

            int messageHeight = Math.Max(_lineHeight, MTextLayout.Height(lines, Theme.Body));
            int contentHeight = Math.Max(iconSize, messageHeight);
            int width = _pad * 2 + iconSize + gap + messageWidth;
            int buttonHeight = Theme.S(Theme.CtrlHeight);

            // Body, then breathing room, a hairline, and the action row. The
            // line is what separates "message" from "buttons" here, so the
            // dialog no longer needs a tinted card to look designed.
            _separatorY = _pad + contentHeight + Theme.S(20);
            int buttonsY = _separatorY + Theme.S(14);
            int height = buttonsY + buttonHeight + Theme.S(20);

            ClientSize = new Size(width, height);

            _iconBounds = new Rectangle(_pad, _pad + (contentHeight - iconSize) / 2, iconSize, iconSize);
            _textLeft = _pad + iconSize + gap;
            _textTop = _pad + (contentHeight - messageHeight) / 2;

            int buttonWidth = Theme.S(92);
            int right = width - _pad;

            MButton primary = new MButton();
            primary.Kind = MButtonKind.Primary;
            primary.Text = _confirm ? _yesText : "确定";
            primary.SetBounds(right - buttonWidth, buttonsY, buttonWidth, buttonHeight);
            primary.Click += delegate { Finish(true); };
            Controls.Add(primary);

            if (_confirm)
            {
                MButton secondary = new MButton();
                secondary.Kind = MButtonKind.Secondary;
                secondary.Text = string.IsNullOrEmpty(_noText) ? "取消" : _noText;
                secondary.SetBounds(right - buttonWidth * 2 - Theme.S(10), buttonsY, buttonWidth, buttonHeight);
                secondary.Click += delegate { Finish(false); };
                Controls.Add(secondary);
            }

            AcceptButton = null;
            CancelButton = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.Prepare(e.Graphics);
            Graphics g = e.Graphics;

            using (Pen rule = new Pen(Theme.Stroke, 1f))
                g.DrawLine(rule, _pad, _separatorY + 0.5f, Width - _pad, _separatorY + 0.5f);

            Rectangle circle = _iconBounds;

            Color tint;
            Color glyph;
            string symbol;
            switch (_kind)
            {
                case MDialogKind.Warning:
                    tint = Theme.Alpha(Theme.Warning, 32);
                    glyph = Theme.Warning;
                    symbol = "!";
                    break;
                case MDialogKind.Error:
                    tint = Theme.Alpha(Theme.Danger, 30);
                    glyph = Theme.Danger;
                    symbol = "×";
                    break;
                case MDialogKind.Question:
                    tint = Theme.Alpha(Theme.Accent, 26);
                    glyph = Theme.Accent;
                    symbol = "?";
                    break;
                default:
                    tint = Theme.Alpha(Theme.Accent, 26);
                    glyph = Theme.Accent;
                    symbol = "i";
                    break;
            }

            Theme.FillRound(g, new RectangleF(circle.X, circle.Y, circle.Width, circle.Height),
                circle.Width / 2f, tint);
            using (Font bold = new Font(Theme.Body.FontFamily, 14f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, symbol, bold, circle, glyph,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding);
            }

            for (int i = 0; i < _lines.Length; i++)
            {
                TextRenderer.DrawText(g, _lines[i], Theme.Body,
                    new Point(_textLeft, _textTop + i * _lineHeight), Theme.TextPrimary,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                Finish(true);
                e.Handled = true;
            }
        }

        private void Finish(bool accepted)
        {
            _accepted = accepted;
            DialogResult = accepted ? DialogResult.OK : DialogResult.Cancel;
            Close();
        }
    }
}
