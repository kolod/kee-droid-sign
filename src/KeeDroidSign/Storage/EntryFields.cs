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

        public const string KeystoreExtension = ".jks";

        public static string KeystoreFileName(string packageId)
        {
            return packageId + KeystoreExtension;
        }
    }
}
