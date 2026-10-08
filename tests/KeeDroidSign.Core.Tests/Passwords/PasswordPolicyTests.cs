using System;
using KeeDroidSign.Core.Passwords;
using Xunit;

namespace KeeDroidSign.Core.Tests.Passwords
{
    public class PasswordPolicyTests
    {
        [Fact]
        public void Defaults_MatchSpecification()
        {
            var policy = new PasswordPolicy();

            Assert.Equal(32, policy.Length);
            Assert.True(policy.Uppercase);
            Assert.True(policy.Lowercase);
            Assert.True(policy.Digits);
            Assert.True(policy.Symbols);
            Assert.False(policy.ExcludeAmbiguous);
            policy.Validate();
        }

        [Theory]
        [InlineData(7)]
        [InlineData(129)]
        public void Validate_RejectsLengthOutOfRange(int length)
        {
            var policy = new PasswordPolicy { Length = length };

            var ex = Assert.Throws<ArgumentException>(() => policy.Validate());
            Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Validate_RejectsNoCharacterClass()
        {
            var policy = new PasswordPolicy
            {
                Uppercase = false, Lowercase = false, Digits = false, Symbols = false,
            };

            Assert.Throws<ArgumentException>(() => policy.Validate());
        }

        [Fact]
        public void Validate_AcceptsBoundaryLengths()
        {
            new PasswordPolicy { Length = 8 }.Validate();
            new PasswordPolicy { Length = 128 }.Validate();
        }
    }
}
