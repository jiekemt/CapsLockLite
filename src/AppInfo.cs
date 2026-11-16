namespace CapsLockLite
{
    /// <summary>
    /// Single source for the version the user sees. Two places cannot read this
    /// constant and have to be bumped together with it: the assembly version in
    /// AssemblyInfo.cs (a literal attribute) and the -Version default in
    /// package.ps1 (the package file names).
    /// </summary>
    internal static class AppInfo
    {
        public const string Version = "1.21.4";

        /// <summary>Sidebar / title bar style: "v1.21.4".</summary>
        public static string VersionText
        {
            get { return "v" + Version; }
        }

        /// <summary>Sentence style used on the about page: "版本 1.21.4".</summary>
        public static string AboutVersion
        {
            get { return "版本 " + Version; }
        }
    }
}
