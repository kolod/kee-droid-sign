using System;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Settings;
using Xunit;

namespace KeeDroidSign.Tests.Settings
{
    /// <summary>Default certificate owner and GitHub owner offered in the new-key window.</summary>
    public class PluginSettingsDefaultsTests
    {
        private static PluginSettings Load() => PluginSettings.Load(new PluginSettingsTests.DictionaryStore());

        [Fact]
        public void Defaults_AreEmpty()
        {
            PluginSettings s = Load();

            Assert.Equal(string.Empty, s.DefaultCommonName);
            Assert.Equal(string.Empty, s.DefaultOrganizationalUnit);
            Assert.Equal(string.Empty, s.DefaultOrganization);
            Assert.Equal(string.Empty, s.DefaultLocality);
            Assert.Equal(string.Empty, s.DefaultState);
            Assert.Equal(string.Empty, s.DefaultCountry);
            Assert.Equal(string.Empty, s.DefaultGitHubOwner);
            Assert.Equal("https://github.com/", s.RepositoryPrefill);
        }

        [Fact]
        public void SaveAndLoad_RoundTrips()
        {
            var store = new PluginSettingsTests.DictionaryStore();
            PluginSettings s = PluginSettings.Load(store);
            s.DefaultCommonName = "Oleksandr Kolodkin";
            s.DefaultOrganizationalUnit = "Mobile";
            s.DefaultOrganization = "Kolod";
            s.DefaultLocality = "Kyiv";
            s.DefaultState = "Kyiv";
            s.DefaultCountry = "UA";
            s.DefaultGitHubOwner = "kolod";

            s.Save(store);
            PluginSettings loaded = PluginSettings.Load(store);

            Assert.Equal("Oleksandr Kolodkin", loaded.DefaultCommonName);
            Assert.Equal("Mobile", loaded.DefaultOrganizationalUnit);
            Assert.Equal("Kolod", loaded.DefaultOrganization);
            Assert.Equal("Kyiv", loaded.DefaultLocality);
            Assert.Equal("Kyiv", loaded.DefaultState);
            Assert.Equal("UA", loaded.DefaultCountry);
            Assert.Equal("kolod", loaded.DefaultGitHubOwner);
            Assert.Equal("https://github.com/kolod/", loaded.RepositoryPrefill);
        }

        [Fact]
        public void SaveAfterKeyChange_IsOnByDefaultAndRoundTrips()
        {
            var store = new PluginSettingsTests.DictionaryStore();
            PluginSettings s = PluginSettings.Load(store);
            Assert.True(s.SaveAfterKeyChange);

            s.SaveAfterKeyChange = false;
            s.Save(store);

            Assert.False(PluginSettings.Load(store).SaveAfterKeyChange);
            Assert.Equal("false", store.Get("SaveAfterKeyChange"));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("kolod", "io.github.kolod.")]
        [InlineData("Kolod", "io.github.kolod.")]
        [InlineData("my-org", "io.github.my_org.")]
        [InlineData("42team", "io.github.")]
        public void PackageIdPrefill_UsesGitHubOwner(string owner, string expected)
        {
            PluginSettings s = Load();
            s.DefaultGitHubOwner = owner;

            Assert.Equal(expected, s.PackageIdPrefill);
        }

        [Theory]
        [InlineData("kolod")]
        [InlineData("my-org")]
        [InlineData("A-B-C")]
        public void PackageIdPrefill_PlusAppName_IsValidPackageId(string owner)
        {
            PluginSettings s = Load();
            s.DefaultGitHubOwner = owner;

            Assert.True(KeeDroidSign.Storage.DroidSignStore.IsValidPackageId(s.PackageIdPrefill + "app"));
        }

        [Theory]
        [InlineData("U")]
        [InlineData("UKR")]
        [InlineData("1A")]
        public void Validate_RejectsInvalidCountry(string country)
        {
            PluginSettings s = Load();
            s.DefaultCountry = country;

            var ex = Assert.Throws<ArgumentException>(() => s.Validate());
            Assert.Contains("country", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("bad owner")]
        [InlineData("under_score")]
        [InlineData("https://github.com/kolod")]
        public void Validate_RejectsInvalidGitHubOwner(string owner)
        {
            PluginSettings s = Load();
            s.DefaultGitHubOwner = owner;

            var ex = Assert.Throws<ArgumentException>(() => s.Validate());
            Assert.Contains("GitHub owner", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Validate_RejectsTooLongOwnerFields()
        {
            PluginSettings s = Load();
            s.DefaultCommonName = new string('a', 65);

            Assert.Throws<ArgumentException>(() => s.Validate());
        }

        [Fact]
        public void Validate_AcceptsLowerCaseCountryAndEmptyValues()
        {
            PluginSettings s = Load();
            s.DefaultCountry = "ua";

            s.Validate();
        }

        [Fact]
        public void DefaultSubject_UsesDisplayNameWhenNoDefaultCommonName()
        {
            PluginSettings s = Load();
            s.DefaultOrganization = "Kolod";
            s.DefaultCountry = "ua";

            DistinguishedName subject = s.DefaultSubject("Example App");

            Assert.Equal("Example App", subject.CommonName);
            Assert.Equal("Kolod", subject.Organization);
            Assert.Equal("ua", subject.Country);

            s.DefaultCommonName = "Oleksandr Kolodkin";
            Assert.Equal("Oleksandr Kolodkin", s.DefaultSubject("Example App").CommonName);
        }
    }
}
