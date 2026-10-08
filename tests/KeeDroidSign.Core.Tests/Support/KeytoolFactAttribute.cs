using System;
using System.IO;
using System.Linq;
using Xunit;

namespace KeeDroidSign.Core.Tests.Support
{
    /// <summary>
    /// A fact that runs only when a JDK keytool is available; otherwise it is reported as skipped.
    /// Lookup order: KDS_KEYTOOL, JAVA_HOME\bin\keytool.exe, PATH.
    /// </summary>
    public sealed class KeytoolFactAttribute : FactAttribute
    {
        private static readonly Lazy<string> s_keytoolPath = new Lazy<string>(Locate);

        public KeytoolFactAttribute()
        {
            if (KeytoolPath == null)
                Skip = "keytool not found (set KDS_KEYTOOL or JAVA_HOME, or add it to PATH)";
        }

        public static string KeytoolPath => s_keytoolPath.Value;

        private static string Locate()
        {
            string explicitPath = Environment.GetEnvironmentVariable("KDS_KEYTOOL");
            if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
                return explicitPath;

            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string candidate = Path.Combine(javaHome, "bin", "keytool.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            return path.Split(Path.PathSeparator)
                .Where(dir => !string.IsNullOrWhiteSpace(dir))
                .Select(dir => SafeCombine(dir.Trim('"'), "keytool.exe"))
                .FirstOrDefault(candidate => candidate != null && File.Exists(candidate));
        }

        private static string SafeCombine(string dir, string file)
        {
            try { return Path.Combine(dir, file); }
            catch (ArgumentException) { return null; }
        }
    }
}
