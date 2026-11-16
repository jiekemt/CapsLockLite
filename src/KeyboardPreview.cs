using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Which modifier a keycap stands for, if any.</summary>
    internal enum KeyModifier
    {
        None = 0,
        Caps = 1,
        Ctrl = 2,
        Alt = 3,
        Shift = 4,
        Win = 5
    }

    /// <summary>One physical key of the preview keyboard, measured in key units.</summary>
    internal sealed class KeyCap
    {
        public Keys Code;
        public Keys AliasCode;
        public string Label;
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public KeyModifier Modifier;

        public KeyCap(Keys code, string label, float x, float y, float width)
        {
            Code = code;
            AliasCode = Keys.None;
            Label = label;
            X = x;
            Y = y;
            Width = width;
            Height = 1f;
            Modifier = KeyModifier.None;
        }

        public KeyCap(Keys code, string label, float x, float y, float width, float height)
            : this(code, label, x, y, width)
        {
            Height = height;
        }

        /// <summary>True when a mapping bound to <paramref name="key"/> lives here.</summary>
        public bool Covers(Keys key)
        {
            Keys code = key & Keys.KeyCode;
            if (code == Code) return true;
            return AliasCode != Keys.None && code == AliasCode;
        }
    }

    /// <summary>
    /// Complete 104-key US layout in key units (1 = one 1u key). Working in units
    /// lets the view scale the whole board to any pixel size while every key keeps
    /// its exact relative position.
    /// </summary>
    internal static class KeyboardLayout
    {
        public const float TotalWidth = 22.5f;
        public const float TotalHeight = 6f;
        public const float RowHeight = 0.92f;

        public static List<KeyCap> Create()
        {
            List<KeyCap> keys = new List<KeyCap>();

            // ---- row 0: function keys --------------------------------------
            keys.Add(new KeyCap(Keys.Escape, "Esc", 0f, 0f, 1f));
            for (int i = 0; i < 4; i++)
                keys.Add(new KeyCap((Keys)((int)Keys.F1 + i), "F" + (i + 1).ToString(), 2f + i, 0f, 1f));
            for (int i = 0; i < 4; i++)
                keys.Add(new KeyCap((Keys)((int)Keys.F5 + i), "F" + (i + 5).ToString(), 6.5f + i, 0f, 1f));
            for (int i = 0; i < 4; i++)
                keys.Add(new KeyCap((Keys)((int)Keys.F9 + i), "F" + (i + 9).ToString(), 11f + i, 0f, 1f));
            keys.Add(new KeyCap(Keys.Snapshot, "PrtSc", 15.5f, 0f, 1f));
            keys.Add(new KeyCap(Keys.Scroll, "ScrLk", 16.5f, 0f, 1f));
            keys.Add(new KeyCap(Keys.Pause, "Pause", 17.5f, 0f, 1f));

            // ---- row 1: digits and navigation ------------------------------
            Keys[] digits =
            {
                Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6,
                Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.Oemplus
            };
            // The shift symbol sits to the left of the digit, separated by a space:
            // one label string, so the normal centred label pass places the pair.
            string[] digitLabels =
            {
                "~ `", "! 1", "@ 2", "# 3", "$ 4", "% 5", "^ 6",
                "& 7", "* 8", "( 9", ") 0", "_ -", "+ ="
            };
            for (int i = 0; i < digits.Length; i++)
                keys.Add(new KeyCap(digits[i], digitLabels[i], i, 1f, 1f));
            keys.Add(new KeyCap(Keys.Back, "Backspace", 13f, 1f, 2f));
            keys.Add(new KeyCap(Keys.Insert, "Ins", 15.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.Home, "Home", 16.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.PageUp, "Page Up", 17.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.NumLock, "Num Lock", 18.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.Divide, "/", 19.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.Multiply, "*", 20.5f, 1f, 1f));
            keys.Add(new KeyCap(Keys.Subtract, "-", 21.5f, 1f, 1f));

            // ---- row 2: tab row --------------------------------------------
            keys.Add(new KeyCap(Keys.Tab, "Tab", 0f, 2f, 1.5f));
            Keys[] topRow =
            {
                Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P,
                Keys.OemOpenBrackets, Keys.OemCloseBrackets
            };
            string[] topLabels = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]" };
            for (int i = 0; i < topRow.Length; i++)
                keys.Add(new KeyCap(topRow[i], topLabels[i], 1.5f + i, 2f, 1f));

            KeyCap backslash = new KeyCap(Keys.OemPipe, "\\", 13.5f, 2f, 1.5f);
            // ISO keyboards report the same physical key as OemBackslash; accept both.
            backslash.AliasCode = Keys.OemBackslash;
            keys.Add(backslash);
            keys.Add(new KeyCap(Keys.Delete, "Del", 15.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.End, "End", 16.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.PageDown, "Page Down", 17.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.NumPad7, "7", 18.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.NumPad8, "8", 19.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.NumPad9, "9", 20.5f, 2f, 1f));
            keys.Add(new KeyCap(Keys.Add, "+", 21.5f, 2f, 1f, 2f));

            // ---- row 3: home row -------------------------------------------
            KeyCap caps = new KeyCap(Keys.Capital, "Caps Lock", 0f, 3f, 1.75f);
            caps.Modifier = KeyModifier.Caps;
            keys.Add(caps);

            Keys[] homeRow =
            {
                Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L,
                Keys.OemSemicolon, Keys.OemQuotes
            };
            string[] homeLabels = { "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'" };
            for (int i = 0; i < homeRow.Length; i++)
                keys.Add(new KeyCap(homeRow[i], homeLabels[i], 1.75f + i, 3f, 1f));

            keys.Add(new KeyCap(Keys.Return, "Enter", 12.75f, 3f, 2.25f));
            keys.Add(new KeyCap(Keys.NumPad4, "4", 18.5f, 3f, 1f));
            keys.Add(new KeyCap(Keys.NumPad5, "5", 19.5f, 3f, 1f));
            keys.Add(new KeyCap(Keys.NumPad6, "6", 20.5f, 3f, 1f));

            // ---- row 4: shift row ------------------------------------------
            KeyCap leftShift = new KeyCap(Keys.LShiftKey, "Shift", 0f, 4f, 2.25f);
            leftShift.Modifier = KeyModifier.Shift;
            keys.Add(leftShift);

            Keys[] bottomRow =
            {
                Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M,
                Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion
            };
            string[] bottomLabels = { "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/" };
            for (int i = 0; i < bottomRow.Length; i++)
                keys.Add(new KeyCap(bottomRow[i], bottomLabels[i], 2.25f + i, 4f, 1f));

            KeyCap rightShift = new KeyCap(Keys.RShiftKey, "Shift", 12.25f, 4f, 2.75f);
            rightShift.Modifier = KeyModifier.Shift;
            keys.Add(rightShift);

            keys.Add(new KeyCap(Keys.Up, "↑", 16.5f, 4f, 1f));
            keys.Add(new KeyCap(Keys.NumPad1, "1", 18.5f, 4f, 1f));
            keys.Add(new KeyCap(Keys.NumPad2, "2", 19.5f, 4f, 1f));
            keys.Add(new KeyCap(Keys.NumPad3, "3", 20.5f, 4f, 1f));
            keys.Add(new KeyCap(Keys.Return, "Enter", 21.5f, 4f, 1f, 2f));

            // ---- row 5: bottom row -----------------------------------------
            KeyCap leftCtrl = new KeyCap(Keys.LControlKey, "Ctrl", 0f, 5f, 1.25f);
            leftCtrl.Modifier = KeyModifier.Ctrl;
            keys.Add(leftCtrl);

            KeyCap leftWin = new KeyCap(Keys.LWin, "Win", 1.25f, 5f, 1.25f);
            leftWin.Modifier = KeyModifier.Win;
            keys.Add(leftWin);

            KeyCap leftAlt = new KeyCap(Keys.LMenu, "Alt", 2.5f, 5f, 1.25f);
            leftAlt.Modifier = KeyModifier.Alt;
            keys.Add(leftAlt);

            keys.Add(new KeyCap(Keys.Space, "", 3.75f, 5f, 6.25f));

            KeyCap rightAlt = new KeyCap(Keys.RMenu, "Alt", 10f, 5f, 1.25f);
            rightAlt.Modifier = KeyModifier.Alt;
            keys.Add(rightAlt);

            KeyCap rightWin = new KeyCap(Keys.RWin, "Win", 11.25f, 5f, 1.25f);
            rightWin.Modifier = KeyModifier.Win;
            keys.Add(rightWin);

            keys.Add(new KeyCap(Keys.Apps, "Menu", 12.5f, 5f, 1.25f));

            KeyCap rightCtrl = new KeyCap(Keys.RControlKey, "Ctrl", 13.75f, 5f, 1.25f);
            rightCtrl.Modifier = KeyModifier.Ctrl;
            keys.Add(rightCtrl);

            keys.Add(new KeyCap(Keys.Left, "←", 15.5f, 5f, 1f));
            keys.Add(new KeyCap(Keys.Down, "↓", 16.5f, 5f, 1f));
            keys.Add(new KeyCap(Keys.Right, "→", 17.5f, 5f, 1f));
            keys.Add(new KeyCap(Keys.NumPad0, "0", 18.5f, 5f, 2f));
            keys.Add(new KeyCap(Keys.Decimal, ".", 20.5f, 5f, 1f));

            return keys;
        }
    }

    /// <summary>
    /// Decides which mappings the modifier selection on the board stands for. The
    /// selection is one chord, not a prefix: a key is marked only when the selected
    /// modifiers are exactly the ones the shortcut needs. Selecting Caps Lock alone
    /// therefore leaves every Caps + Alt shortcut unmarked, so a key never carries
    /// more than one action name. Nothing selected means nothing marked.
    /// </summary>
    internal static class KeyboardFilter
    {
        public static bool HasSelection(bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            return caps || ctrl || alt || shift || win;
        }

        /// <summary>
        /// True only for the exact chord; false for every mapping while no modifier
        /// is selected. The board uses this so the neutral board marks no key.
        /// </summary>
        public static bool Exact(KeyMapping mapping, bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            if (mapping == null) return false;
            if (!HasSelection(caps, ctrl, alt, shift, win)) return false;
            return mapping.Caps == caps
                && mapping.Ctrl == ctrl
                && mapping.Alt == alt
                && mapping.Shift == shift
                && mapping.Win == win;
        }

        /// <summary>
        /// List filter: an empty selection keeps the whole list, otherwise only the
        /// exact chord. Keeping "no selection = everything" here is what lets the
        /// list stay a complete overview while the board above it stays neutral.
        /// </summary>
        public static bool Matches(KeyMapping mapping, bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            if (mapping == null) return false;
            if (!HasSelection(caps, ctrl, alt, shift, win)) return true;
            return Exact(mapping, caps, ctrl, alt, shift, win);
        }
    }

    /// <summary>How a key label is rendered at the current key size.</summary>
    internal enum KeyLabelKind
    {
        Full = 0,
        Small = 1,
        Tiny = 2,
        TwoLines = 3,
        Truncated = 4
    }

    internal sealed class KeyLabelPlan
    {
        public KeyLabelKind Kind;
        public Font FirstFont;
        public Font SecondFont;
        public string First;
        public string Second;
    }

    /// <summary>
    /// Key labels are drawn in the largest size that actually fits the cap. Sizes
    /// are in pixels and derived from the key size, because layout units are
    /// pixels here and point-sized fonts would not follow the window (and would
    /// be 25% too large on a 120 dpi display). The same rule is used by the
    /// control and by the label audit in the self-test.
    /// </summary>
    internal static class KeyLabel
    {
        public const float LabelRatio = 0.36f;
        public const float SmallRatio = 0.30f;
        public const float TinyRatio = 0.24f;

        public static Font CreatePixelFont(float pixels)
        {
            if (pixels < 5f) pixels = 5f;
            return new Font(Theme.Body.FontFamily, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        public static void CreateFonts(float unit, out Font label, out Font small, out Font tiny)
        {
            label = CreatePixelFont(unit * LabelRatio);
            small = CreatePixelFont(unit * SmallRatio);
            tiny = CreatePixelFont(unit * TinyRatio);
        }

        public static KeyLabelPlan Plan(KeyCap cap, int width, float faceHeight,
            Font label, Font small, Font tiny)
        {
            KeyLabelPlan plan = new KeyLabelPlan();
            plan.First = cap.Label;

            if (Theme.Measure(cap.Label, label).Width <= width)
            {
                plan.Kind = KeyLabelKind.Full;
                plan.FirstFont = label;
                return plan;
            }
            if (Theme.Measure(cap.Label, small).Width <= width)
            {
                plan.Kind = KeyLabelKind.Small;
                plan.FirstFont = small;
                return plan;
            }
            if (Theme.Measure(cap.Label, tiny).Width <= width)
            {
                plan.Kind = KeyLabelKind.Tiny;
                plan.FirstFont = tiny;
                return plan;
            }

            int space = cap.Label.IndexOf(' ');
            if (space > 0 && faceHeight >= 16f)
            {
                plan.Kind = KeyLabelKind.TwoLines;
                plan.First = cap.Label.Substring(0, space);
                plan.Second = cap.Label.Substring(space + 1);
                plan.FirstFont = tiny;
                plan.SecondFont = tiny;
                return plan;
            }

            plan.Kind = KeyLabelKind.Truncated;
            plan.FirstFont = tiny;
            return plan;
        }
    }

    /// <summary>
    /// Layout of one cap that also shows its action name: the label rows chosen by
    /// the label pass plus one action row underneath. Everything is derived from
    /// the cap's own face, so letter keys, wide keys and the double-height numpad
    /// keys are fitted the same way.
    /// </summary>
    internal sealed class KeyActionLayout
    {
        public string ActionText;
        public Font ActionFont;
        public int ActionHeight;
        public string[] RowText;
        public Font[] RowFont;
        public int[] RowHeight;

        public bool HasAction
        {
            get { return ActionFont != null && !string.IsNullOrEmpty(ActionText); }
        }

        /// <summary>Height the label rows and the action row need together.</summary>
        public int Height
        {
            get
            {
                if (!HasAction) return 0;
                int total = ActionHeight;
                if (RowHeight != null)
                    for (int i = 0; i < RowHeight.Length; i++) total += RowHeight[i];
                return total;
            }
        }
    }

    /// <summary>
    /// Fits an action name into a key cap. Every key gets a plan, not only the keys
    /// that happen to carry a shortcut today, and the same routine backs the
    /// self-test audit. A name that does not fit is shortened with an ellipsis
    /// rather than dropped, unless even the smallest legible face cannot fit.
    /// </summary>
    internal static class KeyActionText
    {
        /// <summary>Action text smaller than this is unreadable at 100% DPI.</summary>
        public const float MinEm = 6f;

        /// <summary>Action text stays well below the size of the key label.</summary>
        public const float LabelRatio = 0.20f;

        /// <summary>A cap without a label (Space) may use more of its face.</summary>
        public const float EmptyLabelRatio = 0.30f;

        private const int TextInset = 3;
        private const int MinLine = 8;

        /// <summary>Width an action name may use on a cap of <paramref name="capWidth"/>.</summary>
        public static int TextWidth(int capWidth)
        {
            return Math.Max(1, capWidth - TextInset * 2);
        }

        /// <summary>
        /// Candidate faces from 0.30u down to 0.11u, never below <see cref="MinEm"/>.
        /// Ordered large to small; the board and the audit share the ladder.
        /// </summary>
        public static void BuildLadder(float unit, out Font[] fonts, out int[] lines)
        {
            List<Font> built = new List<Font>();
            List<int> heights = new List<int>();
            for (float ratio = EmptyLabelRatio; ratio >= 0.11f - 0.0001f; ratio -= 0.01f)
            {
                float em = unit * ratio;
                if (em < MinEm - 0.001f) break;
                Font font = KeyLabel.CreatePixelFont(em);
                built.Add(font);
                heights.Add(LineHeight(font));
            }
            fonts = built.ToArray();
            lines = heights.ToArray();
        }

        /// <summary>Height of one rendered line, including the leading GDI adds.</summary>
        public static int LineHeight(Font font)
        {
            return Math.Max(1, TextRenderer.MeasureText("国Ag", font).Height);
        }

        public static KeyActionLayout Layout(KeyCap cap, int width, float faceHeight, float unit,
            Font label, Font small, Font tiny, Font[] actionFonts, int[] actionLines, string action)
        {
            KeyActionLayout layout = new KeyActionLayout();
            KeyLabelPlan plan = KeyLabel.Plan(cap, width, faceHeight, label, small, tiny);

            string[] allText;
            Font[] allFont;
            if (string.IsNullOrEmpty(cap.Label))
            {
                // Space and other blank caps carry no label row at all.
                allText = new string[0];
                allFont = new Font[0];
            }
            else if (plan.Kind == KeyLabelKind.TwoLines)
            {
                allText = new string[] { plan.First, plan.Second };
                allFont = new Font[] { plan.FirstFont, plan.SecondFont };
            }
            else if (plan.Kind == KeyLabelKind.TwoLines)
            {
                allText = new string[] { plan.First, plan.Second };
                allFont = new Font[] { plan.FirstFont, plan.SecondFont };
            }
            else
            {
                allText = new string[] { plan.First };
                allFont = new Font[] { plan.FirstFont };
            }

            if (string.IsNullOrEmpty(action) || actionFonts == null || actionFonts.Length == 0)
                return layout;
            if (faceHeight < MinLine + 2f) return layout;

            float padY = Math.Max(1.5f, faceHeight * 0.05f);
            int available = (int)Math.Floor(faceHeight - padY * 2f);
            int textWidth = TextWidth(width);
            float maxEm = unit * (string.IsNullOrEmpty(cap.Label) ? EmptyLabelRatio : LabelRatio);

            // The label keeps all of its rows; only when they leave no room for the
            // action name does the fallback below shrink a split label.
            int rowCount = allText.Length;
            int labelHeight = 0;
            for (int i = 0; i < rowCount; i++)
                labelHeight += LineHeight(allFont[i]);
            // No extra gap: the line boxes already carry leading, and on a 1u
            // cap one spare pixel decides whether the name can be shown.
            int budget = available - labelHeight;

            Font font;
            string fitted;
            int lineHeight;
            if (budget >= MinLine
                && Fit(actionFonts, actionLines, maxEm, textWidth, budget, action,
                    out font, out fitted, out lineHeight))
            {
                layout.RowText = new string[rowCount];
                layout.RowFont = new Font[rowCount];
                layout.RowHeight = new int[rowCount];
                for (int i = 0; i < rowCount; i++)
                {
                    Font rowFont = allFont[i];
                    // Rows are drawn in a rectangle of the same width the label
                    // plan measured, so the label may keep its 1 px spill.
                    string rowText = Ellipsize(allText[i], rowFont, width);
                    layout.RowText[i] = rowText ?? allText[i];
                    layout.RowFont[i] = rowFont;
                    layout.RowHeight[i] = LineHeight(rowFont);
                }
                layout.ActionText = fitted;
                layout.ActionFont = font;
                layout.ActionHeight = lineHeight;
                return layout;
            }

            // "Page Up" is split over two rows on a 1u cap, which leaves no height
            // for the action. Use a slightly smaller label face instead: first try
            // the whole name on one row, then keep the split but shrink it. The
            // action name is what this page exists to show.
            if (plan.Kind == KeyLabelKind.TwoLines && !string.IsNullOrEmpty(cap.Label))
            {
                int space = cap.Label.IndexOf(' ');
                for (int i = 0; i < actionFonts.Length; i++)
                {
                    Font candidate = actionFonts[i];
                    if (candidate.Size > tiny.Size + 0.01f) continue;
                    if (candidate.Size < MinEm - 0.01f) continue;
                    int candidateLine = LineHeight(candidate);

                    if (Theme.Measure(cap.Label, candidate).Width <= width - 2)
                    {
                        int candidateBudget = available - candidateLine;
                        if (candidateBudget >= MinLine)
                        {
                            Font wholeFont;
                            string wholeText;
                            int wholeLine;
                            if (Fit(actionFonts, actionLines, maxEm, textWidth, candidateBudget,
                                action, out wholeFont, out wholeText, out wholeLine))
                            {
                                layout.RowText = new string[] { cap.Label };
                                layout.RowFont = new Font[] { candidate };
                                layout.RowHeight = new int[] { candidateLine };
                                layout.ActionText = wholeText;
                                layout.ActionFont = wholeFont;
                                layout.ActionHeight = wholeLine;
                                return layout;
                            }
                        }
                    }

                    if (space <= 0) continue;
                    string firstPart = cap.Label.Substring(0, space);
                    string secondPart = cap.Label.Substring(space + 1);
                    if (Theme.Measure(firstPart, candidate).Width > width - 2) continue;
                    if (Theme.Measure(secondPart, candidate).Width > width - 2) continue;
                    int twoRowHeight = candidateLine * 2;
                    int twoRowBudget = available - twoRowHeight;
                    if (twoRowBudget < MinLine) continue;

                    Font splitFont;
                    string splitText;
                    int splitLine;
                    if (!Fit(actionFonts, actionLines, maxEm, textWidth, twoRowBudget, action,
                        out splitFont, out splitText, out splitLine)) continue;

                    layout.RowText = new string[] { firstPart, secondPart };
                    layout.RowFont = new Font[] { candidate, candidate };
                    layout.RowHeight = new int[] { candidateLine, candidateLine };
                    layout.ActionText = splitText;
                    layout.ActionFont = splitFont;
                    layout.ActionHeight = splitLine;
                    return layout;
                }
            }
            return layout;
        }

        /// <summary>
        /// Largest face whose line fits <paramref name="budget"/> and whose text fits
        /// the width. When no face fits the whole name, the smallest face that fits
        /// the height is used with an ellipsis instead.
        /// </summary>
        private static bool Fit(Font[] fonts, int[] lines, float maxEm, int width, int budget,
            string text, out Font font, out string fitted, out int lineHeight)
        {
            font = null;
            fitted = null;
            lineHeight = 0;
            int smallest = -1;
            for (int i = 0; i < fonts.Length; i++)
            {
                if (fonts[i].Size > maxEm + 0.01f) continue;
                if (lines[i] > budget) continue;
                smallest = i;
                if (Theme.Measure(text, fonts[i]).Width <= width)
                {
                    font = fonts[i];
                    fitted = text;
                    lineHeight = lines[i];
                    return true;
                }
            }
            if (smallest < 0) return false;
            string shortened = Ellipsize(text, fonts[smallest], width);
            if (string.IsNullOrEmpty(shortened)) return false;
            font = fonts[smallest];
            fitted = shortened;
            lineHeight = lines[smallest];
            return true;
        }

        /// <summary>
        /// Shortens the text to the widest prefix that still leaves room for "…".
        /// GDI's own ellipsis is not used: on centred text it cut whole glyphs at
        /// the cap border instead of ending the word, so this is measured.
        /// </summary>
        public static string Ellipsize(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (Theme.Measure(text, font).Width <= width) return text;
            const string mark = "…";
            int markWidth = Theme.Measure(mark, font).Width;
            if (markWidth > width) return null;
            int low = 0;
            int high = text.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Theme.Measure(text.Substring(0, mid), font).Width + markWidth <= width) low = mid;
                else high = mid - 1;
            }
            if (low <= 0) return null;
            return text.Substring(0, low).TrimEnd() + mark;
        }
    }

    /// <summary>
    /// Checks every key label against the space available for a given page width.
    /// Used by the self-test so a too-small window cannot silently start cutting
    /// key names again.
    /// </summary>
    internal static class KeyboardLabelAudit
    {
        public static List<string> Truncated(int pageWidth)
        {
            List<string> problems = new List<string>();
            float unit = Math.Max(4f, pageWidth - Theme.S(2) * 2) / KeyboardLayout.TotalWidth;
            float gap = Math.Max(1f, unit * 0.03f);

            Font label = null;
            Font small = null;
            Font tiny = null;
            KeyLabel.CreateFonts(unit, out label, out small, out tiny);
            try
            {
                List<KeyCap> caps = KeyboardLayout.Create();
                for (int i = 0; i < caps.Count; i++)
                {
                    KeyCap cap = caps[i];
                    if (string.IsNullOrEmpty(cap.Label)) continue;

                    // Every cap is measured with its own width: 2u keys (Backspace,
                    // Enter) and 1.25/1.75u keys have much more room than a letter.
                    int width = (int)(cap.Width * unit - gap * 2f) + 2;
                    float faceHeight = cap.Height * unit - gap * 2f;
                    float depth = Math.Min(faceHeight * 0.10f, Math.Max(1.5f, unit * 0.06f));
                    float topHeight = faceHeight - depth;

                    KeyLabelPlan plan = KeyLabel.Plan(cap, width, topHeight, label, small, tiny);
                    bool bad = plan.Kind == KeyLabelKind.Truncated;
                    if (!bad && plan.Kind == KeyLabelKind.TwoLines)
                    {
                        if (Theme.Measure(plan.First, plan.FirstFont).Width > width) bad = true;
                        if (Theme.Measure(plan.Second, plan.SecondFont).Width > width) bad = true;
                    }
                    if (bad) problems.Add(cap.Label);
                }
            }
            finally
            {
                label.Dispose();
                small.Dispose();
                tiny.Dispose();
            }
            return problems;
        }

        /// <summary>
        /// Runs the action-name fit for every cap of the board at one page width.
        /// The name must be drawn (full or ellipsised) inside the cap whenever a
        /// legible face fits; with <paramref name="require"/> the caller asserts
        /// that every single key, including wide and double-height ones, can show
        /// it. Nothing here needs a window, so the sweep is cheap.
        /// </summary>
        public static List<string> ActionProblems(int pageWidth, string action, bool require)
        {
            List<string> problems = new List<string>();
            float unit = Math.Max(4f, pageWidth - Theme.S(2) * 2) / KeyboardLayout.TotalWidth;
            float gap = Math.Max(1f, unit * 0.03f);

            Font label = null;
            Font small = null;
            Font tiny = null;
            Font[] actionFonts = null;
            int[] actionLines = null;
            KeyLabel.CreateFonts(unit, out label, out small, out tiny);
            KeyActionText.BuildLadder(unit, out actionFonts, out actionLines);
            try
            {
                List<KeyCap> caps = KeyboardLayout.Create();
                for (int i = 0; i < caps.Count; i++)
                {
                    KeyCap cap = caps[i];
                    string name = string.IsNullOrEmpty(cap.Label) ? "Space" : cap.Label;
                    int width = (int)(cap.Width * unit - gap * 2f) + 2;
                    float faceHeight = cap.Height * unit - gap * 2f;
                    float depth = Math.Min(faceHeight * 0.10f, Math.Max(1.5f, unit * 0.06f));
                    float topHeight = faceHeight - depth;

                    KeyActionLayout layout = KeyActionText.Layout(cap, width, topHeight, unit,
                        label, small, tiny, actionFonts, actionLines, action);
                    if (!layout.HasAction)
                    {
                        if (require) problems.Add(name + ":missing");
                        continue;
                    }
                    if (layout.Height > (int)Math.Floor(topHeight))
                        problems.Add(name + ":tall");
                    if (layout.ActionFont.Size < KeyActionText.MinEm - 0.01f)
                        problems.Add(name + ":tiny");
                    if (Theme.Measure(layout.ActionText, layout.ActionFont).Width
                        > KeyActionText.TextWidth(width))
                        problems.Add(name + ":wide");
                    for (int r = 0; r < layout.RowText.Length; r++)
                    {
                        if (Theme.Measure(layout.RowText[r], layout.RowFont[r]).Width > width)
                            problems.Add(name + ":label");
                    }
                }
            }
            finally
            {
                label.Dispose();
                small.Dispose();
                tiny.Dispose();
                if (actionFonts != null)
                    for (int i = 0; i < actionFonts.Length; i++) actionFonts[i].Dispose();
            }
            return problems;
        }
    }

    /// <summary>
    /// Section header of the keyboard: title, current filter and the
    /// expand/collapse chevron. The whole strip is the click target, and the
    /// chevron is rotated from the animation progress so it turns while the
    /// board folds away.
    /// </summary>
    internal sealed class MKeyboardHeader : Control
    {
        private const string TitleText = "键盘";

        private bool _hover;
        private float _chevron = 1f;
        private string _summary = "";

        public event EventHandler Toggled;

        public MKeyboardHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = Theme.S(28);
        }

        /// <summary>Filter + count line, e.g. "Caps Lock + Alt · 20 条".</summary>
        public string Summary
        {
            get { return _summary; }
            set
            {
                _summary = value == null ? "" : value;
                Invalidate();
            }
        }

        /// <summary>1 = expanded (chevron up), 0 = collapsed (chevron down).</summary>
        public float ChevronProgress
        {
            get { return _chevron; }
            set { _chevron = value; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            if (_hover)
            {
                Theme.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
                    Theme.SF(Theme.RadiusControl), Theme.SurfaceHover);
            }

            TextRenderer.DrawText(g, TitleText, Theme.BodyStrong,
                new Rectangle(Theme.S(2), 0, Theme.S(60), Height), Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

            int titleWidth = Theme.Measure(TitleText, Theme.BodyStrong).Width;
            if (_summary.Length > 0)
            {
                int left = Theme.S(2) + titleWidth + Theme.S(10);
                int width = Math.Max(1, Width - left - Theme.S(28));
                TextRenderer.DrawText(g, _summary, Theme.Caption,
                    new Rectangle(left, 0, width, Height), Theme.TextTertiary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            DrawChevron(g);
        }

        private void DrawChevron(Graphics g)
        {
            float cx = Width - Theme.S(14);
            float cy = Height / 2f;
            float half = Theme.SF(4f);
            float rise = Theme.SF(2.4f);

            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(180f * _chevron);
            using (Pen pen = new Pen(_hover ? Theme.TextPrimary : Theme.TextSecondary, Theme.SF(1.5f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, -half, -rise, 0f, rise);
                g.DrawLine(pen, 0f, rise, half, -rise);
            }
            g.Restore(state);
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            EventHandler handler = Toggled;
            if (handler != null) handler(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                EventHandler handler = Toggled;
                if (handler != null) handler(this, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>
    /// Owner-drawn keyboard. Keys with a mapping in the current filter are filled
    /// with the accent colour; the five modifier keys are clickable toggles and
    /// show a diagonal mask when active, so "this key has shortcuts" and "this
    /// modifier is part of the filter" never look the same. Nothing is drawn in a
    /// child window, so the whole board is one paint pass.
    /// </summary>
    internal sealed class MKeyboardView : Control
    {
        private const float KeyGapUnit = 0.03f;

        private readonly List<KeyCap> _caps = KeyboardLayout.Create();
        private readonly List<KeyMapping> _mappings = new List<KeyMapping>();
        private readonly int[] _capCounts;      // exact matches under the selected chord
        private readonly int[] _capAllCounts;   // every mapping on the key, any chord
        private readonly int[] _capEnabled;     // exact matches that are enabled
        private readonly string[] _capActions;
        private readonly bool[] _capDisabled;   // exact matches exist, none enabled
        private readonly KeyActionLayout[] _capLayouts;
        private readonly RectangleF[] _rects;
        private Font _labelFont;
        private Font _subFont;
        private Font _tinyFont;
        private Font _countFont;
        private Font[] _actionFonts;
        private int[] _actionLines;
        private float _fontUnit = -1f;
        private float _layoutUnit = -1f;
        private bool _layoutsDirty = true;

        private float _unit = 1f;
        private float _originX;
        private float _originY;
        private bool _capsFilter;
        private bool _ctrlFilter;
        private bool _altFilter;
        private bool _shiftFilter;
        private bool _winFilter;
        private int _hover = -1;
        private int _selected = -1;
        private int _lastClickIndex = -1;
        private int _lastClickTick;

        // A double-click is a fast gesture: the Windows window is far too generous
        // for a cap that opens an editor, so activation only accepts a pair this
        // quick. The click toggle uses the same window.
        private const int QuickDoubleClickMs = 300;

        // Dragging a used main key inside a chord moves its shortcuts onto the
        // key it is dropped on. The drag only arms on a key that actually carries
        // shortcuts, so an empty key still behaves exactly like a plain click.
        private const int DragThreshold = 4;
        private bool _dragArmed;
        private bool _dragActive;
        private int _dragFrom = -1;
        private int _dragTo = -1;
        private Point _dragStart;
        private PointF _dragPoint;
        private PointF _dragRender;
        // Only the band the ghost covered last frame is repainted, so the rest of
        // the board never re-renders while the pointer moves.
        private Rectangle _dragDirty;
        private bool _dragDirtyValid;
        private PointF _dragGrab;

        public event EventHandler SelectionChanged;
        public event EventHandler KeyActivated;
        public event EventHandler KeyContextRequested;
        public event Action<KeyModifier> ModifierToggleRequested;
        /// <summary>Raised when a main key is dropped on another main key.</summary>
        public event Action<Keys, Keys> KeyDragDropped;

        /// <summary>True while a key is following the pointer.</summary>
        internal bool IsDragging { get { return _dragActive; } }

        public MKeyboardView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Opaque |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            // The board is pointer-driven: it no longer takes keyboard focus, so a
            // stray keystroke cannot be swallowed by a key cap.
            TabStop = false;
            Cursor = Cursors.Default;

            _capCounts = new int[_caps.Count];
            _capAllCounts = new int[_caps.Count];
            _capEnabled = new int[_caps.Count];
            _capActions = new string[_caps.Count];
            _capDisabled = new bool[_caps.Count];
            _capLayouts = new KeyActionLayout[_caps.Count];
            _rects = new RectangleF[_caps.Count];
        }

        /// <summary>Read-only view of the layout, used by the self-test.</summary>
        public IList<KeyCap> Caps
        {
            get { return _caps; }
        }

        public Keys SelectedKey
        {
            get { return _selected < 0 ? Keys.None : _caps[_selected].Code; }
        }

        public bool HasSelection
        {
            get
            {
                return KeyboardFilter.HasSelection(_capsFilter, _ctrlFilter, _altFilter,
                    _shiftFilter, _winFilter);
            }
        }

        /// <summary>Keys the current mode highlights: counts in the neutral view, matches otherwise.</summary>
        public int MarkedKeyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _capCounts.Length; i++)
                    if ((HasSelection ? _capCounts[i] : _capAllCounts[i]) > 0) count++;
                return count;
            }
        }

        /// <summary>Keys whose matching shortcuts all exist but are disabled.</summary>
        public int DisabledKeyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _capDisabled.Length; i++)
                    if (_capDisabled[i]) count++;
                return count;
            }
        }

        /// <summary>Number of shortcuts bound to a key over all chords (the neutral count).</summary>
        public int ActionCount(Keys key)
        {
            int index = IndexOfKey(key);
            return index < 0 ? 0 : _capAllCounts[index];
        }

        /// <summary>True when the key carries a planned action name in the current mode.</summary>
        public bool HasActionOn(Keys key)
        {
            ComputeLayout();
            EnsureFonts();
            EnsureLayouts();
            int index = IndexOfKey(key);
            return index >= 0 && _capLayouts[index] != null && _capLayouts[index].HasAction;
        }

        private int IndexOfKey(Keys key)
        {
            if (key == Keys.None) return -1;
            for (int i = 0; i < _caps.Count; i++)
            {
                if (_caps[i].Modifier != KeyModifier.None) continue;
                if (_caps[i].Covers(key)) return i;
            }
            return -1;
        }

        public void SetMappings(IList<KeyMapping> mappings)
        {
            _mappings.Clear();
            if (mappings != null) _mappings.AddRange(mappings);
            Recompute();
            Invalidate();
        }

        public void SetFilter(bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            if (_capsFilter == caps && _ctrlFilter == ctrl && _altFilter == alt
                && _shiftFilter == shift && _winFilter == win) return;
            _capsFilter = caps;
            _ctrlFilter = ctrl;
            _altFilter = alt;
            _shiftFilter = shift;
            _winFilter = win;
            Recompute();
            Invalidate();
        }

        /// <summary>Highlights one key; Keys.None clears the highlight.</summary>
        public void SetSelectedKey(Keys key)
        {
            int index = IndexOfKey(key);
            if (index == _selected) return;
            _selected = index;
            Invalidate();
        }

        public void ClearSelection()
        {
            if (_selected < 0) return;
            _selected = -1;
            Invalidate();
            RaiseSelectionChanged();
        }

        private void Recompute()
        {
            for (int i = 0; i < _capCounts.Length; i++)
            {
                _capCounts[i] = 0;
                _capAllCounts[i] = 0;
                _capEnabled[i] = 0;
                _capActions[i] = null;
                _capDisabled[i] = false;
            }

            for (int m = 0; m < _mappings.Count; m++)
            {
                KeyMapping mapping = _mappings[m];
                if (mapping == null) continue;
                Keys code = (Keys)(mapping.Key & (int)Keys.KeyCode);
                int index = IndexOfKey(code);
                if (index < 0) continue;

                // Every chord feeds the neutral count; only the exact chord feeds
                // the filtered view and its enabled / disabled state.
                _capAllCounts[index]++;
                if (!KeyboardFilter.Exact(mapping, _capsFilter, _ctrlFilter, _altFilter,
                    _shiftFilter, _winFilter)) continue;

                _capCounts[index]++;
                if (mapping.Enabled)
                {
                    if (_capEnabled[index] == 0) _capActions[index] = mapping.ActionText;
                    _capEnabled[index]++;
                }
                else if (_capEnabled[index] == 0 && _capActions[index] == null)
                {
                    // Kept only as a fallback: an enabled duplicate wins the name.
                    _capActions[index] = mapping.ActionText;
                }
            }

            for (int i = 0; i < _capDisabled.Length; i++)
                _capDisabled[i] = _capCounts[i] > 0 && _capEnabled[i] == 0;

            _layoutsDirty = true;
        }

        private bool IsModifierSelected(KeyModifier modifier)
        {
            switch (modifier)
            {
                case KeyModifier.Caps: return _capsFilter;
                case KeyModifier.Ctrl: return _ctrlFilter;
                case KeyModifier.Alt: return _altFilter;
                case KeyModifier.Shift: return _shiftFilter;
                case KeyModifier.Win: return _winFilter;
            }
            return false;
        }

        private void RaiseSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        // ---- geometry ------------------------------------------------------
        private void ComputeLayout()
        {
            int padX = Theme.S(2);
            int padY = Theme.S(2);
            float availableWidth = Math.Max(1f, Width - padX * 2);
            float availableHeight = Math.Max(1f, Height - padY * 2);
            float previous = _unit;
            _unit = Math.Min(availableWidth / KeyboardLayout.TotalWidth,
                availableHeight / KeyboardLayout.TotalHeight);
            if (_unit <= 0.1f) _unit = 0.1f;
            if (Math.Abs(previous - _unit) > 0.0001f) _layoutsDirty = true;
            _originX = padX + (availableWidth - _unit * KeyboardLayout.TotalWidth) / 2f;
            _originY = padY + (availableHeight - _unit * KeyboardLayout.TotalHeight) / 2f;

            for (int i = 0; i < _caps.Count; i++)
            {
                KeyCap cap = _caps[i];
                _rects[i] = new RectangleF(_originX + cap.X * _unit, _originY + cap.Y * _unit,
                    cap.Width * _unit, cap.Height * _unit);
            }
        }

        /// <summary>Top face of a cap: what is left after the 3D depth and the gaps.</summary>
        private RectangleF TopFace(int index)
        {
            RectangleF cell = _rects[index];
            float gap = Math.Max(1f, _unit * KeyGapUnit);
            float faceWidth = cell.Width - gap * 2f;
            float faceHeight = cell.Height - gap * 2f;
            float depth = Math.Min(faceHeight * 0.10f, Math.Max(1.5f, _unit * 0.06f));
            return new RectangleF(cell.X + gap, cell.Y + gap, faceWidth, faceHeight - depth);
        }

        private int IndexAt(Point point)
        {
            ComputeLayout();
            for (int i = 0; i < _rects.Length; i++)
            {
                if (_rects[i].Contains(point.X, point.Y)) return i;
            }
            return -1;
        }

        // ---- painting ------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush back = new SolidBrush(Theme.Background))
                g.FillRectangle(back, ClientRectangle);

            ComputeLayout();
            EnsureFonts();
            EnsureLayouts();
            for (int i = 0; i < _caps.Count; i++) DrawKey(g, i);
            if (_dragActive) DrawDragOverlay(g);
        }

        /// <summary>Drop target ring, dimmed source cap and the lifted card under the pointer.</summary>
        private void DrawDragOverlay(Graphics g)
        {
            if (_dragFrom < 0) return;

            if (_dragTo >= 0)
            {
                RectangleF target = TopFace(_dragTo);
                float radius = Math.Min(Theme.SF(4f), Math.Max(2f, target.Height * 0.18f));
                Theme.FillRound(g, target, radius, Theme.Alpha(Theme.Accent, 42));
                Theme.StrokeRound(g, new RectangleF(target.X + 0.5f, target.Y + 0.5f,
                    Math.Max(0.5f, target.Width - 1f), Math.Max(0.5f, target.Height - 1f)),
                    radius, Theme.Accent, 2f);
            }

            // The source stays faintly visible so the user keeps track of where the
            // shortcuts came from while the card itself travels with the pointer.
            RectangleF source = TopFace(_dragFrom);
            float sourceRadius = Math.Min(Theme.SF(4f), Math.Max(2f, source.Height * 0.18f));
            Theme.FillRound(g, source, sourceRadius, Theme.Alpha(Theme.Background, 170));

            RectangleF ghost = DragGhostRect();
            // The lifted card is the cap itself, not a wider panel: same face,
            // same corner radius. Only the key symbol is dropped; the action line
            // keeps the size and position it has on the board.
            float ghostRadius = Math.Min(Theme.SF(4f), Math.Max(2f, ghost.Height * 0.18f));

            Theme.FillRound(g, new RectangleF(ghost.X + 1f, ghost.Y + 3f, ghost.Width, ghost.Height),
                ghostRadius, Theme.Alpha(Color.Black, Theme.IsDark ? 90 : 30));
            Theme.FillRound(g, ghost, ghostRadius, Theme.IsDark ? Theme.SurfaceAlt : Theme.Surface);
            Theme.StrokeRound(g, new RectangleF(ghost.X + 0.5f, ghost.Y + 0.5f,
                Math.Max(0.5f, ghost.Width - 1f), Math.Max(0.5f, ghost.Height - 1f)),
                ghostRadius, Theme.Accent, 2f);

            KeyActionLayout layout = _capLayouts[_dragFrom];
            if (layout != null && layout.HasAction)
                DrawActionKey(g, _caps[_dragFrom], ghost, Theme.Accent, layout, true);
        }

        /// <summary>
        /// The lifted card is the source cap's face, unchanged in size and shape;
        /// it follows the pointer with the grab offset taken inside that face.
        /// </summary>
        private RectangleF DragGhostRect()
        {
            RectangleF face = TopFace(_dragFrom);
            float x = Math.Max(Theme.SF(2f), Math.Min(_dragRender.X - _dragGrab.X,
                ClientSize.Width - face.Width - Theme.SF(2f)));
            float y = Math.Max(Theme.SF(2f), Math.Min(_dragRender.Y - _dragGrab.Y,
                ClientSize.Height - face.Height - Theme.SF(2f)));
            return new RectangleF(x, y, face.Width, face.Height);
        }

        /// <summary>
        /// Fonts are pixel-sized and follow the key size, so they have to be
        /// rebuilt whenever the board is resized (which also covers DPI changes).
        /// </summary>
        private void EnsureFonts()
        {
            if (_labelFont != null && Math.Abs(_fontUnit - _unit) < 0.4f) return;
            _fontUnit = _unit;
            if (_labelFont != null)
            {
                _labelFont.Dispose();
                _subFont.Dispose();
                _tinyFont.Dispose();
                _countFont.Dispose();
            }
            DisposeActionFonts();
            KeyLabel.CreateFonts(_unit, out _labelFont, out _subFont, out _tinyFont);
            _countFont = new Font(Theme.Body.FontFamily, Math.Max(5.5f, _unit * 0.155f),
                FontStyle.Bold, GraphicsUnit.Pixel);
            KeyActionText.BuildLadder(_unit, out _actionFonts, out _actionLines);
            _layoutsDirty = true;
        }

        /// <summary>
        /// Fits the action name of every cap once per size or data change. Working
        /// from the real cap rectangles means wide keys, 1u letters and the numpad's
        /// double-height keys are all handled by the same rule.
        /// </summary>
        private void EnsureLayouts()
        {
            if (!_layoutsDirty && Math.Abs(_layoutUnit - _unit) < 0.01f) return;
            _layoutUnit = _unit;
            _layoutsDirty = false;
            if (_labelFont == null || _actionFonts == null) return;

            for (int i = 0; i < _caps.Count; i++)
            {
                RectangleF face = TopFace(i);
                _capLayouts[i] = KeyActionText.Layout(_caps[i], (int)face.Width + 2,
                    face.Height, _unit, _labelFont, _subFont, _tinyFont,
                    _actionFonts, _actionLines,
                    _caps[i].Modifier == KeyModifier.None ? _capActions[i] : null);
            }
        }

        private void DisposeActionFonts()
        {
            if (_actionFonts == null) return;
            for (int i = 0; i < _actionFonts.Length; i++) _actionFonts[i].Dispose();
            _actionFonts = null;
            _actionLines = null;
        }

        /// <summary>
        /// Self-test hook: checks the built plans against the real cap rectangles.
        /// The synthetic audit works from key units, this one from the control's
        /// actual pixel sizes, so a rounding difference in the paint path cannot
        /// hide an overflowing name.
        /// </summary>
        public List<string> LayoutProblems()
        {
            ComputeLayout();
            EnsureFonts();
            EnsureLayouts();

            List<string> problems = new List<string>();
            for (int i = 0; i < _caps.Count; i++)
            {
                KeyActionLayout layout = _capLayouts[i];
                if (layout == null || !layout.HasAction) continue;
                string name = string.IsNullOrEmpty(_caps[i].Label) ? "Space" : _caps[i].Label;
                RectangleF face = TopFace(i);
                int rowWidth = Math.Max(1, (int)face.Width + 2);
                int actionWidth = Math.Max(1, (int)face.Width - 2);
                int height = Math.Max(1, (int)face.Height);
                if (layout.Height > height)
                    problems.Add(name + ":tall " + layout.Height + ">" + height);
                if (layout.ActionFont.Size < KeyActionText.MinEm - 0.01f)
                    problems.Add(name + ":tiny");
                int measured = Theme.Measure(layout.ActionText, layout.ActionFont).Width;
                if (measured > actionWidth)
                    problems.Add(name + ":wide " + measured + ">" + actionWidth
                        + " " + layout.ActionText);
                for (int r = 0; r < layout.RowText.Length; r++)
                {
                    if (Theme.Measure(layout.RowText[r], layout.RowFont[r]).Width > rowWidth)
                        problems.Add(name + ":label " + layout.RowText[r]);
                }
            }
            return problems;
        }

        private void DrawKey(Graphics g, int index)
        {
            KeyCap cap = _caps[index];
            RectangleF cell = _rects[index];
            float gap = Math.Max(1f, _unit * KeyGapUnit);
            RectangleF face = new RectangleF(cell.X + gap, cell.Y + gap,
                cell.Width - gap * 2f, cell.Height - gap * 2f);
            if (face.Width < 4f || face.Height < 4f) return;

            float depth = Math.Min(face.Height * 0.10f, Math.Max(1.5f, _unit * 0.06f));
            float radius = Math.Min(Theme.SF(4f), Math.Max(2f, face.Height * 0.18f));
            RectangleF top = new RectangleF(face.X, face.Y, face.Width, face.Height - depth);
            RectangleF bottom = new RectangleF(face.X, face.Y + depth, face.Width, face.Height - depth);

            bool filtered = HasSelection;
            bool modifierOn = cap.Modifier != KeyModifier.None && IsModifierSelected(cap.Modifier);
            bool enabledBound = filtered && _capEnabled[index] > 0;
            bool disabledOnly = filtered && _capCounts[index] > 0 && _capEnabled[index] == 0;
            bool usedNeutral = !filtered && _capAllCounts[index] > 0;
            bool hovered = index == _hover;
            bool selected = index == _selected;

            Color faceColor = Theme.IsDark ? Theme.SurfaceAlt : Theme.Surface;
            Color edgeColor = Theme.StrokeStrong;
            Color textColor = Theme.IsDark ? Theme.TextPrimary : Theme.TextSecondary;

            if (modifierOn)
            {
                // A selected modifier is a filter switch, not a bound key: keep the
                // normal cap and put a diagonal mask on it instead of the solid
                // accent fill used for keys that carry shortcuts.
                edgeColor = Theme.Accent;
                textColor = Theme.IsDark ? Theme.Accent : Theme.AccentPressed;
            }
            else if (enabledBound)
            {
                faceColor = Theme.IsDark ? Theme.Blend(Theme.Accent, Theme.SurfaceAlt, 0.72f) : Theme.AccentSoft;
                edgeColor = Theme.Alpha(Theme.Accent, 190);
                textColor = Theme.Accent;
            }
            else if (disabledOnly)
            {
                // The shortcut exists but cannot fire: muted cap + grey hatch, never
                // the accent fill that would read as "active".
                faceColor = Theme.IsDark ? Theme.SurfaceAlt : Theme.SurfaceAlt;
                edgeColor = Theme.StrokeStrong;
                textColor = Theme.TextSecondary;
            }
            else if (usedNeutral)
            {
                // Overview state: a light accent wash marks every key that carries
                // shortcuts; the count badge carries the number, not the name.
                faceColor = Theme.IsDark
                    ? Theme.Blend(Theme.AccentSoft, Theme.SurfaceAlt, 0.80f)
                    : Theme.Blend(Theme.AccentSoft, Theme.Surface, 0.55f);
                edgeColor = Theme.Alpha(Theme.Accent, 90);
            }
            if (hovered)
                faceColor = Theme.Blend(faceColor, Theme.IsDark ? Color.White : Color.Black, 0.06f);

            Color shadow = Theme.IsDark
                ? Theme.Blend(faceColor, Color.Black, 0.55f)
                : Theme.Blend(faceColor, Theme.StrokeStrong, 0.55f);

            Theme.FillRound(g, bottom, radius, shadow);
            Theme.FillRound(g, top, radius, faceColor);
            if (modifierOn) DrawModifierMask(g, top, radius);
            else if (disabledOnly) DrawDisabledMask(g, top, radius);
            Theme.StrokeRound(g, new RectangleF(top.X + 0.5f, top.Y + 0.5f,
                Math.Max(0.5f, top.Width - 1f), Math.Max(0.5f, top.Height - 1f)),
                radius, edgeColor, 1f);

            DrawKeyText(g, cap, top, textColor, _capLayouts[index]);
            if (usedNeutral) DrawCountBadge(g, top, _capAllCounts[index]);

            if (selected)
            {
                Theme.StrokeRound(g, new RectangleF(top.X + 1f, top.Y + 1f,
                    Math.Max(1f, top.Width - 2f), Math.Max(1f, top.Height - 2f)),
                    radius, Theme.TextPrimary, 1.6f);
            }
        }

        /// <summary>Diagonal hatch that marks an active modifier key.</summary>
        private void DrawModifierMask(Graphics g, RectangleF face, float radius)
        {
            using (GraphicsPath path = Theme.Rounded(face, radius))
            {
                Region previous = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);
                    using (Pen pen = new Pen(Theme.Alpha(Theme.Accent, 120), 1f))
                    {
                        float step = Math.Max(4f, _unit * 0.18f);
                        for (float x = face.Left - face.Height; x < face.Right + face.Height; x += step)
                            g.DrawLine(pen, x, face.Bottom, x + face.Height, face.Top);
                    }
                }
                finally
                {
                    g.Clip = previous;
                    previous.Dispose();
                }
            }
        }

        /// <summary>
        /// Grey hatch for a key whose shortcuts exist but are all disabled. The
        /// direction is the mirror of <see cref="DrawModifierMask"/> ("\" instead of
        /// "/"), so "modifier selected" and "shortcut turned off" never read as the
        /// same state.
        /// </summary>
        private void DrawDisabledMask(Graphics g, RectangleF face, float radius)
        {
            using (GraphicsPath path = Theme.Rounded(face, radius))
            {
                Region previous = g.Clip;
                try
                {
                    g.SetClip(path, CombineMode.Intersect);
                    using (Pen pen = new Pen(Theme.Alpha(Theme.TextDisabled, 120), 1f))
                    {
                        float step = Math.Max(4f, _unit * 0.20f);
                        for (float x = face.Left - face.Height; x < face.Right + face.Height; x += step)
                            g.DrawLine(pen, x, face.Top, x + face.Height, face.Bottom);
                    }
                }
                finally
                {
                    g.Clip = previous;
                    previous.Dispose();
                }
            }
        }

        /// <summary>
        /// Small count badge in the bottom-right corner of a key in the neutral
        /// overview: how many shortcuts use that main key, over all chords. It is a
        /// true circle, which sits better next to the square caps than the old pill.
        /// The corner is the only place the key label never reaches, so the number
        /// never sits on top of the letter. Below a readable key size the wash alone
        /// marks the key.
        /// </summary>
        private void DrawCountBadge(Graphics g, RectangleF face, int count)
        {
            if (_countFont == null || count <= 0 || _unit < 34f) return;

            string text = count.ToString();
            // Two-digit counts get the extra pixels they need and the badge stays a
            // circle; single digits keep the compact size.
            float textWidth = Theme.Measure(text, _countFont).Width;
            float diameter = Math.Max(9f, Math.Max(_unit * 0.26f, textWidth + 4f));
            float inset = Math.Max(1f, _unit * 0.04f);
            // Every count sits in the same bottom-right corner, Space included: a
            // consistent corner is easier to scan than one badge floating mid-bar.
            RectangleF circle = new RectangleF(face.Right - diameter - inset,
                face.Bottom - diameter - inset, diameter, diameter);
            if (circle.X < face.X + 1f || circle.Y < face.Y + 1f) return;

            Theme.FillRound(g, circle, diameter / 2f, Theme.Accent);
            TextRenderer.DrawText(g, text, _countFont, Rectangle.Round(circle), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private const TextFormatFlags KeyLabelFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
            TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.NoPrefix;

        private const TextFormatFlags KeyLineFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            TextFormatFlags.NoPrefix;

        // No EndEllipsis here: the name was measured against the cap already, so
        // GDI must never shorten it a second time (that used to cut glyphs).
        private const TextFormatFlags ActionTextFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            TextFormatFlags.NoPrefix;

        private void DrawKeyText(Graphics g, KeyCap cap, RectangleF face, Color color,
            KeyActionLayout layout)
        {
            if (layout != null && layout.HasAction)
            {
                DrawActionKey(g, cap, face, color, layout);
                return;
            }
            if (string.IsNullOrEmpty(cap.Label)) return;

            int width = Math.Max(1, (int)face.Width + 2);
            // Labels may spill 1 px into the gap on each side: the neighbour is
            // further away than that and the alternative is a truncated word.
            Rectangle bounds = new Rectangle((int)face.X - 1, (int)face.Y,
                width, Math.Max(1, (int)face.Height));

            KeyLabelPlan plan = KeyLabel.Plan(cap, width, face.Height,
                _labelFont, _subFont, _tinyFont);

            if (plan.Kind == KeyLabelKind.TwoLines)
            {
                DrawKeyLines(g, plan.First, plan.Second, face, color,
                    plan.FirstFont, plan.SecondFont);
                return;
            }

            TextRenderer.DrawText(g, cap.Label, plan.FirstFont, bounds, color, KeyLabelFlags);
        }

        /// <summary>
        /// Draws a cap that carries an action name. The rows come from the measured
        /// plan, so the label keeps its normal face and the action line sits below
        /// it; both are centred as a block when the cap has spare height (numpad
        /// Enter, tall + key). The name is already fitted, so no GDI ellipsis is
        /// involved and a glyph can never be cut at the cap border.
        /// </summary>
        private void DrawActionKey(Graphics g, KeyCap cap, RectangleF face, Color color,
            KeyActionLayout layout)
        {
            DrawActionKey(g, cap, face, color, layout, false);
        }

        /// <summary>
        /// Draws a cap that carries an action name. The rows come from the measured
        /// plan, so the label keeps its normal face and the action line sits below
        /// it; both are centred as a block when the cap has spare height (numpad
        /// Enter, tall + key). The name is already fitted, so no GDI ellipsis is
        /// involved and a glyph can never be cut at the cap border.
        /// When <paramref name="hideSymbol"/> is set the key-name rows keep their
        /// space but are not painted, so the action line stays exactly where it is
        /// on the board (used by the drag card).
        /// </summary>
        private void DrawActionKey(Graphics g, KeyCap cap, RectangleF face, Color color,
            KeyActionLayout layout, bool hideSymbol)
        {
            RectangleF top = face;
            int width = Math.Max(1, (int)top.Width + 2);
            int height = Math.Max(1, (int)top.Height);
            int total = layout.Height;
            int y = (int)Math.Round(top.Y + (height - total) / 2f);
            if (y < top.Y + 1f) y = (int)top.Y + 1;
            if (y + total > top.Y + height) y = (int)top.Y + height - total;

            for (int i = 0; i < layout.RowText.Length; i++)
            {
                Rectangle row = new Rectangle((int)top.X - 1, y, width, layout.RowHeight[i]);
                if (!hideSymbol)
                {
                    TextRenderer.DrawText(g, layout.RowText[i], layout.RowFont[i], row, color,
                        KeyLineFlags);
                }
                y += layout.RowHeight[i];
            }

            Rectangle action = new Rectangle((int)top.X + 1, y,
                Math.Max(1, (int)top.Width - 2), Math.Max(1, layout.ActionHeight));
            TextRenderer.DrawText(g, layout.ActionText, layout.ActionFont, action,
                Theme.Alpha(color, 205), ActionTextFlags);
        }

        private void DrawKeyLines(Graphics g, string first, string second,
            RectangleF face, Color color, Font firstFont, Font secondFont)
        {
            // Row boxes come from the measured line height, not from a fixed split
            // of the face: "Page" and "PageDown" carry a descender, and a 42/58
            // split left the top row a couple of pixels short, so GDI clipped the
            // hook of the g (same for q, p, y, j).
            int[] rows = TwoLineRowHeights(firstFont, secondFont);
            int firstHeight = rows[0];
            int secondHeight = rows[1];
            int total = firstHeight + secondHeight;
            float padY = Math.Max(1f, (face.Height - total) / 2f);
            int y = (int)Math.Round(face.Y + padY);
            Rectangle top = new Rectangle((int)face.X - 1, y,
                Math.Max(1, (int)face.Width + 2), firstHeight);
            Rectangle bottom = new Rectangle((int)face.X - 1, y + firstHeight,
                Math.Max(1, (int)face.Width + 2), secondHeight);

            TextRenderer.DrawText(g, first, firstFont, top, color, KeyLineFlags);
            TextRenderer.DrawText(g, second, secondFont, bottom, color, KeyLineFlags);
        }

        /// <summary>
        /// Row heights for a two-line cap label. Exposed so the self-test can prove
        /// the boxes are tall enough for descenders instead of trusting a ratio.
        /// </summary>
        internal static int[] TwoLineRowHeights(Font firstFont, Font secondFont)
        {
            return new int[]
            {
                KeyActionText.LineHeight(firstFont),
                KeyActionText.LineHeight(secondFont)
            };
        }

        // ---- interaction ---------------------------------------------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragArmed)
            {
                if (!_dragActive)
                {
                    if (Math.Abs(e.X - _dragStart.X) < DragThreshold
                        && Math.Abs(e.Y - _dragStart.Y) < DragThreshold) return;
                    _dragPoint = new PointF(e.X, e.Y);
                    _dragRender = _dragPoint;
                    BeginDrag();
                }
                // The card tracks the pointer one-to-one: any smoothing here reads
                // as lag, and the drag must feel like the cap is under the cursor.
                _dragPoint = new PointF(e.X, e.Y);
                _dragRender = _dragPoint;
                _dragTo = DropTargetAt(e.Location);
                InvalidateDrag();
                return;
            }

            int index = IndexAt(e.Location);
            if (index != _hover)
            {
                _hover = index;
                Invalidate();
            }

            // Every cap responds to the pointer: modifiers toggle the filter, main
            // keys select and narrow the list.
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragActive) return;
            if (_hover == -1) return;
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            int index = IndexAt(e.Location);
            if (index < 0) return;

            KeyCap cap = _caps[index];
            if (e.Button == MouseButtons.Left)
            {
                if (cap.Modifier != KeyModifier.None)
                {
                    Action<KeyModifier> handler = ModifierToggleRequested;
                    if (handler != null) handler(cap.Modifier);
                    return;
                }
                // A used key inside a chord is draggable: press arms the drag and
                // the click only fires on release when the pointer never moved.
                if (HasSelection && _capCounts[index] > 0)
                {
                    ArmDrag(index, e.Location);
                    return;
                }
                // A main key is selectable in both modes: in the neutral overview it
                // only narrows the list (editing still waits for a chord).
                ApplyLeftClick(index, Environment.TickCount);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                // Modifier caps only have a menu inside a chord; a main key always
                // offers "新增到 <键>" — that is the neutral overview's way in.
                if (cap.Modifier != KeyModifier.None && !HasSelection) return;
                if (cap.Modifier == KeyModifier.None && _selected != index)
                {
                    _selected = index;
                    Invalidate();
                    RaiseSelectionChanged();
                }
                EventHandler context = KeyContextRequested;
                if (context != null) context(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragArmed || e.Button != MouseButtons.Left) return;

            int from = _dragFrom;
            int to = _dragTo;
            bool moved = _dragActive;
            EndDrag();

            if (moved)
            {
                if (to >= 0 && to != from) RaiseKeyDrag(from, to);
                // A finished drag is not a click: the next press starts a fresh
                // pair instead of completing one with the pre-drag press.
                _lastClickIndex = -1;
                _lastClickTick = 0;
                return;
            }

            // A press without movement is still an ordinary click.
            if (from >= 0) ApplyLeftClick(from, Environment.TickCount);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // Losing capture mid-drag means the release happened elsewhere:
            // complete the drop instead of leaving a floating cap behind.
            if (!_dragActive || Capture) return;
            int from = _dragFrom;
            int to = _dragTo;
            EndDrag();
            if (to >= 0 && to != from) RaiseKeyDrag(from, to);
            _lastClickIndex = -1;
            _lastClickTick = 0;
        }

        private void ArmDrag(int index, Point location)
        {
            _dragArmed = true;
            _dragActive = false;
            _dragDirtyValid = false;
            _dragFrom = index;
            _dragTo = -1;
            _dragStart = location;
            _dragPoint = new PointF(location.X, location.Y);
            _dragRender = _dragPoint;
            RectangleF face = TopFace(index);
            _dragGrab = new PointF(location.X - face.X, location.Y - face.Y);
            // The press is also the first half of a possible double-click, so the
            // click bookkeeping has to survive until we know whether this really
            // became a drag (see OnMouseUp).
        }

        private void BeginDrag()
        {
            _dragActive = true;
            _hover = -1;
            Capture = true;
            Cursor = Cursors.SizeAll;
            _dragDirtyValid = false;
            InvalidateDrag();
        }

        /// <summary>Repaints only the band the drag visuals occupy (old ∪ new).</summary>
        private void InvalidateDrag()
        {
            if (!_dragActive)
            {
                Invalidate();
                return;
            }
            Rectangle now = DragDirtyBounds();
            Rectangle band = _dragDirtyValid ? Rectangle.Union(_dragDirty, now) : now;
            band.Inflate(4, 4);
            if (band.Width > 0 && band.Height > 0) Invalidate(band);
            _dragDirty = now;
            _dragDirtyValid = true;
        }

        private Rectangle DragDirtyBounds()
        {
            Rectangle band = Rectangle.Empty;
            if (_dragFrom >= 0 && _dragFrom < _rects.Length)
                band = Rectangle.Round(_rects[_dragFrom]);
            if (_dragTo >= 0 && _dragTo < _rects.Length)
                band = band.IsEmpty
                    ? Rectangle.Round(_rects[_dragTo])
                    : Rectangle.Union(band, Rectangle.Round(_rects[_dragTo]));
            if (_dragFrom >= 0 && _dragFrom < _rects.Length)
            {
                Rectangle ghost = Rectangle.Round(DragGhostRect());
                ghost.Inflate(4, 6);
                band = band.IsEmpty ? ghost : Rectangle.Union(band, ghost);
            }
            return band;
        }

        /// <summary>Aborts a drag without moving anything (Esc at the form level).</summary>
        internal void CancelDrag()
        {
            if (_dragArmed || _dragActive) EndDrag();
        }

        private void EndDrag()
        {
            Rectangle band = _dragDirtyValid ? _dragDirty : Rectangle.Empty;
            _dragArmed = false;
            _dragActive = false;
            _dragFrom = -1;
            _dragTo = -1;
            _dragDirtyValid = false;
            if (Capture) Capture = false;
            Cursor = Cursors.Default;
            if (band.Width > 0 && band.Height > 0)
            {
                band.Inflate(6, 8);
                Invalidate(band);
            }
            else
            {
                Invalidate();
            }
        }

        /// <summary>The main key under the pointer that could receive the drop.</summary>
        private int DropTargetAt(Point point)
        {
            int index = IndexAt(point);
            if (index < 0 || index == _dragFrom) return -1;
            if (_caps[index].Modifier != KeyModifier.None) return -1;
            return index;
        }

        private void RaiseKeyDrag(int from, int to)
        {
            Action<Keys, Keys> handler = KeyDragDropped;
            if (handler == null || from < 0 || to < 0 || to == from) return;
            handler(_caps[from].Code, _caps[to].Code);
        }

        /// <summary>
        /// Self-test hook: completes a drag from one key to another and reports
        /// whether the drop event ran with that pair.
        /// </summary>
        internal bool SimulateKeyDrag(Keys fromKey, Keys toKey)
        {
            int from = IndexOfKey(fromKey);
            int to = IndexOfKey(toKey);
            if (from < 0 || to < 0 || from == to) return false;

            bool raised = false;
            Keys receivedFrom = Keys.None;
            Keys receivedTo = Keys.None;
            Action<Keys, Keys> probe = delegate(Keys a, Keys b)
            {
                raised = true;
                receivedFrom = a;
                receivedTo = b;
            };

            Action<Keys, Keys> previous = KeyDragDropped;
            KeyDragDropped += probe;
            _dragFrom = from;
            _dragTo = to;
            try { RaiseKeyDrag(from, to); }
            finally
            {
                KeyDragDropped -= probe;
                KeyDragDropped = previous;
                _dragFrom = -1;
                _dragTo = -1;
            }
            return raised && receivedFrom == fromKey && receivedTo == toKey;
        }


        /// <summary>
        /// Left click on a main key. With a chord selected the second click of a
        /// double-click keeps the key selected, so the activation that follows
        /// knows which shortcut the user means. In the neutral overview there is
        /// no edit step to protect, so every click toggles instead: a double-click
        /// selects and deselects, rather than swallowing its second half and
        /// leaving the user with no reaction at all.
        /// </summary>
        private void ApplyLeftClick(int index, int tick)
        {
            bool chord = HasSelection;
            int elapsed = tick - _lastClickTick;
            bool secondClick = chord && index == _lastClickIndex
                && elapsed >= 0 && elapsed <= QuickDoubleClickMs;

            // Entering a chord must not let an old neutral click count as the
            // first half of a double-click.
            _lastClickIndex = chord ? index : -1;
            _lastClickTick = chord ? tick : 0;

            if (secondClick)
            {
                if (_selected != index)
                {
                    _selected = index;
                    Invalidate();
                    RaiseSelectionChanged();
                }
                return;
            }

            _selected = _selected == index ? -1 : index;
            Invalidate();
            RaiseSelectionChanged();
        }

        /// <summary>
        /// Self-test hook: replays a left click on a cap with an explicit tick, so
        /// the neutral and chord double-click rules can be checked without a window.
        /// </summary>
        public Keys ClickKey(Keys key, int tick)
        {
            int index = IndexOfKey(key);
            if (index < 0) return Keys.None;
            ApplyLeftClick(index, tick);
            return SelectedKey;
        }

        /// <summary>
        /// A second click on the same cap within <see cref="QuickDoubleClickMs"/>:
        /// the double-click candidate. Measured from the click toggle rather than
        /// from mouse-down, because a drag resets its own press bookkeeping and
        /// the first half of the pair may itself have started on another cap.
        /// </summary>
        private bool IsQuickSecondClick(int index, int tick)
        {
            int elapsed = tick - _lastClickTick;
            return index == _lastClickIndex && elapsed >= 0 && elapsed <= QuickDoubleClickMs;
        }

        /// <summary>
        /// Self-test hook: replays the activation decision for a cap with an
        /// explicit tick, without needing a real double-click message.
        /// </summary>
        internal bool DoubleClickForTest(Keys key, int tick)
        {
            int index = IndexOfKey(key);
            if (index < 0) return false;
            if (!IsQuickSecondClick(index, tick)) return false;
            ActivateCap(index, tick);
            return true;
        }

        /// <summary>
        /// Self-test hook: arms a drag on a cap without a real mouse press, so the
        /// double-click bookkeeping can be checked across the drag path.
        /// </summary>
        internal void ArmDragForTest(Keys key)
        {
            int index = IndexOfKey(key);
            if (index >= 0) ArmDrag(index, new Point(0, 0));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;

            int index = IndexAt(e.Location);
            if (index < 0 || _caps[index].Modifier != KeyModifier.None) return;

            // The system still reports a double-click for pairs up to ~500 ms apart;
            // only a genuinely quick pair opens an editor. The first click may have
            // toggled the cap off (after a drag it always does), so activation must
            // not require a live selection here.
            if (!IsQuickSecondClick(index, Environment.TickCount)) return;
            ActivateCap(index, Environment.TickCount);
        }

        /// <summary>
        /// Opens the editor for a cap. The first half of a double-click already
        /// toggled the selection, and a drag or a previous click may have moved it,
        /// so this re-asserts the cap before raising the activation event.
        /// </summary>
        private void ActivateCap(int index, int tick)
        {
            if (_selected != index)
            {
                _selected = index;
                Invalidate();
                RaiseSelectionChanged();
            }
            // The release after this event must not toggle the fresh selection off.
            _lastClickIndex = index;
            _lastClickTick = tick;

            EventHandler handler = KeyActivated;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// Self-test hook: runs the activation path for a cap as if the preceding
        /// press had just happened and the cap were the current selection.
        /// </summary>
        internal bool ActivateCapForTest(Keys key)
        {
            int index = IndexOfKey(key);
            if (index < 0 || !HasSelection) return false;
            ActivateCap(index, Environment.TickCount);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_labelFont != null)
                {
                    _labelFont.Dispose();
                    _subFont.Dispose();
                    _tinyFont.Dispose();
                }
                if (_countFont != null) _countFont.Dispose();
                DisposeActionFonts();
            }
            base.Dispose(disposing);
        }
    }
}


