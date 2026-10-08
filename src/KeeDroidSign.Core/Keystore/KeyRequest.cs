using System;
using System.Linq;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Parameters for generating an Android signing key and keystore.</summary>
    public sealed class KeyRequest
    {
        public const int MinPasswordLength = 6;
        public const int MinValidityYears = 25;
        public const int MaxValidityYears = 100;

        public string Alias { get; set; }
        public DistinguishedName Subject { get; set; }

        /// <summary>RSA key size: 2048, 3072 or 4096 bits.</summary>
        public int KeySize { get; set; } = 4096;

        public int ValidityYears { get; set; } = 30;
        public string StorePassword { get; set; }
        public string KeyPassword { get; set; }

        /// <summary>Throws <see cref="ArgumentException"/>; messages never contain password values.</summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(Alias) || Alias.Length > 64 || !Alias.All(IsAliasChar))
                throw new ArgumentException(
                    "The key alias must be 1-64 characters long and contain only Latin letters, digits, '.', '_' or '-'.");

            if (Subject == null)
                throw new ArgumentException("The certificate subject is required.");
            Subject.Validate();

            if (KeySize != 2048 && KeySize != 3072 && KeySize != 4096)
                throw new ArgumentException("The RSA key size must be 2048, 3072 or 4096 bits.");

            if (ValidityYears < MinValidityYears || ValidityYears > MaxValidityYears)
                throw new ArgumentException(
                    $"The validity must be between {MinValidityYears} and {MaxValidityYears} years.");

            ValidatePassword(StorePassword, "store password");
            ValidatePassword(KeyPassword, "key password");
        }

        public override string ToString() =>
            $"KeyRequest(Alias={Alias}, Subject={Subject}, KeySize={KeySize}, ValidityYears={ValidityYears}, " +
            $"StorePassword={Redaction.Placeholder}, KeyPassword={Redaction.Placeholder})";

        private static void ValidatePassword(string password, string name)
        {
            if (password == null || password.Length < MinPasswordLength)
                throw new ArgumentException($"The {name} must be at least {MinPasswordLength} characters long.");

            if (password.Any(char.IsControl))
                throw new ArgumentException($"The {name} must not contain control characters.");
        }

        private static bool IsAliasChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
    }
}
