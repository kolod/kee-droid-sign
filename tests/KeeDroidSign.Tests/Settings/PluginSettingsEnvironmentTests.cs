using System;
using KeeDroidSign.Settings;
using Xunit;

namespace KeeDroidSign.Tests.Settings
{
    /// <summary>Default GitHub environment: "release" for new installations, repository level after an upgrade.</summary>
    public class PluginSettingsEnvironmentTests
    {
        [Fact]
        public void NewInstallation_DefaultsToRelease()
        {
            PluginSettings s = PluginSettings.Load(new PluginSettingsTests.DictionaryStore());

            Assert.Equal("release", s.DefaultEnvironment);
        }

        [Fact]
        public void UpgradeFrom10x_KeepsRepositoryLevel()
        {
            var store = new PluginSettingsTests.DictionaryStore();
            store.Set("RootGroup", "DroidSign");

            Assert.Equal(string.Empty, PluginSettings.Load(store).DefaultEnvironment);
        }

        [Theory]
        [InlineData("staging")]
        [InlineData("")]
        public void SavedValue_RoundTrips(string value)
        {
            var store = new PluginSettingsTests.DictionaryStore();
            PluginSettings s = PluginSettings.Load(store);
            s.DefaultEnvironment = value;
            s.Save(store);

            Assert.Equal(value, PluginSettings.Load(store).DefaultEnvironment);
            Assert.Equal(value, store.Get("GitHubEnvironment"));
        }

        [Fact]
        public void NewSettingsObject_IsRepositoryLevel()
        {
            Assert.Equal(string.Empty, new PluginSettings().DefaultEnvironment);
        }

        [Theory]
        [InlineData("prod/eu")]
        [InlineData(" release")]
        public void Validate_RejectsInvalidEnvironment(string value)
        {
            var s = new PluginSettings { DefaultEnvironment = value };

            var ex = Assert.Throws<ArgumentException>(() => s.Validate());
            Assert.Contains("GitHub environment", ex.Message);
        }

        [Fact]
        public void Validate_AcceptsEmptyAndValidEnvironment()
        {
            new PluginSettings { DefaultEnvironment = string.Empty }.Validate();
            new PluginSettings { DefaultEnvironment = "release" }.Validate();
        }
    }
}
