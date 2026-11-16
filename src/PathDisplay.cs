using System;
using System.IO;

namespace CapsLockLite
{
    internal static class PathDisplay
    {
        public static string Friendly(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData)
                && path.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
                return "%APPDATA%" + path.Substring(appData.Length);
            return path;
        }

        public static string Shorten(string path, int maxLength)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= maxLength) return path;
            string file = Path.GetFileName(path);
            string directory = Path.GetDirectoryName(path);
            string tail = file;
            if (!string.IsNullOrEmpty(directory))
            {
                string leaf = Path.GetFileName(directory);
                if (!string.IsNullOrEmpty(leaf)) tail = leaf + "\\" + file;
            }
            string root = Path.GetPathRoot(path);
            string candidate = (root ?? "") + "…\\" + tail;
            if (candidate.Length <= maxLength) return candidate;
            return "…\\" + tail;
        }
    }
}
