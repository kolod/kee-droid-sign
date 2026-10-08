using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Services;
using Xunit;

namespace KeeDroidSign.Tests.Services
{
    /// <summary>The export confirmation always names the target repository and every secret.</summary>
    public class ExportConfirmationTests
    {
        [Fact]
        public void NewSecretsOnly_NamesRepositoryAndSecrets()
        {
            var plan = new ExportPlan(new[] { "ANDROID_KEYSTORE_BASE64", "ANDROID_KEY_ALIAS" }, new string[0]);

            string text = ExportConfirmation.Build("kolod/whiskergrid", plan);

            Assert.Contains("kolod/whiskergrid", text);
            Assert.Contains("ANDROID_KEYSTORE_BASE64", text);
            Assert.Contains("ANDROID_KEY_ALIAS", text);
            Assert.DoesNotContain("overwritten", text);
        }

        [Fact]
        public void WithConflicts_ListsSecretsToOverwriteSeparately()
        {
            var plan = new ExportPlan(new[] { "ANDROID_KEY_ALIAS" }, new[] { "ANDROID_KEY_PASSWORD" });

            string text = ExportConfirmation.Build("kolod/whiskergrid", plan);

            Assert.Contains("kolod/whiskergrid", text);
            Assert.Contains("overwritten", text);
            Assert.True(text.IndexOf("ANDROID_KEY_PASSWORD") > text.IndexOf("overwritten"));
        }
    }
}
