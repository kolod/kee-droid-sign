namespace KeeDroidSign.Storage
{
    /// <summary>Custom field names and values that mark the plugin's entries (spec FR-004).</summary>
    public static class EntryFields
    {
        /// <summary>
        /// Marks keystore and key entries. A key entry's title is its key number, which is also the
        /// alias inside the keystore. (Plugin 1.0.0 also wrote "DroidSign.KeyNumber"; it is ignored.)
        /// </summary>
        public const string Role = "DroidSign.Role";

        public const string RoleKeystore = "keystore";
        public const string RoleKey = "key";

        /// <summary>
        /// Optional per-app export target on the keystore entry: absent = use the default from the
        /// settings, <see cref="ExportTargetRepository"/>, or <see cref="ExportTargetEnvironmentPrefix"/> + name.
        /// </summary>
        public const string ExportTarget = "DroidSign.ExportTarget";

        public const string ExportTargetRepository = "repository";
        public const string ExportTargetEnvironmentPrefix = "environment:";

        public const string KeystoreExtension = ".jks";

        public static string KeystoreFileName(string packageId)
        {
            return packageId + KeystoreExtension;
        }
    }
}
