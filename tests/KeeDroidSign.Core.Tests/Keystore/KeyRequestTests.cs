using System;
using KeeDroidSign.Core.Keystore;
using Xunit;

namespace KeeDroidSign.Core.Tests.Keystore
{
    public class KeyRequestTests
    {
        internal static KeyRequest Valid() => new KeyRequest
        {
            Alias = "upload",
            Subject = new DistinguishedName { CommonName = "Jane Doe", Organization = "Acme", Country = "UA" },
            StorePassword = "test-store-pass",
            KeyPassword = "test-key-pass",
        };

        [Fact]
        public void Defaults_MatchSpecification()
        {
            var request = new KeyRequest();

            Assert.Equal(4096, request.KeySize);
            Assert.Equal(30, request.ValidityYears);
        }

        [Fact]
        public void Validate_AcceptsValidRequest()
        {
            Valid().Validate();
        }

        [Theory]
        [InlineData(1024)]
        [InlineData(2047)]
        [InlineData(8192)]
        public void Validate_RejectsUnsupportedKeySize(int size)
        {
            var request = Valid();
            request.KeySize = size;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Theory]
        [InlineData(2048)]
        [InlineData(3072)]
        [InlineData(4096)]
        public void Validate_AcceptsSupportedKeySize(int size)
        {
            var request = Valid();
            request.KeySize = size;

            request.Validate();
        }

        [Theory]
        [InlineData(24)]
        [InlineData(101)]
        public void Validate_RejectsValidityOutOfRange(int years)
        {
            var request = Valid();
            request.ValidityYears = years;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Theory]
        [InlineData("")]
        [InlineData("with space")]
        [InlineData("ключ")]
        [InlineData("a/b")]
        [InlineData(null)]
        public void Validate_RejectsInvalidAlias(string alias)
        {
            var request = Valid();
            request.Alias = alias;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Fact]
        public void Validate_RejectsTooLongAlias()
        {
            var request = Valid();
            request.Alias = new string('a', 65);

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("abc\u0001def")]
        [InlineData(null)]
        public void Validate_RejectsBadStorePassword(string password)
        {
            var request = Valid();
            request.StorePassword = password;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("abc\ndef")]
        [InlineData(null)]
        public void Validate_RejectsBadKeyPassword(string password)
        {
            var request = Valid();
            request.KeyPassword = password;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Fact]
        public void Validate_RequiresSubject()
        {
            var request = Valid();
            request.Subject = null;

            Assert.Throws<ArgumentException>(() => request.Validate());
        }

        [Fact]
        public void Validate_ErrorMessagesDoNotContainPasswords()
        {
            var request = Valid();
            request.StorePassword = "pw-x1";

            var ex = Assert.Throws<ArgumentException>(() => request.Validate());

            Assert.DoesNotContain("pw-x1", ex.Message);
        }

        [Fact]
        public void ToString_RedactsPasswords()
        {
            string text = Valid().ToString();

            Assert.DoesNotContain("test-store-pass", text);
            Assert.DoesNotContain("test-key-pass", text);
            Assert.Contains("upload", text);
        }
    }
}
