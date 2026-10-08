using System.Threading;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Storage;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace KeeDroidSign.Tests.Support
{
    /// <summary>In-memory KeePass databases and hand-built app groups for tests (no files written).</summary>
    internal static class TestDatabase
    {
        public const string StorePassword = "test-store-pass";
        public const string KeyPassword = "test-key-pass";

        public static PwDatabase Create()
        {
            var pd = new PwDatabase();
            pd.New(IOConnectionInfo.FromPath("test.kdbx"), new CompositeKey());
            pd.Modified = false;
            return pd;
        }

        public static SigningKeystore Keystore(string alias = "1")
        {
            return new KeystoreGenerator().GenerateAsync(new KeyRequest
            {
                Alias = alias,
                Subject = new DistinguishedName { CommonName = "Test App", Country = "UA" },
                KeySize = 2048,
                StorePassword = StorePassword,
                KeyPassword = KeyPassword,
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>Builds the documented layout by hand, independent of DroidSignStore.CreateApp.</summary>
        public static PwGroup AddApp(PwDatabase pd, string root, string packageId, SigningKeystore keystore,
            string repoUrl = "https://github.com/octo/app", params int[] keyNumbers)
        {
            PwGroup app = pd.RootGroup.FindCreateGroup(root, true).FindCreateGroup(packageId, true);

            var ks = new PwEntry(true, true);
            ks.Strings.Set(PwDefs.TitleField, new ProtectedString(false, "Test App"));
            ks.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, StorePassword));
            if (repoUrl != null)
                ks.Strings.Set(PwDefs.UrlField, new ProtectedString(false, repoUrl));
            ks.Strings.Set(EntryFields.Role, new ProtectedString(false, EntryFields.RoleKeystore));
            if (keystore != null)
                ks.Binaries.Set(packageId + ".jks", new ProtectedBinary(true, keystore.Content));
            app.AddEntry(ks, true);

            foreach (int n in keyNumbers.Length == 0 ? new[] { 1 } : keyNumbers)
                AddKeyEntry(app, n);
            return app;
        }

        public static PwEntry AddKeyEntry(PwGroup app, int number, string title = null)
        {
            var key = new PwEntry(true, true);
            key.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title ?? number.ToString()));
            key.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, KeyPassword));
            key.Strings.Set(EntryFields.Role, new ProtectedString(false, EntryFields.RoleKey));
            key.Strings.Set(EntryFields.KeyNumber, new ProtectedString(false, number.ToString()));
            app.AddEntry(key, true);
            return key;
        }
    }
}
