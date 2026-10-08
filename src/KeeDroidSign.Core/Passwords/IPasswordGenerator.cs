namespace KeeDroidSign.Core.Passwords
{
    /// <summary>Generates passwords that satisfy a <see cref="PasswordPolicy"/>.</summary>
    public interface IPasswordGenerator
    {
        /// <summary>Generates a password; throws <see cref="System.ArgumentException"/> for unsatisfiable rules.</summary>
        string Generate(PasswordPolicy policy);
    }
}
