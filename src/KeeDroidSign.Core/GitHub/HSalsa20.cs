using System;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// HSalsa20 core (crypto_core_hsalsa20): derives a 32-byte subkey from a 32-byte key and a
    /// 16-byte input. Used by crypto_box to turn the X25519 shared secret into a box key.
    /// </summary>
    internal static class HSalsa20
    {
        // "expand 32-byte k" as little-endian words.
        private const uint Sigma0 = 0x61707865;
        private const uint Sigma1 = 0x3320646e;
        private const uint Sigma2 = 0x79622d32;
        private const uint Sigma3 = 0x6b206574;

        public static byte[] Derive(byte[] key, byte[] input)
        {
            if (key == null || key.Length != 32) throw new ArgumentException("The key must be 32 bytes.", nameof(key));
            if (input == null || input.Length != 16) throw new ArgumentException("The input must be 16 bytes.", nameof(input));

            uint x0 = Sigma0, x5 = Sigma1, x10 = Sigma2, x15 = Sigma3;
            uint x1 = Load(key, 0), x2 = Load(key, 4), x3 = Load(key, 8), x4 = Load(key, 12);
            uint x11 = Load(key, 16), x12 = Load(key, 20), x13 = Load(key, 24), x14 = Load(key, 28);
            uint x6 = Load(input, 0), x7 = Load(input, 4), x8 = Load(input, 8), x9 = Load(input, 12);

            for (int i = 0; i < 20; i += 2)
            {
                // Column round.
                x4 ^= Rotl(x0 + x12, 7); x8 ^= Rotl(x4 + x0, 9); x12 ^= Rotl(x8 + x4, 13); x0 ^= Rotl(x12 + x8, 18);
                x9 ^= Rotl(x5 + x1, 7); x13 ^= Rotl(x9 + x5, 9); x1 ^= Rotl(x13 + x9, 13); x5 ^= Rotl(x1 + x13, 18);
                x14 ^= Rotl(x10 + x6, 7); x2 ^= Rotl(x14 + x10, 9); x6 ^= Rotl(x2 + x14, 13); x10 ^= Rotl(x6 + x2, 18);
                x3 ^= Rotl(x15 + x11, 7); x7 ^= Rotl(x3 + x15, 9); x11 ^= Rotl(x7 + x3, 13); x15 ^= Rotl(x11 + x7, 18);

                // Row round.
                x1 ^= Rotl(x0 + x3, 7); x2 ^= Rotl(x1 + x0, 9); x3 ^= Rotl(x2 + x1, 13); x0 ^= Rotl(x3 + x2, 18);
                x6 ^= Rotl(x5 + x4, 7); x7 ^= Rotl(x6 + x5, 9); x4 ^= Rotl(x7 + x6, 13); x5 ^= Rotl(x4 + x7, 18);
                x11 ^= Rotl(x10 + x9, 7); x8 ^= Rotl(x11 + x10, 9); x9 ^= Rotl(x8 + x11, 13); x10 ^= Rotl(x9 + x8, 18);
                x12 ^= Rotl(x15 + x14, 7); x13 ^= Rotl(x12 + x15, 9); x14 ^= Rotl(x13 + x12, 13); x15 ^= Rotl(x14 + x13, 18);
            }

            var output = new byte[32];
            Store(output, 0, x0);
            Store(output, 4, x5);
            Store(output, 8, x10);
            Store(output, 12, x15);
            Store(output, 16, x6);
            Store(output, 20, x7);
            Store(output, 24, x8);
            Store(output, 28, x9);
            return output;
        }

        private static uint Rotl(uint value, int count) => (value << count) | (value >> (32 - count));

        private static uint Load(byte[] buffer, int offset) =>
            buffer[offset] | ((uint)buffer[offset + 1] << 8) | ((uint)buffer[offset + 2] << 16) | ((uint)buffer[offset + 3] << 24);

        private static void Store(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }
    }
}
