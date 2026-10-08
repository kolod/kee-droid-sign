using System;
using System.Collections.Generic;
using System.Linq;
using KeeDroidSign.Core.Passwords;
using Xunit;

namespace KeeDroidSign.Core.Tests.Passwords
{
    public class PasswordGeneratorTests
    {
        private const string Forbidden = "$`\\\"' \t\r\n";

        private readonly PasswordGenerator _generator = new PasswordGenerator();

        [Theory]
        [InlineData(8)]
        [InlineData(32)]
        [InlineData(128)]
        public void Generate_ReturnsExactLength(int length)
        {
            string password = _generator.Generate(new PasswordPolicy { Length = length });

            Assert.Equal(length, password.Length);
        }

        [Fact]
        public void Generate_LettersAndDigitsOnly_ContainsNoSymbols()
        {
            var policy = new PasswordPolicy { Length = 40, Symbols = false };

            for (int i = 0; i < 200; i++)
            {
                string password = _generator.Generate(policy);
                Assert.Equal(40, password.Length);
                Assert.All(password, c => Assert.True(IsAsciiLetterOrDigit(c), $"Unexpected character code {(int)c}"));
            }
        }

        [Fact]
        public void Generate_ContainsEveryEnabledClass()
        {
            var policy = new PasswordPolicy { Length = 8 };

            for (int i = 0; i < 1000; i++)
            {
                string password = _generator.Generate(policy);
                Assert.Contains(password, c => c >= 'A' && c <= 'Z');
                Assert.Contains(password, c => c >= 'a' && c <= 'z');
                Assert.Contains(password, c => c >= '0' && c <= '9');
                Assert.Contains(password, c => PasswordGenerator.SafeSymbols.IndexOf(c) >= 0);
            }
        }

        [Fact]
        public void Generate_NeverProducesShellOrPropertiesUnsafeCharacters()
        {
            var policy = new PasswordPolicy();

            for (int i = 0; i < 10000; i++)
            {
                string password = _generator.Generate(policy);
                Assert.True(password.IndexOfAny(Forbidden.ToCharArray()) < 0, "Forbidden character generated");
                Assert.All(password, c => Assert.True(
                    IsAsciiLetterOrDigit(c) || PasswordGenerator.SafeSymbols.IndexOf(c) >= 0,
                    $"Unexpected character code {(int)c}"));
            }
        }

        [Fact]
        public void Generate_ExcludeAmbiguous_NeverProducesAmbiguousCharacters()
        {
            var policy = new PasswordPolicy { ExcludeAmbiguous = true };

            for (int i = 0; i < 2000; i++)
            {
                string password = _generator.Generate(policy);
                Assert.True(password.IndexOfAny("0O1lI".ToCharArray()) < 0, "Ambiguous character generated");
            }
        }

        [Fact]
        public void Generate_ProducesDistinctPasswords()
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < 1000; i++)
                Assert.True(seen.Add(_generator.Generate(new PasswordPolicy())), "Duplicate password");
        }

        [Fact]
        public void Generate_UnsatisfiablePolicy_Throws()
        {
            var policy = new PasswordPolicy { Uppercase = false, Lowercase = false, Digits = false, Symbols = false };

            Assert.Throws<ArgumentException>(() => _generator.Generate(policy));
        }

        [Fact]
        public void Generate_NullPolicy_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _generator.Generate(null));
        }

        [Fact]
        public void Generate_DistributionCoversAllAllowedCharacters()
        {
            var policy = new PasswordPolicy { Length = 128 };
            var counts = new Dictionary<char, int>();

            for (int i = 0; i < 200; i++)
                foreach (char c in _generator.Generate(policy))
                    counts[c] = counts.TryGetValue(c, out int n) ? n + 1 : 1;

            // 26 + 26 + 10 + 15 = 77 characters; 25,600 draws must hit every one of them.
            Assert.Equal(77, counts.Count);
            Assert.True(counts.Values.Min() > 100, "A character is drawn suspiciously rarely");
        }

        private static bool IsAsciiLetterOrDigit(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
    }
}
