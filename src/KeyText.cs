using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal static class KeyText
    {
        public static string FormatKey(Keys key)
        {
            key = key & Keys.KeyCode;
            switch (key)
            {
                case Keys.D0: return "0";
                case Keys.D1: return "1";
                case Keys.D2: return "2";
                case Keys.D3: return "3";
                case Keys.D4: return "4";
                case Keys.D5: return "5";
                case Keys.D6: return "6";
                case Keys.D7: return "7";
                case Keys.D8: return "8";
                case Keys.D9: return "9";
                case Keys.NumPad0: return "Num 0";
                case Keys.NumPad1: return "Num 1";
                case Keys.NumPad2: return "Num 2";
                case Keys.NumPad3: return "Num 3";
                case Keys.NumPad4: return "Num 4";
                case Keys.NumPad5: return "Num 5";
                case Keys.NumPad6: return "Num 6";
                case Keys.NumPad7: return "Num 7";
                case Keys.NumPad8: return "Num 8";
                case Keys.NumPad9: return "Num 9";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemBackslash: return "\\";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemtilde: return "`";
                case Keys.Back: return "Backspace";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.LWin: return "Win";
                case Keys.RWin: return "Win";
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey: return "Ctrl";
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu: return "Alt";
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey: return "Shift";
                case Keys.MediaNextTrack: return "下一曲";
                case Keys.MediaPreviousTrack: return "上一曲";
                case Keys.MediaPlayPause: return "播放/暂停";
                case Keys.VolumeUp: return "音量+";
                case Keys.VolumeDown: return "音量−";
                case Keys.VolumeMute: return "静音";
                // Mouse triggers are usable both as the main key of a shortcut and,
                // since 1.25.2, as the key a "发送按键" action sends; the labels are
                // parsed back in ParseKeyName.
                case (Keys)MouseKeys.Left: return "鼠标左键";
                case (Keys)MouseKeys.Right: return "鼠标右键";
                case (Keys)MouseKeys.Middle: return "鼠标中键";
                case (Keys)MouseKeys.WheelUp: return "滚轮上滑";
                case (Keys)MouseKeys.WheelDown: return "滚轮下滑";
                case Keys.None: return "未设置";
            }
            if (key >= Keys.A && key <= Keys.Z)
                return key.ToString();
            if (key >= Keys.F1 && key <= Keys.F24)
                return key.ToString();
            return key.ToString();
        }

        public static string FormatModifiers(bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            List<string> parts = new List<string>();
            if (caps) parts.Add("Caps");
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            return string.Join(" + ", parts.ToArray());
        }

        public static string FormatTrigger(KeyMapping mapping)
        {
            string mods = FormatModifiers(mapping.Caps, mapping.Ctrl, mapping.Alt, mapping.Shift, mapping.Win);
            string key = FormatKey((Keys)mapping.Key);
            if (mods.Length == 0)
                return key;
            return mods + " + " + key;
        }

        public static string FormatCombo(bool ctrl, bool alt, bool shift, bool win, Keys key)
        {
            string mods = FormatModifiers(false, ctrl, alt, shift, win);
            string name = FormatKey(key);
            if (mods.Length == 0)
                return name;
            return mods + " + " + name;
        }

        public static bool IsModifier(Keys key)
        {
            key = key & Keys.KeyCode;
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu
                || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.LWin || key == Keys.RWin;
        }

        public static bool IsPlainTypingKey(Keys key)
        {
            key = key & Keys.KeyCode;
            if (key >= Keys.A && key <= Keys.Z) return true;
            if (key >= Keys.D0 && key <= Keys.D9) return true;
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return true;
            switch (key)
            {
                case Keys.Space:
                case Keys.OemSemicolon:
                case Keys.OemQuotes:
                case Keys.Oemcomma:
                case Keys.OemPeriod:
                case Keys.OemQuestion:
                case Keys.OemOpenBrackets:
                case Keys.OemCloseBrackets:
                case Keys.OemPipe:
                case Keys.OemBackslash:
                case Keys.OemMinus:
                case Keys.Oemplus:
                case Keys.Oemtilde:
                    return true;
            }
            return false;
        }

        public static string BuildComboParam(bool ctrl, bool alt, bool shift, bool win, Keys key)
        {
            return FormatCombo(ctrl, alt, shift, win, key);
        }

        public static bool TryParseCombo(string text, out bool ctrl, out bool alt, out bool shift, out bool win, out Keys key)
        {
            ctrl = alt = shift = win = false;
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string source = text.Trim();
            string keyPart;
            string modPart;
            int lastPlus = source.LastIndexOf('+');
            if (lastPlus == source.Length - 1)
            {
                keyPart = "+";
                modPart = source.Substring(0, lastPlus).TrimEnd('+');
            }
            else if (lastPlus > 0 && source.Substring(lastPlus + 1).Trim().Length == 0)
            {
                keyPart = "+";
                modPart = source.Substring(0, lastPlus);
            }
            else if (lastPlus >= 0)
            {
                keyPart = source.Substring(lastPlus + 1).Trim();
                modPart = source.Substring(0, lastPlus);
            }
            else
            {
                keyPart = source;
                modPart = "";
            }

            if (modPart.Length > 0)
            {
                string[] pieces = modPart.Split('+');
                foreach (string raw in pieces)
                {
                    string p = raw.Trim();
                    if (p.Length == 0) continue;
                    if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                    else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                    else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                    else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase)) win = true;
                    else return false;
                }
            }

            key = ParseKeyName(keyPart);
            return key != Keys.None;
        }

        public static Keys ParseKeyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Keys.None;
            string s = name.Trim();
            if (s.Length == 1)
            {
                char c = char.ToUpperInvariant(s[0]);
                if (c >= 'A' && c <= 'Z') return (Keys)(int)c;
                if (c >= '0' && c <= '9') return (Keys)((int)Keys.D0 + (c - '0'));
                switch (c)
                {
                    case ';': return Keys.OemSemicolon;
                    case '\'': return Keys.OemQuotes;
                    case ',': return Keys.Oemcomma;
                    case '.': return Keys.OemPeriod;
                    case '/': return Keys.OemQuestion;
                    case '[': return Keys.OemOpenBrackets;
                    case ']': return Keys.OemCloseBrackets;
                    case '\\': return Keys.OemPipe;
                    case '-': return Keys.OemMinus;
                    case '=': return Keys.Oemplus;
                    case '`': return Keys.Oemtilde;
                }
            }
            if (s.Equals("+", StringComparison.OrdinalIgnoreCase) || s.Equals("Plus", StringComparison.OrdinalIgnoreCase)) return Keys.Oemplus;
            // The five mouse triggers are legal keys to send: "发送按键" can click or
            // scroll, with or without Ctrl/Alt/Shift/Win held around it.
            Keys mouse;
            if (TryParseMouseKey(s, out mouse)) return mouse;
            // Arrow keys are shown as glyphs in the editor and stored that way in
            // the parameter string, so the parser has to accept both spellings.
            if (s == "↑") return Keys.Up;
            if (s == "↓") return Keys.Down;
            if (s == "←") return Keys.Left;
            if (s == "→") return Keys.Right;
            // Numpad digits are shown as "Num 7" in the editor and stored that way
            // in the parameter string; Enum.Parse only knows "NumPad7", so without
            // this branch a captured numpad key was rejected as an invalid combo.
            Keys numpad;
            if (TryParseNumpad(s, out numpad)) return numpad;
            switch (s.ToUpperInvariant())
            {
                case "UP": return Keys.Up;
                case "DOWN": return Keys.Down;
                case "LEFT": return Keys.Left;
                case "RIGHT": return Keys.Right;
                case "HOME": return Keys.Home;
                case "END": return Keys.End;
                case "PAGEUP": case "PGUP": return Keys.PageUp;
                case "PAGEDOWN": case "PGDN": return Keys.PageDown;
                case "INSERT": case "INS": return Keys.Insert;
                case "DELETE": case "DEL": return Keys.Delete;
                case "BACKSPACE": case "BACK": return Keys.Back;
                case "ENTER": case "RETURN": return Keys.Return;
                case "ESC": case "ESCAPE": return Keys.Escape;
                case "SPACE": return Keys.Space;
                case "TAB": return Keys.Tab;
            }
            try
            {
                return (Keys)Enum.Parse(typeof(Keys), s, true);
            }
            catch
            {
                return Keys.None;
            }
        }

        /// <summary>
        /// The five mouse spellings, both the label <see cref="FormatKey"/> writes and
        /// the terse English form a hand-edited config or a macro file may use.
        /// </summary>
        private static bool TryParseMouseKey(string text, out Keys key)
        {
            key = Keys.None;
            if (string.IsNullOrEmpty(text)) return false;
            string flat = text.Replace(" ", "").Replace("_", string.Empty).ToUpperInvariant();
            switch (flat)
            {
                case "鼠标左键":
                case "鼠标左":
                case "MOUSELEFT":
                case "MOUSEL":
                    key = (Keys)MouseKeys.Left;
                    return true;
                case "鼠标右键":
                case "鼠标右":
                case "MOUSERIGHT":
                case "MOUSER":
                    key = (Keys)MouseKeys.Right;
                    return true;
                case "鼠标中键":
                case "鼠标中":
                case "MOUSEMIDDLE":
                case "MOUSEM":
                    key = (Keys)MouseKeys.Middle;
                    return true;
                case "滚轮上滑":
                case "滚轮上":
                case "WHEELUP":
                case "MOUSEWHEELUP":
                    key = (Keys)MouseKeys.WheelUp;
                    return true;
                case "滚轮下滑":
                case "滚轮下":
                case "WHEELDOWN":
                case "MOUSEWHEELDOWN":
                    key = (Keys)MouseKeys.WheelDown;
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Accepts the spellings that can appear for a numpad key: "Num 7"
        /// (what <see cref="FormatKey"/> writes), "Num7", "NumPad7", "Numpad 7"
        /// and the Chinese label "小键盘 7".
        /// </summary>
        private static bool TryParseNumpad(string text, out Keys key)
        {
            key = Keys.None;
            if (string.IsNullOrEmpty(text)) return false;
            string flat = text.Replace(" ", "").Replace("_", string.Empty);
            string prefix;
            if (flat.Length >= 2 && flat.StartsWith("小键盘", StringComparison.Ordinal))
            {
                prefix = "小键盘";
            }
            else if (flat.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "NumPad";
            }
            else if (flat.StartsWith("Num", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "Num";
            }
            else
            {
                return false;
            }
            string digits = flat.Substring(prefix.Length);
            if (digits.Length != 1 || digits[0] < '0' || digits[0] > '9') return false;
            key = (Keys)((int)Keys.NumPad0 + (digits[0] - '0'));
            return true;
        }

        public static string EscapeForJavaScript(string text)
        {
            if (text == null) return "";
            StringBuilder sb = new StringBuilder(text.Length + 16);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\'': sb.Append("\\'"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
