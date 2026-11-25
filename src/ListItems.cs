using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Shared painting helpers for owner-drawn rows.</summary>
    internal static class RowPaint
    {
        public static void Toggle(Graphics g, Rectangle bounds, bool on, bool enabled)
        {
            RectangleF track = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
            float radius = track.Height / 2f;
            if (on)
            {
                Color fill = enabled ? Theme.Accent : Theme.Blend(Theme.Accent, Theme.Surface, 0.6f);
                Theme.FillRound(g, track, radius, fill);
            }
            else
            {
                Theme.FillRound(g, track, radius, Theme.Layer);
                Theme.StrokeRound(g, track, radius, enabled ? Theme.StrokeStrong : Theme.Stroke, 1f);
            }
            float knob = track.Height - Theme.SF(4f);
            float x = on ? track.Right - knob - Theme.SF(2f) : track.Left + Theme.SF(2f);
            Color knobColor = !enabled ? Theme.TextDisabled
                : on ? Theme.OnAccent : Theme.TextSecondary;
            using (SolidBrush b = new SolidBrush(knobColor))
                g.FillEllipse(b, new RectangleF(x, track.Top + Theme.SF(2f), knob, knob));
        }

        public static void Chip(Graphics g, Rectangle bounds, string text, Font font,
            Color fill, Color stroke, Color fore)
        {
            Chip(g, bounds, text, font, fill, stroke, fore, ChipTextFlags);
        }

        private const TextFormatFlags ChipTextFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;

        /// <summary>
        /// Drawn chip with explicit text flags. Left-aligned text (used when the
        /// content has to be trimmed) is inset by a normal chip padding so a glyph
        /// never touches the rounded border.
        /// </summary>
        public static void Chip(Graphics g, Rectangle bounds, string text, Font font,
            Color fill, Color stroke, Color fore, TextFormatFlags textFlags)
        {
            RectangleF r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f,
                Math.Max(1f, bounds.Width - 1f), Math.Max(1f, bounds.Height - 1f));
            float radius = Theme.SF(Theme.RadiusChip);
            Theme.FillRound(g, r, radius, fill);
            if (stroke.A > 0) Theme.StrokeRound(g, r, radius, stroke, 1f);

            Rectangle textRect = bounds;
            if ((textFlags & TextFormatFlags.HorizontalCenter) == 0)
            {
                textRect = new Rectangle(bounds.X + Theme.S(8), bounds.Y,
                    Math.Max(1, bounds.Width - Theme.S(16)), bounds.Height);
            }
            TextRenderer.DrawText(g, text, font, textRect, fore, textFlags);
        }
    }

    /// <summary>Row for one key mapping in the main list.</summary>
    internal sealed class MappingListItem : MListItem
    {
        private readonly KeyMapping _mapping;
        private readonly bool _shadowed;

        public MappingListItem(KeyMapping mapping, bool shadowed)
        {
            _mapping = mapping;
            _shadowed = shadowed;
        }

        public KeyMapping Mapping { get { return _mapping; } }

        private const TextFormatFlags ChipTextFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;

        // A combination too long for the row keeps its beginning and loses the tail:
        // centred text would be cut on both sides and read as a different chord.
        private const TextFormatFlags ChipTextFlagsTrimmed =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
            TextFormatFlags.PreserveGraphicsClipping;

        /// <summary>Width a trigger needs to be drawn without trimming.</summary>
        internal static int ChipNeededWidth(string trigger)
        {
            return Theme.Measure(trigger == null ? "" : trigger, Theme.KeyCap).Width + Theme.S(18);
        }

        /// <summary>
        /// Width the trigger chip gets inside <paramref name="available"/> pixels.
        /// Exposed so the self-test can prove a five-modifier chord is not clamped
        /// to the old fixed 220 px column.
        /// </summary>
        internal static int TriggerChipWidth(string trigger, int available)
        {
            int needed = ChipNeededWidth(trigger);
            int max = Math.Max(Theme.S(46), available);
            return Math.Max(Theme.S(46), Math.Min(needed, max));
        }

        public override string Key
        {
            get { return _mapping.TriggerText; }
        }

        public override Rectangle ToggleBounds(Rectangle bounds)
        {
            int width = Theme.S(40);
            int height = Theme.S(20);
            return new Rectangle(bounds.Right - Theme.S(16) - width,
                bounds.Y + (bounds.Height - height) / 2, width, height);
        }

        public override void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled)
        {
            bool on = _mapping.Enabled;

            if (selected)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceAlt);
                RectangleF bar = new RectangleF(bounds.X + Theme.SF(3f),
                    bounds.Y + (bounds.Height - Theme.SF(18f)) / 2f, Theme.SF(3f), Theme.SF(18f));
                Theme.FillRound(g, bar, Theme.SF(1.5f), Theme.Accent);
            }
            else if (hovered)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceHover);
            }

            Rectangle toggle = ToggleBounds(bounds);
            int chipHeight = Theme.S(26);
            int chipY = bounds.Y + (bounds.Height - chipHeight) / 2;
            int chipX = bounds.X + Theme.S(16);

            // Type tag sits right before the toggle. Since 1.8.1 the trigger layer
            // is always Caps, so the old "范围" column would repeat the same word on
            // every row; the tag now answers the more useful question of where the
            // action comes from.
            ActionDef actionDef = ActionCatalog.Get(_mapping.Action);
            string actionType;
            if (actionDef != null && actionDef.IsMacro) actionType = "宏";
            else if (actionDef != null && actionDef.IsPlugin) actionType = "插件";
            else actionType = "原生";

            bool isMacro = actionType == "宏";
            Size typeSize = Theme.Measure(actionType, Theme.Caption);
            int typeWidth = typeSize.Width + Theme.S(18);
            int typeHeight = Theme.S(20);
            Rectangle typeRect = new Rectangle(toggle.X - Theme.S(12) - typeWidth,
                bounds.Y + (bounds.Height - typeHeight) / 2, typeWidth, typeHeight);
            RowPaint.Chip(g, typeRect, actionType, Theme.Caption,
                isMacro ? Theme.AccentSoft : Color.Transparent,
                isMacro ? Theme.Alpha(Theme.Accent, 90) : Theme.Stroke,
                !on ? Theme.TextDisabled : (isMacro ? Theme.Accent : Theme.TextSecondary));

            // A row that repeats an earlier trigger can never fire (the Caps layer
            // takes the first match, the OS rejects the second global hotkey), so
            // label it instead of showing a live-looking toggle.
            Rectangle duplicateRect = Rectangle.Empty;
            if (_shadowed)
            {
                Size duplicateSize = Theme.Measure("重复", Theme.Caption);
                int duplicateWidth = duplicateSize.Width + Theme.S(14);
                duplicateRect = new Rectangle(typeRect.X - Theme.S(8) - duplicateWidth,
                    bounds.Y + (bounds.Height - typeHeight) / 2, duplicateWidth, typeHeight);
                RowPaint.Chip(g, duplicateRect, "重复", Theme.Caption,
                    Theme.DangerSoft, Theme.Alpha(Theme.Danger, 110), Theme.Danger);
            }

            // The trigger chip grows with its content instead of stopping at a fixed
            // 220 px: a combination with all five modifiers is wider than that and
            // was simply cut in half. The action name keeps its own minimum, so the
            // chip can never take the whole row.
            int rightEdge = duplicateRect.IsEmpty ? typeRect.X : duplicateRect.X;
            string trigger = _mapping.TriggerText;
            int chipAvailable = Math.Max(Theme.S(46),
                rightEdge - Theme.S(24) - Theme.S(160) - chipX);
            int chipWidth = TriggerChipWidth(trigger, chipAvailable);
            bool chipFits = chipWidth >= ChipNeededWidth(trigger);
            Rectangle chipRect = new Rectangle(chipX, chipY, chipWidth, chipHeight);

            RowPaint.Chip(g, chipRect, trigger, Theme.KeyCap,
                on ? Theme.Layer : Theme.Surface,
                on ? Theme.Stroke : Theme.Stroke,
                on ? Theme.TextPrimary : Theme.TextDisabled,
                chipFits ? ChipTextFlags : ChipTextFlagsTrimmed);

            int textX = chipRect.Right + Theme.S(14);
            int textWidth = Math.Max(Theme.S(40), rightEdge - Theme.S(10) - textX);
            Rectangle textRect = new Rectangle(textX, bounds.Y, textWidth, bounds.Height);
            TextRenderer.DrawText(g, _mapping.ActionText, on ? Theme.Body : Theme.Body, textRect,
                on ? Theme.TextPrimary : Theme.TextDisabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping);

            RowPaint.Toggle(g, toggle, on, true);
        }
    }

    /// <summary>Row used by the action picker list.</summary>
    internal sealed class ChoiceListItem : MListItem
    {
        private readonly string _id;
        private readonly string _category;
        private readonly string _title;
        private readonly string _hint;
        private readonly string _shortcut;
        private readonly object _tag;

        public ChoiceListItem(string id, string category, string title, string hint, string shortcut, object tag)
        {
            _id = id;
            _category = category;
            _title = title;
            _hint = hint;
            _shortcut = shortcut;
            _tag = tag;
        }

        public object Tag { get { return _tag; } }
        public string Id { get { return _id; } }

        public override string Key { get { return _title; } }

        public override void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled)
        {
            if (selected)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(4f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(8f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceAlt);
            }
            else if (hovered)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(4f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(8f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceHover);
            }

            int padding = Theme.S(14);
            int x = bounds.X + padding;
            int rightReserve = Theme.S(16);

            if (!string.IsNullOrEmpty(_shortcut))
            {
                Size size = Theme.Measure(_shortcut, Theme.Caption);
                int w = size.Width + Theme.S(12);
                Rectangle keyRect = new Rectangle(bounds.Right - padding - w,
                    bounds.Y + (bounds.Height - Theme.S(22)) / 2, w, Theme.S(22));
                RowPaint.Chip(g, keyRect, _shortcut, Theme.Caption, Theme.Layer, Theme.Stroke,
                    Theme.TextSecondary);
                rightReserve = w + Theme.S(12);
            }

            int categoryWidth = 0;
            int titleY = string.IsNullOrEmpty(_hint) ? bounds.Y : bounds.Y - Theme.S(6);

            if (!string.IsNullOrEmpty(_category))
            {
                Size size = Theme.Measure(_category, Theme.Caption);
                categoryWidth = size.Width + Theme.S(12);
                TextRenderer.DrawText(g, _category, Theme.Caption,
                    new Rectangle(x, titleY, categoryWidth, bounds.Height),
                    Theme.TextTertiary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
            }

            int titleX = x + categoryWidth + (categoryWidth > 0 ? Theme.S(10) : 0);
            int titleWidth = Math.Max(Theme.S(30),
                bounds.Right - padding - rightReserve - titleX);

            TextRenderer.DrawText(g, _title, Theme.Body, new Rectangle(titleX, titleY, titleWidth, bounds.Height),
                Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping);

            if (!string.IsNullOrEmpty(_hint))
            {
                TextRenderer.DrawText(g, _hint, Theme.Caption,
                    new Rectangle(titleX, bounds.Y + Theme.S(14), titleWidth, bounds.Height),
                    Theme.TextTertiary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping);
            }
        }
    }

    /// <summary>Row for one plugin on the plugins page.</summary>
    internal sealed class PluginListItem : MListItem
    {
        private readonly PluginStatus _plugin;

        public PluginListItem(PluginStatus plugin)
        {
            _plugin = plugin;
        }

        public PluginStatus Plugin { get { return _plugin; } }

        public override string Key { get { return _plugin.Name; } }

        public override void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled)
        {
            if (selected)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceAlt);
                RectangleF bar = new RectangleF(bounds.X + Theme.SF(3f),
                    bounds.Y + (bounds.Height - Theme.SF(18f)) / 2f, Theme.SF(3f), Theme.SF(18f));
                Theme.FillRound(g, bar, Theme.SF(1.5f), Theme.Accent);
            }
            else if (hovered)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceHover);
            }

            int statusWidth = Theme.S(64);
            Rectangle statusRect = new Rectangle(bounds.Right - Theme.S(16) - statusWidth,
                bounds.Y + (bounds.Height - Theme.S(20)) / 2, statusWidth, Theme.S(20));

            string countText = _plugin.ActionCount + " 个动作";
            Size countSize = Theme.Measure(countText, Theme.Caption);
            int countWidth = countSize.Width + Theme.S(14);
            Rectangle countRect = new Rectangle(statusRect.X - Theme.S(8) - countWidth,
                statusRect.Y, countWidth, Theme.S(20));

            int textX = bounds.X + Theme.S(16);
            int textWidth = Math.Max(Theme.S(60), countRect.X - Theme.S(12) - textX);

            Theme.DrawText(g, _plugin.Name, Theme.BodyStrong,
                new Rectangle(textX, bounds.Y + Theme.S(6), textWidth, Theme.S(22)),
                Theme.TextPrimary,
                TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);

            string meta = _plugin.IsLoaded
                ? _plugin.Id + "  ·  v" + _plugin.Version
                    + (_plugin.Author.Length > 0 ? "  ·  " + _plugin.Author : "")
                : _plugin.FileName + (_plugin.Trusted ? "  ·  未能加载" : "  ·  未信任");
            Theme.DrawText(g, meta, Theme.Caption,
                new Rectangle(textX, bounds.Y + Theme.S(28), textWidth, Theme.S(20)),
                Theme.TextTertiary,
                TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);

            RowPaint.Chip(g, countRect, countText, Theme.Caption,
                Theme.Layer, Theme.Stroke, Theme.TextSecondary);

            if (_plugin.IsLoaded)
            {
                RowPaint.Chip(g, statusRect, "已加载", Theme.Caption,
                    Theme.IsDark ? Theme.Blend(Theme.Success, Theme.Background, 0.84f)
                                 : Color.FromArgb(240, 250, 240),
                    Color.Empty, Theme.Success);
            }
            else if (!_plugin.Trusted)
            {
                RowPaint.Chip(g, statusRect, "未信任", Theme.Caption,
                    Theme.Layer, Color.Empty, Theme.Warning);
            }
            else
            {
                RowPaint.Chip(g, statusRect, "失败", Theme.Caption,
                    Theme.DangerSoft, Color.Empty, Theme.Danger);
            }
        }
    }

    /// <summary>Row for one macro on the macro page.</summary>
    internal sealed class MacroListItem : MListItem
    {
        private readonly Macro _macro;
        private readonly int _bound;
        private readonly bool _running;

        public MacroListItem(Macro macro, int bound, bool running)
        {
            _macro = macro;
            _bound = bound;
            _running = running;
        }

        public Macro Macro { get { return _macro; } }

        public override string Key { get { return _macro.Name; } }

        public override void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled)
        {
            if (selected)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceAlt);
                RectangleF bar = new RectangleF(bounds.X + Theme.SF(3f),
                    bounds.Y + (bounds.Height - Theme.SF(18f)) / 2f, Theme.SF(3f), Theme.SF(18f));
                Theme.FillRound(g, bar, Theme.SF(1.5f), Theme.Accent);
            }
            else if (hovered)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceHover);
            }

            int countWidth = Theme.S(58);
            Rectangle countRect = new Rectangle(bounds.Right - Theme.S(150) - countWidth,
                bounds.Y + (bounds.Height - Theme.S(20)) / 2, countWidth, Theme.S(20));
            int usedWidth = Theme.S(134);
            Rectangle usedRect = new Rectangle(bounds.Right - Theme.S(16) - usedWidth,
                bounds.Y + (bounds.Height - Theme.S(20)) / 2, usedWidth, Theme.S(20));

            int textX = bounds.X + Theme.S(16);
            int textWidth = Math.Max(Theme.S(60), countRect.X - Theme.S(12) - textX);

            string meta = _macro.StepCount + " 个动作";
            if (_macro.RestoreCursor) meta += "  ·  返回原位";
            if (!string.IsNullOrEmpty(_macro.Note)) meta += "  ·  " + _macro.Note;
            Theme.DrawText(g, _macro.Name, Theme.BodyStrong,
                new Rectangle(textX, bounds.Y + Theme.S(6), textWidth, Theme.S(22)),
                Theme.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
            Theme.DrawText(g, meta, Theme.Caption,
                new Rectangle(textX, bounds.Y + Theme.S(28), textWidth, Theme.S(20)),
                Theme.TextTertiary, TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);

            Theme.DrawText(g, _macro.StepCount.ToString(), Theme.Caption,
                countRect, Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (_running)
            {
                RowPaint.Chip(g, usedRect, "运行中", Theme.Caption,
                    Theme.AccentSoft, Theme.Alpha(Theme.Accent, 110), Theme.Accent);
            }
            else if (_bound > 0)
            {
                RowPaint.Chip(g, usedRect, "被 " + _bound + " 个快捷键使用", Theme.Caption,
                    Theme.Layer, Theme.Stroke, Theme.TextSecondary);
            }
            else
            {
                Theme.DrawText(g, "未绑定", Theme.Caption, usedRect, Theme.TextDisabled,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Row for one step inside the macro editor.</summary>
    internal sealed class MacroStepListItem : MListItem
    {
        private readonly MacroStep _step;
        private readonly bool _missing;

        /// <summary>1-based position of this step in the list; refreshed on every rebuild.</summary>
        public int StepNumber { get; set; }

        public MacroStepListItem(MacroStep step, bool missing)
        {
            _step = step;
            _missing = missing;
        }

        public MacroStep Step { get { return _step; } }

        public override string Key
        {
            get
            {
                string note = (_step.Note ?? "").Trim();
                if (note.Length > 0) return note;
                return ActionCatalog.Describe(_step.Action, _step.Param);
            }
        }

        public override void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled)
        {
            if (selected)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceAlt);
                RectangleF bar = new RectangleF(bounds.X + Theme.SF(3f),
                    bounds.Y + (bounds.Height - Theme.SF(18f)) / 2f, Theme.SF(3f), Theme.SF(18f));
                Theme.FillRound(g, bar, Theme.SF(1.5f), Theme.Accent);
            }
            else if (hovered)
            {
                RectangleF fill = new RectangleF(bounds.X + Theme.SF(6f), bounds.Y + Theme.SF(2f),
                    bounds.Width - Theme.SF(12f), bounds.Height - Theme.SF(4f));
                Theme.FillRound(g, fill, Theme.SF(Theme.RadiusChip), Theme.SurfaceHover);
            }

            int countWidth = Theme.S(44);
            Rectangle countRect = new Rectangle(bounds.Right - Theme.S(16) - countWidth,
                bounds.Y + (bounds.Height - Theme.S(20)) / 2, countWidth, Theme.S(20));
            int delayWidth = Theme.S(64);
            Rectangle delayRect = new Rectangle(countRect.X - Theme.S(10) - delayWidth,
                countRect.Y, delayWidth, countRect.Height);
            int paramWidth = Theme.S(132);
            Rectangle paramRect = new Rectangle(delayRect.X - Theme.S(14) - paramWidth,
                countRect.Y, paramWidth, countRect.Height);

            Rectangle stepNumberRect = new Rectangle(bounds.X + Theme.S(18),
                bounds.Y, Theme.S(40), bounds.Height);
            Theme.DrawText(g, StepNumber > 0 ? StepNumber.ToString() : "", Theme.Caption,
                stepNumberRect, Theme.TextTertiary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.PreserveGraphicsClipping);

            int textX = bounds.X + Theme.S(74);
            bool macroStep = ActionCatalog.IsMacroActionId(_step.Action);
            if (macroStep)
            {
                Rectangle chipRect = new Rectangle(textX, bounds.Y + (bounds.Height - Theme.S(20)) / 2,
                    Theme.S(34), Theme.S(20));
                RowPaint.Chip(g, chipRect, "宏", Theme.Caption,
                    Theme.AccentSoft, Theme.Alpha(Theme.Accent, 110), Theme.Accent);
                textX = chipRect.Right + Theme.S(10);
            }

            int textWidth = Math.Max(Theme.S(40), paramRect.X - Theme.S(10) - textX);
            string note = (_step.Note ?? "").Trim();
            // A named step shows only the user's label in the action column: the name
            // replaces the action text instead of stacking on top of it. The parameter
            // column keeps showing the real value either way.
            string label = note.Length > 0 ? note : ActionCatalog.Describe(_step.Action, _step.Param);
            Theme.DrawText(g, label, note.Length > 0 ? Theme.BodyStrong : Theme.Body,
                new Rectangle(textX, bounds.Y, textWidth, bounds.Height),
                _missing ? Theme.Danger : Theme.TextPrimary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.PreserveGraphicsClipping);

            string param = _step.Param ?? "";
            if (param.Length > 0 && !macroStep)
            {
                Theme.DrawText(g, param, Theme.Caption,
                    new Rectangle(paramRect.X, bounds.Y, paramRect.Width, bounds.Height),
                    Theme.TextTertiary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.PreserveGraphicsClipping);
            }

            Theme.DrawText(g, _step.DelayMs + " ms", Theme.Caption, delayRect, Theme.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, "×" + Math.Max(1, _step.Count), Theme.Caption, countRect, Theme.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }
}
