namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Names of the GitHub Actions secrets written by the export. The defaults match the reference
    /// workflow (decode keystore + write keystore.properties).
    /// </summary>
    public sealed class SecretMapping
    {
        public string KeystoreBase64Name { get; set; } = "ANDROID_KEYSTORE_BASE64";
        public string StorePasswordName { get; set; } = "ANDROID_KEYSTORE_PASSWORD";
        public string KeyAliasName { get; set; } = "ANDROID_KEY_ALIAS";
        public string KeyPasswordName { get; set; } = "ANDROID_KEY_PASSWORD";

        internal string[] AllNames() => new[] { KeystoreBase64Name, StorePasswordName, KeyAliasName, KeyPasswordName };
    }
}
