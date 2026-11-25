using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Pseudo key codes for the five mouse triggers (left/right/middle button and
    /// wheel up/down).
    ///
    /// They live outside the virtual-key range a keyboard hook can report (0..255),
    /// so a KeyMapping that stores one of these values can never be hit by a real
    /// key event and never collides with a keyboard shortcut. They are stored in
    /// KeyMapping.Key exactly like a normal key, which keeps the config format,
    /// the conflict check and the editor unchanged.
    /// </summary>
    internal static class MouseKeys
    {
        public const int Left = 0x0E01;
        public const int Right = 0x0E02;
        public const int Middle = 0x0E03;
        public const int WheelUp = 0x0E04;
        public const int WheelDown = 0x0E05;

        public static readonly Keys[] All =
        {
            (Keys)Left,
            (Keys)Right,
            (Keys)Middle,
            (Keys)WheelUp,
            (Keys)WheelDown
        };

        public static bool IsMouseKey(Keys key)
        {
            key = key & Keys.KeyCode;
            return key == (Keys)Left
                || key == (Keys)Right
                || key == (Keys)Middle
                || key == (Keys)WheelUp
                || key == (Keys)WheelDown;
        }

        /// <summary>
        /// A wheel notch has no key-up, so it can only fire once per notch and has
        /// no meaningful "hold to repeat" state.
        /// </summary>
        public static bool IsWheel(Keys key)
        {
            key = key & Keys.KeyCode;
            return key == (Keys)WheelUp || key == (Keys)WheelDown;
        }
    }
}
