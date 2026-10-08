using System;
using System.Linq;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Services;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using KeePassLib.Security;
using Xunit;

namespace KeeDroidSign.Tests.Storage
{
    /// <summary>Per-app export target on the keystore entry and its resolution against the default.</summary>
    public class ExportTargetTests
    {
        [Theory]
        [InlineData(null, ExportTargetKind.Default, null, false)]
        [InlineData("", ExportTargetKind.Default, null, false)]
        [InlineData("repository", ExportTargetKind.Repository, null, false)]
        [InlineData("environment:staging", ExportTargetKind.Environment, "staging", false)]
        [InlineData("environment:", ExportTargetKind.Default, null, true)]
        [InlineData("environment:prod/eu", ExportTargetKind.Default, null, true)]
        [InlineData("garbage", ExportTargetKind.Default, null, true)]
        public void Parse(string value, ExportTargetKind kind, string environment, bool invalid)
        {
            ExportTargetOverride parsed = ExportTargetOverride.Parse(value);

            Assert.Equal(kind, parsed.Kind);
            Assert.Equal(environment, parsed.Environment);
            Assert.Equal(invalid, parsed.IsInvalid);
        }

        [Fact]
        public void FieldValue_RoundTrips()
        {
            Assert.Null(ExportTargetOverride.Default.ToFieldValue());
            Assert.Equal("repository", ExportTargetOverride.Repository.ToFieldValue());
            Assert.Equal("environment:staging", ExportTargetOverride.ForEnvironment("staging").ToFieldValue());
            Assert.Throws<ArgumentException>(() => ExportTargetOverride.ForEnvironment("a/b"));
        }

        private static AppKeystore App(PwDatabase pd, PluginSettings settings) =>
            new DroidSignStore(pd, settings).FindApp("com.example.app");

        private static PwDatabase Database(string targetField = null)
        {
            PwDatabase pd = TestDatabase.Create();
            PwGroup group = TestDatabase.AddApp(pd, "DroidSign", "com.example.app", null);
            if (targetField != null)
                group.Entries.First(DroidSignStore.IsKeystoreEntry).Strings.Set(EntryFields.ExportTarget,
                    new ProtectedString(false, targetField));
            return pd;
        }

        [Theory]
        [InlineData("release", null, "release", false)]
        [InlineData("", null, null, false)]
        [InlineData("release", "repository", null, true)]
        [InlineData("", "environment:staging", "staging", true)]
        [InlineData("release", "environment:staging", "staging", true)]
        public void Resolve_PrefersAppOverride(string defaultEnvironment, string field, string expected, bool fromApp)
        {
            var settings = new PluginSettings { DefaultEnvironment = defaultEnvironment };

            ExportTarget target = ExportTarget.Resolve(App(Database(field), settings), settings);

            Assert.Equal(expected, target.Environment);
            Assert.Equal(fromApp, target.FromApp);
            Assert.Equal("octo/app", target.Repository.ToString());
            Assert.Equal(expected == null ? "octo/app" : "octo/app, environment " + expected, target.ToString());
        }

        [Fact]
        public void Resolve_InvalidField_UsesDefaultAndWarns()
        {
            var settings = new PluginSettings { DefaultEnvironment = "release" };
            AppKeystore app = App(Database("bogus"), settings);

            ExportTarget target = ExportTarget.Resolve(app, settings);

            Assert.Equal("release", target.Environment);
            Assert.False(target.FromApp);
            Assert.Contains(app.Warnings, w => w.Contains(EntryFields.ExportTarget));
        }

        [Fact]
        public void SetExportTarget_WritesAndRemovesField_AndMarksModified()
        {
            PwDatabase pd = Database();
            var settings = new PluginSettings();
            var store = new DroidSignStore(pd, settings);
            PwEntry keystoreEntry = App(pd, settings).KeystoreEntry;

            store.SetExportTarget(App(pd, settings), ExportTargetOverride.ForEnvironment("staging"));

            Assert.True(pd.Modified);
            Assert.Equal("environment:staging", keystoreEntry.Strings.ReadSafe(EntryFields.ExportTarget));
            Assert.Equal(ExportTargetKind.Environment, App(pd, settings).TargetOverride.Kind);

            pd.Modified = false;
            store.SetExportTarget(App(pd, settings), ExportTargetOverride.Default);

            Assert.True(pd.Modified);
            Assert.Null(keystoreEntry.Strings.Get(EntryFields.ExportTarget));
        }

        [Fact]
        public void SetExportTarget_Unchanged_DoesNotMarkModified()
        {
            PwDatabase pd = Database("repository");
            var settings = new PluginSettings();

            new DroidSignStore(pd, settings).SetExportTarget(App(pd, settings), ExportTargetOverride.Repository);

            Assert.False(pd.Modified);
        }
    }
}
