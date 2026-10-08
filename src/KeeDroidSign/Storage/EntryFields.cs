namespace KeeDroidSign.Storage
{
    /// <summary>Custom field names and values that mark the plugin's entries (spec FR-004).</summary>
    public static class EntryFields
    {
        public const string Role = "DroidSign.Role";
        public const string KeyNumber = "DroidSign.KeyNumber";

        public const string RoleKeystore = "keystore";
        public const string RoleKey = "key";

        public const string KeystoreExtension = ".jks";

        public static string KeystoreFileName(string packageId)
        {
            return packageId + KeystoreExtension;
        }
    }
}
