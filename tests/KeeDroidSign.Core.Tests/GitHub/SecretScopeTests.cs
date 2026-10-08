using System;
using KeeDroidSign.Core.GitHub;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class SecretScopeTests
    {
        private static readonly RepositoryTarget Repo = RepositoryTarget.Parse("octo/app");

        [Theory]
        [InlineData("release")]
        [InlineData("Production EU")]
        [InlineData("staging-2_a.b")]
        public void ValidNames_AreAccepted(string name)
        {
            Assert.True(EnvironmentNames.IsValid(name));
            Assert.Equal(name, new SecretScope(Repo, name).Environment);
        }

        [Theory]
        [InlineData("")]
        [InlineData("prod/eu")]
        [InlineData(" release")]
        [InlineData("release ")]
        [InlineData("rel\tease")]
        public void InvalidNames_AreRejected(string name)
        {
            Assert.False(EnvironmentNames.IsValid(name));
            Assert.Throws<ArgumentException>(() => new SecretScope(Repo, name));
        }

        [Fact]
        public void TooLongName_IsRejected()
        {
            Assert.True(EnvironmentNames.IsValid(new string('a', 255)));
            Assert.False(EnvironmentNames.IsValid(new string('a', 256)));
        }

        [Fact]
        public void RepositoryScope_UsesActionsSecretsPath()
        {
            SecretScope scope = SecretScope.ForRepository(Repo);

            Assert.False(scope.IsEnvironment);
            Assert.Equal("repos/octo/app/actions/secrets", scope.SecretsPath);
            Assert.Equal("octo/app", scope.ToString());
        }

        [Fact]
        public void EnvironmentScope_UsesEscapedEnvironmentPath()
        {
            var scope = new SecretScope(Repo, "my env");

            Assert.True(scope.IsEnvironment);
            Assert.Equal("repos/octo/app/environments/my%20env/secrets", scope.SecretsPath);
            Assert.Equal("octo/app, environment my env", scope.ToString());
        }
    }
}
