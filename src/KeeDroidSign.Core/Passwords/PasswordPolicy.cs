using System;

namespace KeeDroidSign.Core.Passwords
{
    /// <summary>Rules for generated passwords (FR-002).</summary>
    public sealed class PasswordPolicy
    {
        public const int MinLength = 8;
        public const int MaxLength = 128;

        public int Length { get; set; } = 32;
        public bool Uppercase { get; set; } = true;
        public bool Lowercase { get; set; } = true;
        public bool Digits { get; set; } = true;

        /// <summary>Symbols are always taken from <see cref="PasswordGenerator.SafeSymbols"/>.</summary>
        public bool Symbols { get; set; } = true;

        /// <summary>Excludes visually ambiguous characters <c>0 O 1 l I</c>.</summary>
        public bool ExcludeAmbiguous { get; set; }

        internal int EnabledClassCount =>
            (Uppercase ? 1 : 0) + (Lowercase ? 1 : 0) + (Digits ? 1 : 0) + (Symbols ? 1 : 0);

        /// <summary>Throws <see cref="ArgumentException"/> when the rules cannot be satisfied.</summary>
        public void Validate()
        {
            if (Length < MinLength || Length > MaxLength)
                throw new ArgumentException(
                    $"Password length must be between {MinLength} and {MaxLength}, but was {Length}.");

            if (EnabledClassCount == 0)
                throw new ArgumentException("At least one character class must be enabled.");

            if (Length < EnabledClassCount)
                throw new ArgumentException(
                    $"Password length {Length} is shorter than the number of required character classes ({EnabledClassCount}).");
        }
    }
}
