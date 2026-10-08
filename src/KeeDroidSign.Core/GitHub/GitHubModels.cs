using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Result category of a token check.</summary>
    public enum TokenStatus
    {
        Valid,

        /// <summary>HTTP 401: the token is wrong, expired or revoked.</summary>
        InvalidOrExpired,

        /// <summary>HTTP 403 without rate limiting: the token may not use the API.</summary>
        InsufficientPermissions,

        /// <summary>GitHub could not be reached, timed out or returned an unexpected status.</summary>
        NetworkError,

        /// <summary>HTTP 429, or 403 with <c>x-ratelimit-remaining: 0</c>.</summary>
        RateLimited,
    }

    /// <summary>Result of a repository access check.</summary>
    public enum RepositoryAccess
    {
        /// <summary>The repository exists and its Actions secrets can be listed.</summary>
        CanManageSecrets,

        /// <summary>The repository does not exist or is invisible to the token.</summary>
        NotFoundOrNoAccess,

        /// <summary>The repository is visible but its Actions secrets are not.</summary>
        InsufficientPermissions,
        Archived,
        NetworkError,
        RateLimited,
    }

    /// <summary>Outcome of writing a single secret.</summary>
    public enum SecretWriteStatus
    {
        Created,
        Updated,
        Failed,
    }

    /// <summary>Result of <see cref="GitHubClient.ValidateTokenAsync"/>.</summary>
    public sealed class TokenValidationResult
    {
        public TokenValidationResult(TokenStatus status, GitHubIdentity identity, string message)
        {
            Status = status;
            Identity = identity;
            Message = message;
        }

        public TokenStatus Status { get; }

        /// <summary>The authenticated account; <c>null</c> unless <see cref="Status"/> is Valid.</summary>
        public GitHubIdentity Identity { get; }

        /// <summary>Human-readable English explanation; never contains the token.</summary>
        public string Message { get; }

        public override string ToString() => $"TokenValidationResult(Status={Status}, Login={Identity?.Login})";
    }

    /// <summary>A repository's public key for encrypting Actions secrets.</summary>
    public sealed class RepositoryPublicKey
    {
        public RepositoryPublicKey(string keyId, byte[] key)
        {
            KeyId = keyId;
            Key = key;
        }

        public string KeyId { get; }

        /// <summary>32-byte X25519 public key.</summary>
        public byte[] Key { get; }
    }

    /// <summary>Outcome of a single secret write.</summary>
    public sealed class SecretWriteResult
    {
        public SecretWriteResult(SecretWriteStatus status, string reason = null)
        {
            Status = status;
            Reason = reason;
        }

        public SecretWriteStatus Status { get; }

        /// <summary>Fixed English failure reason; never a copy of the response body.</summary>
        public string Reason { get; }
    }

    [DataContract]
    internal sealed class UserDto
    {
        [DataMember(Name = "login")] public string Login { get; set; }
    }

    [DataContract]
    internal sealed class RepositoryDto
    {
        [DataMember(Name = "archived")] public bool Archived { get; set; }
        [DataMember(Name = "default_branch")] public string DefaultBranch { get; set; }
    }

    [DataContract]
    internal sealed class SecretListDto
    {
        [DataMember(Name = "total_count")] public int TotalCount { get; set; }
        [DataMember(Name = "secrets")] public SecretDto[] Secrets { get; set; }
    }

    [DataContract]
    internal sealed class SecretDto
    {
        [DataMember(Name = "name")] public string Name { get; set; }
    }

    [DataContract]
    internal sealed class PublicKeyDto
    {
        [DataMember(Name = "key_id")] public string KeyId { get; set; }
        [DataMember(Name = "key")] public string Key { get; set; }
    }

    [DataContract]
    internal sealed class PutSecretDto
    {
        [DataMember(Name = "encrypted_value", Order = 0)] public string EncryptedValue { get; set; }
        [DataMember(Name = "key_id", Order = 1)] public string KeyId { get; set; }
    }

    /// <summary>JSON helpers over the framework's DataContractJsonSerializer (no extra dependency).</summary>
    internal static class Json
    {
        public static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        public static string Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
