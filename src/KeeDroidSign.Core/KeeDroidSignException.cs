using System;

namespace KeeDroidSign.Core
{
    /// <summary>
    /// Base class for all errors raised by the core. Messages are fixed English text plus
    /// non-secret context only; they never contain passwords, tokens or key bytes.
    /// </summary>
    public class KeeDroidSignException : Exception
    {
        public KeeDroidSignException(string message) : base(message) { }

        public KeeDroidSignException(string message, Exception innerException)
            : base(message, innerException) { }
    }

    /// <summary>The keystore password is wrong or the keystore is corrupted.</summary>
    public sealed class InvalidKeystorePasswordException : KeeDroidSignException
    {
        public InvalidKeystorePasswordException()
            : base("The keystore could not be opened: wrong store password or corrupted keystore.") { }
    }

    /// <summary>The private key password is wrong.</summary>
    public sealed class InvalidKeyPasswordException : KeeDroidSignException
    {
        public InvalidKeyPasswordException(string alias)
            : base($"The private key '{alias}' could not be decrypted: wrong key password.") { }
    }

    /// <summary>The keystore has no entry with the requested alias.</summary>
    public sealed class AliasNotFoundException : KeeDroidSignException
    {
        public AliasNotFoundException(string alias)
            : base($"The keystore does not contain a key entry with alias '{alias}'.") { }
    }

    /// <summary>The output keystore file already exists and is never overwritten.</summary>
    public sealed class KeystoreExistsException : KeeDroidSignException
    {
        public KeystoreExistsException(string path)
            : base($"The file '{path}' already exists and will not be overwritten.") { }
    }

    /// <summary>A GitHub API call failed in a way that cannot be expressed as a status value.</summary>
    public class GitHubApiException : KeeDroidSignException
    {
        public GitHubApiException(string message) : base(message) { }
    }
}
