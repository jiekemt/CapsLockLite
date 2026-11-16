using System;
using System.Collections.Generic;

namespace CapsLockLite
{
    /// <summary>
    /// Finds mappings that can never fire because an earlier enabled mapping
    /// already owns the same trigger.
    ///
    /// The Caps layer resolves a chord by taking the first match in list order
    /// (HotkeyManager.FindCapsMapping) and the OS refuses to register a second
    /// global hotkey that another mapping already took. Either way the later
    /// duplicate stays "enabled" in the list while doing nothing, which is the
    /// silent-shadowing bug this type exists to expose.
    /// </summary>
    internal static class MappingConflicts
    {
        private const int KeyMask = 0xFFFF;

        /// <summary>
        /// Scope plus modifiers plus main key. Two mappings with the same
        /// signature compete for one trigger; different scopes (Caps layer vs
        /// global) never collide.
        /// </summary>
        public static string Signature(KeyMapping mapping)
        {
            if (mapping == null) return null;
            return (mapping.Caps ? "1" : "0")
                + (mapping.Ctrl ? "1" : "0")
                + (mapping.Alt ? "1" : "0")
                + (mapping.Shift ? "1" : "0")
                + (mapping.Win ? "1" : "0")
                + ":" + (mapping.Key & KeyMask);
        }

        /// <summary>
        /// Ids of enabled mappings that repeat a signature seen earlier in the
        /// list. Disabled mappings take part in no trigger, so they are skipped
        /// and never mark anything as shadowed.
        /// </summary>
        public static HashSet<string> FindShadowed(IEnumerable<KeyMapping> mappings)
        {
            HashSet<string> shadowed = new HashSet<string>(StringComparer.Ordinal);
            if (mappings == null) return shadowed;

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyMapping mapping in mappings)
            {
                if (mapping == null || !mapping.Enabled) continue;
                // No main key means the row can never be triggered, so it does not
                // compete with anything (and two of them must not be flagged as a
                // duplicate pair).
                if (mapping.Key == 0) continue;
                string signature = Signature(mapping);
                if (signature == null) continue;
                if (!seen.Add(signature) && !string.IsNullOrEmpty(mapping.Id))
                    shadowed.Add(mapping.Id);
            }
            return shadowed;
        }
    }
}
