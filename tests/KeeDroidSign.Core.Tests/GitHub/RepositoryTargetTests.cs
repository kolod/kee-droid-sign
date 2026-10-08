using System;
using KeeDroidSign.Core.GitHub;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class RepositoryTargetTests
    {
        [Theory]
        [InlineData("octo-org/my.app_1")]
        [InlineData("https://github.com/octo-org/my.app_1")]
        [InlineData("https://github.com/octo-org/my.app_1.git")]
        [InlineData("https://github.com/octo-org/my.app_1/")]
        [InlineData("  octo-org/my.app_1  ")]
        public void Parse_AcceptsSupportedForms(string input)
        {
            RepositoryTarget target = RepositoryTarget.Parse(input);

            Assert.Equal("octo-org", target.Owner);
            Assert.Equal("my.app_1", target.Name);
            Assert.Equal("octo-org/my.app_1", target.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("owner")]
        [InlineData("/name")]
        [InlineData("owner/")]
        [InlineData("own er/name")]
        [InlineData("owner/na me")]
        [InlineData("owner/name/extra")]
        [InlineData("owner/..")]
        [InlineData("owner_x/name")]
        [InlineData("https://gitlab.com/owner/name")]
        [InlineData(null)]
        public void Parse_RejectsInvalidInput(string input)
        {
            Assert.Throws<ArgumentException>(() => RepositoryTarget.Parse(input));
            Assert.False(RepositoryTarget.TryParse(input, out _));
        }

        [Fact]
        public void Parse_RejectsTooLongOwner()
        {
            Assert.Throws<ArgumentException>(() => RepositoryTarget.Parse(new string('o', 40) + "/name"));
            RepositoryTarget.Parse(new string('o', 39) + "/name");
        }

        [Fact]
        public void Parse_RejectsTooLongName()
        {
            Assert.Throws<ArgumentException>(() => RepositoryTarget.Parse("owner/" + new string('n', 101)));
            RepositoryTarget.Parse("owner/" + new string('n', 100));
        }
    }
}
