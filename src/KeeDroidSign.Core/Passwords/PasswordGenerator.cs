using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace KeeDroidSign.Core.Passwords
{
    /// <summary>
    /// Cryptographically secure password generator. Generated passwords never contain characters
    /// that break an unquoted shell heredoc or a Java <c>.properties</c> file (FR-004).
    /// </summary>
    public sealed class PasswordGenerator : IPasswordGenerator
    {
        /// <summary>The only symbols ever produced; safe in CI heredocs and <c>.properties</c> files.</summary>
        public const string SafeSymbols = "!#%+,-./:=?@^_~";

        private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
        private const string DigitChars = "0123456789";
        private const string AmbiguousChars = "0O1lI";

        public string Generate(PasswordPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            policy.Validate();

            List<string> classes = BuildClasses(policy);
            string all = string.Concat(classes);
            var result = new char[policy.Length];

            using (var rng = new RNGCryptoServiceProvider())
            {
                int i = 0;
                foreach (string cls in classes)
                    result[i++] = cls[NextIndex(rng, cls.Length)];

                for (; i < result.Length; i++)
                    result[i] = all[NextIndex(rng, all.Length)];

                // Fisher-Yates shuffle so the guaranteed characters are not always at the start.
                for (int j = result.Length - 1; j > 0; j--)
                {
                    int k = NextIndex(rng, j + 1);
                    char tmp = result[j];
                    result[j] = result[k];
                    result[k] = tmp;
                }
            }

            string password = new string(result);
            Array.Clear(result, 0, result.Length);
            return password;
        }

        private static List<string> BuildClasses(PasswordPolicy policy)
        {
            var classes = new List<string>();
            if (policy.Uppercase) classes.Add(UppercaseChars);
            if (policy.Lowercase) classes.Add(LowercaseChars);
            if (policy.Digits) classes.Add(DigitChars);
            if (policy.Symbols) classes.Add(SafeSymbols);

            if (policy.ExcludeAmbiguous)
                classes = classes.Select(c => new string(c.Where(ch => AmbiguousChars.IndexOf(ch) < 0).ToArray())).ToList();

            return classes;
        }

        /// <summary>Uniform random index in [0, count) using rejection sampling (no modulo bias).</summary>
        private static int NextIndex(RandomNumberGenerator rng, int count)
        {
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)count);
            var buffer = new byte[4];
            uint value;
            do
            {
                rng.GetBytes(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            }
            while (value >= limit);

            return (int)(value % (uint)count);
        }
    }
}
