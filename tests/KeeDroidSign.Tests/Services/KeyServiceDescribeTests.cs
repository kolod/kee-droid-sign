using System;
using System.Linq;
using KeeDroidSign.Core;
using KeeDroidSign.Core.Fingerprints;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Services;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    public class KeyServiceDescribeTests
    {
        private static (KeyContext Key, SigningKeystore Keystore) Setup()
        {
            var pd = TestDatabase.Create();
            SigningKeystore ks = TestDatabase.Keystore();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", ks);
            var store = new DroidSignStore(pd, KeyServiceCreateTests.FastSettings());
            return (store.ResolveKey(group.Entries.First(DroidSignStore.IsKeyEntry)), ks);
        }

        [Fact]
        public void Describe_ReturnsCertificateDetailsAndFingerprints()
        {
            var (key, ks) = Setup();

            KeyDetails details = KeyService.Describe(key);

            Assert.Equal("1", details.Alias);
            Assert.Equal("CN=Test App, C=UA", details.Subject);
            Assert.Equal(ks.NotBefore, details.NotBefore);
            Assert.Equal(ks.NotAfter, details.NotAfter);
            Assert.Equal(CertificateFingerprint.Compute(ks.CertificateDer), details.Sha256);
        }

        [Fact]
        public void Describe_WrongStorePassword_Throws()
        {
            var (key, _) = Setup();
            key.App.KeystoreEntry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "wrong-password"));

            Assert.Throws<InvalidKeystorePasswordException>(() => KeyService.Describe(key));
        }

        [Fact]
        public void Describe_AliasNotInKeystore_Throws()
        {
            var (key, _) = Setup();
            PwEntry second = TestDatabase.AddKeyEntry(key.App.Group, 2);
            var store = new DroidSignStore(new PwDatabase(), KeyServiceCreateTests.FastSettings());

            KeyContext missing = store.ResolveKey(second);

            Assert.Throws<AliasNotFoundException>(() => KeyService.Describe(missing));
        }

        [Fact]
        public void Describe_WithProblems_Throws()
        {
            var pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", null);
            KeyContext key = new DroidSignStore(pd, KeyServiceCreateTests.FastSettings())
                .ResolveKey(group.Entries.First(DroidSignStore.IsKeyEntry));

            Assert.Throws<InvalidOperationException>(() => KeyService.Describe(key));
        }
    }
}
