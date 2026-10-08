using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Minimal GitHub REST client (API version 2022-11-28) for token validation and Actions secrets.
    /// Always talks to https://api.github.com; never includes the token or response bodies in errors.
    /// </summary>
    public sealed partial class GitHubClient : IDisposable
    {
        internal const string ApiVersion = "2022-11-28";
        private static readonly Uri BaseAddress = new Uri("https://api.github.com/");
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;

        /// <param name="credential">Personal access token used for every request.</param>
        /// <param name="handler">Optional HTTP handler (tests inject a fake); not disposed by the client.</param>
        public GitHubClient(GitHubCredential credential, HttpMessageHandler handler = null)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));

            _http = handler == null ? new HttpClient() : new HttpClient(handler, false);
            _http.BaseAddress = BaseAddress;
            _http.Timeout = RequestTimeout;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KeeDroidSign", ProductVersion()));
        }

        /// <summary>Checks the token and returns the account login (FR-016, FR-017).</summary>
        public async Task<TokenValidationResult> ValidateTokenAsync(CancellationToken ct)
        {
            using (Response response = await SendAsync(HttpMethod.Get, "user", null, ct).ConfigureAwait(false))
            {
                if (response.Failure == Failure.Network)
                    return new TokenValidationResult(TokenStatus.NetworkError, null, "GitHub could not be reached.");
                if (response.Failure == Failure.RateLimited)
                    return new TokenValidationResult(TokenStatus.RateLimited, null, "The GitHub API rate limit was exceeded; try again later.");

                switch (response.Status)
                {
                    case HttpStatusCode.OK:
                        UserDto user = await response.ReadJsonAsync<UserDto>().ConfigureAwait(false);
                        if (user == null || string.IsNullOrEmpty(user.Login))
                            return new TokenValidationResult(TokenStatus.NetworkError, null, "GitHub returned an unexpected response.");
                        return new TokenValidationResult(TokenStatus.Valid,
                            new GitHubIdentity(user.Login, ParseScopes(response.Message)), "Signed in as " + user.Login + ".");
                    case HttpStatusCode.Unauthorized:
                        return new TokenValidationResult(TokenStatus.InvalidOrExpired, null, "The token is invalid, expired or revoked.");
                    case HttpStatusCode.Forbidden:
                        return new TokenValidationResult(TokenStatus.InsufficientPermissions, null, "The token is not allowed to access the GitHub API.");
                    default:
                        return new TokenValidationResult(TokenStatus.NetworkError, null,
                            "GitHub returned HTTP " + (int)response.Status + ".");
                }
            }
        }

        /// <summary>Checks that the repository exists and its Actions secrets are accessible (FR-018).</summary>
        public async Task<RepositoryAccess> CheckRepositoryAccessAsync(RepositoryTarget repo, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            using (Response response = await SendAsync(HttpMethod.Get, RepoPath(repo), null, ct).ConfigureAwait(false))
            {
                RepositoryAccess? failure = MapAccessFailure(response);
                if (failure.HasValue)
                    return failure.Value;
                if (response.Status != HttpStatusCode.OK)
                    return RepositoryAccess.NotFoundOrNoAccess;

                RepositoryDto dto = await response.ReadJsonAsync<RepositoryDto>().ConfigureAwait(false);
                if (dto != null && dto.Archived)
                    return RepositoryAccess.Archived;
            }

            using (Response response = await SendAsync(HttpMethod.Get, RepoPath(repo) + "/actions/secrets?per_page=1", null, ct).ConfigureAwait(false))
            {
                RepositoryAccess? failure = MapAccessFailure(response);
                if (failure.HasValue)
                    return failure.Value;
                return response.Status == HttpStatusCode.OK
                    ? RepositoryAccess.CanManageSecrets
                    : RepositoryAccess.InsufficientPermissions;
            }
        }

        /// <summary>Lists the names of all repository Actions secrets (FR-021).</summary>
        public Task<IReadOnlyCollection<string>> ListSecretNamesAsync(RepositoryTarget repo, CancellationToken ct) =>
            ListSecretNamesAsync(SecretScope.ForRepository(repo), ct);

        /// <summary>Lists the names of all secrets of the repository or of one of its environments.</summary>
        public async Task<IReadOnlyCollection<string>> ListSecretNamesAsync(SecretScope scope, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            var names = new List<string>();
            for (int page = 1; ; page++)
            {
                string uri = scope.SecretsPath + "?per_page=100&page=" + page;
                using (Response response = await SendAsync(HttpMethod.Get, uri, null, ct).ConfigureAwait(false))
                {
                    EnsureScopeAccess(response, scope);
                    EnsureSuccess(response, scope.IsEnvironment ? "list the environment secrets" : "list the repository secrets");
                    SecretListDto dto = await response.ReadJsonAsync<SecretListDto>().ConfigureAwait(false);
                    if (dto == null)
                        throw new GitHubApiException("GitHub returned an unexpected response while listing secrets.");

                    SecretDto[] secrets = dto.Secrets ?? new SecretDto[0];
                    names.AddRange(secrets.Select(s => s.Name).Where(n => !string.IsNullOrEmpty(n)));
                    if (secrets.Length == 0 || names.Count >= dto.TotalCount)
                        return names;
                }
            }
        }

        /// <summary>Gets the repository public key used to encrypt secrets.</summary>
        public Task<RepositoryPublicKey> GetPublicKeyAsync(RepositoryTarget repo, CancellationToken ct) =>
            GetPublicKeyAsync(SecretScope.ForRepository(repo), ct);

        /// <summary>
        /// Gets the public key used to encrypt secrets of the scope. For an environment, a missing
        /// environment throws <see cref="EnvironmentNotFoundException"/>.
        /// </summary>
        public async Task<RepositoryPublicKey> GetPublicKeyAsync(SecretScope scope, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            using (Response response = await SendAsync(HttpMethod.Get, scope.SecretsPath + "/public-key", null, ct).ConfigureAwait(false))
            {
                EnsureScopeAccess(response, scope);
                EnsureSuccess(response, scope.IsEnvironment ? "get the environment public key" : "get the repository public key");
                PublicKeyDto dto = await response.ReadJsonAsync<PublicKeyDto>().ConfigureAwait(false);

                byte[] key = null;
                try
                {
                    if (dto != null && dto.Key != null)
                        key = Convert.FromBase64String(dto.Key);
                }
                catch (FormatException)
                {
                    key = null;
                }

                if (key == null || key.Length != SealedBox.PublicKeyLength || string.IsNullOrEmpty(dto.KeyId))
                    throw new GitHubApiException("GitHub returned an invalid public key.");

                return new RepositoryPublicKey(dto.KeyId, key);
            }
        }

        /// <summary>Creates or updates one secret with an already encrypted value (FR-022).</summary>
        public Task<SecretWriteResult> PutSecretAsync(RepositoryTarget repo, string name, string encryptedValue,
            string keyId, CancellationToken ct) =>
            PutSecretAsync(SecretScope.ForRepository(repo), name, encryptedValue, keyId, ct);

        /// <summary>Creates or updates one secret of the scope with an already encrypted value.</summary>
        public async Task<SecretWriteResult> PutSecretAsync(SecretScope scope, string name, string encryptedValue,
            string keyId, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            SecretNames.Validate(name);

            string body = Json.Serialize(new PutSecretDto { EncryptedValue = encryptedValue, KeyId = keyId });
            var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            string uri = scope.SecretsPath + "/" + Uri.EscapeDataString(name);

            using (Response response = await SendAsync(HttpMethod.Put, uri, content, ct).ConfigureAwait(false))
            {
                if (response.Failure == Failure.Network)
                    return new SecretWriteResult(SecretWriteStatus.Failed, "GitHub could not be reached (network error).");
                if (response.Failure == Failure.RateLimited)
                    return new SecretWriteResult(SecretWriteStatus.Failed, "The GitHub API rate limit was exceeded.");

                switch ((int)response.Status)
                {
                    case 201: return new SecretWriteResult(SecretWriteStatus.Created);
                    case 204: return new SecretWriteResult(SecretWriteStatus.Updated);
                    case 403:
                    case 404: return new SecretWriteResult(SecretWriteStatus.Failed, scope.IsEnvironment
                        ? "Insufficient permissions to write environment secrets."
                        : "Insufficient permissions to write repository secrets.");
                    case 422: return new SecretWriteResult(SecretWriteStatus.Failed, "The secret was rejected by GitHub (validation failed).");
                    default: return new SecretWriteResult(SecretWriteStatus.Failed, "GitHub returned HTTP " + (int)response.Status + ".");
                }
            }
        }

        public void Dispose() => _http.Dispose();

        private static void EnsureSuccess(Response response, string action)
        {
            if (response.Failure == Failure.Network)
                throw new GitHubApiException($"Could not {action}: GitHub could not be reached.");
            if (response.Failure == Failure.RateLimited)
                throw new GitHubApiException($"Could not {action}: the GitHub API rate limit was exceeded.");

            switch (response.Status)
            {
                case HttpStatusCode.OK:
                    return;
                case HttpStatusCode.Unauthorized:
                    throw new GitHubApiException($"Could not {action}: the token is invalid or expired.");
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.NotFound:
                    throw new GitHubApiException($"Could not {action}: the repository was not found or access is denied.");
                default:
                    throw new GitHubApiException($"Could not {action}: GitHub returned HTTP {(int)response.Status}.");
            }
        }

        /// <summary>Environment-specific errors: a missing environment or no Environments permission.</summary>
        private static void EnsureScopeAccess(Response response, SecretScope scope)
        {
            if (!scope.IsEnvironment || response.Failure != Failure.None)
                return;
            if (response.Status == HttpStatusCode.NotFound)
                throw new EnvironmentNotFoundException(scope.Repository, scope.Environment);
            if (response.Status == HttpStatusCode.Forbidden)
                throw new GitHubApiException(
                    $"The token may not access the secrets of environment '{scope.Environment}' in {scope.Repository} (it needs the Environments permission).");
        }

        internal static string RepoPath(RepositoryTarget repo) =>
            "repos/" + Uri.EscapeDataString(repo.Owner) + "/" + Uri.EscapeDataString(repo.Name);

        private static RepositoryAccess? MapAccessFailure(Response response)
        {
            if (response.Failure == Failure.Network) return RepositoryAccess.NetworkError;
            if (response.Failure == Failure.RateLimited) return RepositoryAccess.RateLimited;
            return null;
        }

        private async Task<Response> SendAsync(HttpMethod method, string relativeUri, HttpContent content, CancellationToken ct)
        {
            using (var request = new HttpRequestMessage(method, relativeUri) { Content = content })
            {
                try
                {
                    HttpResponseMessage message = await _http.SendAsync(request, ct).ConfigureAwait(false);
                    return new Response(message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException || ex is WebException)
                {
                    // Connection failures and HttpClient timeouts (reported as TaskCanceledException).
                    return Response.NetworkFailure();
                }
            }
        }

        private static string[] ParseScopes(HttpResponseMessage message)
        {
            if (!message.Headers.TryGetValues("X-OAuth-Scopes", out IEnumerable<string> values))
                return new string[0];
            return values.SelectMany(v => v.Split(','))
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();
        }

        private static string ProductVersion()
        {
            Version version = typeof(GitHubClient).Assembly.GetName().Version;
            return version.ToString(3);
        }

        internal enum Failure
        {
            None,
            Network,
            RateLimited,
        }

        /// <summary>Wraps an HTTP response with the rate-limit and network failure classification.</summary>
        internal sealed class Response : IDisposable
        {
            private Response() { }

            public Response(HttpResponseMessage message)
            {
                Message = message;
                Status = message.StatusCode;
                Failure = IsRateLimited(message) ? Failure.RateLimited : Failure.None;
            }

            public HttpResponseMessage Message { get; private set; }
            public HttpStatusCode Status { get; private set; }
            public Failure Failure { get; private set; }

            public static Response NetworkFailure() => new Response { Failure = Failure.Network };

            public async Task<T> ReadJsonAsync<T>() where T : class
            {
                if (Message?.Content == null)
                    return null;
                string json = await Message.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    return Json.Deserialize<T>(json);
                }
                catch (SerializationException)
                {
                    return null;
                }
            }

            public void Dispose() => Message?.Dispose();

            private static bool IsRateLimited(HttpResponseMessage message)
            {
                if ((int)message.StatusCode == 429)
                    return true;
                if (message.StatusCode != HttpStatusCode.Forbidden)
                    return false;
                return message.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string> values)
                    && values.Any(v => v.Trim() == "0");
            }
        }
    }
}
