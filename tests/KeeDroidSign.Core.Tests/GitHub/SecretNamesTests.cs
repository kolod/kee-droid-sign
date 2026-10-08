using System;
using KeeDroidSign.Core.GitHub;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class SecretNamesTests
    {
        [Fact]
        public void Defaults_MatchCiWorkflowContract()
        {
            var mapping = new SecretMapping();

            Assert.Equal("ANDROID_KEYSTORE_BASE64", mapping.KeystoreBase64Name);
            Assert.Equal("ANDROID_KEYSTORE_PASSWORD", mapping.StorePasswordName);
            Assert.Equal("ANDROID_KEY_ALIAS", mapping.KeyAliasName);
            Assert.Equal("ANDROID_KEY_PASSWORD", mapping.KeyPasswordName);
            SecretNames.Validate(mapping);
        }

        [Theory]
        [InlineData("1SECRET")]
        [InlineData("MY-SECRET")]
        [InlineData("MY SECRET")]
        [InlineData("GITHUB_TOKEN_X")]
        [InlineData("github_custom")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("СЕКРЕТ")]
        public void Validate_RejectsInvalidNames(string name)
        {
            var mapping = new SecretMapping { KeyAliasName = name };

            Assert.Throws<ArgumentException>(() => SecretNames.Validate(mapping));
        }

        [Theory]
        [InlineData("_PRIVATE")]
        [InlineData("release_store_password")]
        [InlineData("A1")]
        public void Validate_AcceptsValidNames(string name)
        {
            SecretNames.Validate(new SecretMapping { KeyAliasName = name });
        }

        [Fact]
        public void Validate_RejectsDuplicatesIgnoringCase()
        {
            var mapping = new SecretMapping { KeyAliasName = "android_key_password" };

            Assert.Throws<ArgumentException>(() => SecretNames.Validate(mapping));
        }
    }
}
