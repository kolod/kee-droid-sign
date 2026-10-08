using System;
using KeeDroidSign.Core.GitHub;
using Xunit;

namespace KeeDroidSign.Core.Tests.GitHub
{
    public class HSalsa20Tests
    {
        // libsodium test/default/core1.c and core1.exp (NaCl "firstkey" vector).
        private const string SharedSecret = "4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742";
        private const string FirstKey = "1b27556473e985d462cd51197a9a46c76009549eac6474f206c4ee0844f68389";

        [Fact]
        public void Derive_MatchesLibsodiumCore1Vector()
        {
            byte[] result = HSalsa20.Derive(Hex(SharedSecret), new byte[16]);

            Assert.Equal(Hex(FirstKey), result);
        }

        [Fact]
        public void Derive_RejectsWrongSizes()
        {
            Assert.Throws<ArgumentException>(() => HSalsa20.Derive(new byte[31], new byte[16]));
            Assert.Throws<ArgumentException>(() => HSalsa20.Derive(new byte[32], new byte[15]));
        }

        internal static byte[] Hex(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
