using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Deployment environment calls and repository secret deletion.</summary>
    public sealed partial class GitHubClient
    {
        /// <summary>Reads an environment and its deployment branch policy (needs Actions: read).</summary>
        public async Task<EnvironmentInfo> GetEnvironmentAsync(RepositoryTarget repo, string environment, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            EnvironmentNames.Validate(environment);

            using (Response response = await SendAsync(HttpMethod.Get, SecretScope.EnvironmentPath(repo, environment), null, ct)
                .ConfigureAwait(false))
            {
                EnsureReachable(response, "read the environment");
                switch (response.Status)
                {
                    case HttpStatusCode.OK:
                        EnvironmentDto dto = await response.ReadJsonAsync<EnvironmentDto>().ConfigureAwait(false);
                        if (dto == null)
                            throw new GitHubApiException("GitHub returned an unexpected response while reading the environment.");
                        return new EnvironmentInfo(EnvironmentExistence.Found, MapPolicy(dto.DeploymentBranchPolicy), dto);
                    case HttpStatusCode.NotFound:
                        return new EnvironmentInfo(EnvironmentExistence.NotFound, BranchPolicyKind.Unknown, null);
                    case HttpStatusCode.Forbidden:
                        return new EnvironmentInfo(EnvironmentExistence.NoPermission, BranchPolicyKind.Unknown, null);
                    case HttpStatusCode.Unauthorized:
                        throw new GitHubApiException("Could not read the environment: the token is invalid or expired.");
                    default:
                        throw new GitHubApiException($"Could not read the environment: GitHub returned HTTP {(int)response.Status}.");
                }
            }
        }

        /// <summary>Creates or updates an environment (needs Administration: write); returns the HTTP status.</summary>
        internal async Task<HttpStatusCode> PutEnvironmentAsync(RepositoryTarget repo, string environment, EnvironmentPutDto body,
            CancellationToken ct)
        {
            var content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
            using (Response response = await SendAsync(HttpMethod.Put, SecretScope.EnvironmentPath(repo, environment), content, ct)
                .ConfigureAwait(false))
            {
                EnsureReachable(response, "update the environment");
                return response.Status;
            }
        }

        /// <summary>Lists the custom deployment branch/tag policies of an environment as (name, type).</summary>
        internal async Task<IReadOnlyList<BranchPolicyEntryDto>> ListBranchPoliciesAsync(RepositoryTarget repo, string environment,
            CancellationToken ct)
        {
            var policies = new List<BranchPolicyEntryDto>();
            for (int page = 1; ; page++)
            {
                string uri = SecretScope.EnvironmentPath(repo, environment) + "/deployment-branch-policies?per_page=100&page=" + page;
                using (Response response = await SendAsync(HttpMethod.Get, uri, null, ct).ConfigureAwait(false))
                {
                    EnsureSuccess(response, "list the deployment branch policies");
                    BranchPolicyListDto dto = await response.ReadJsonAsync<BranchPolicyListDto>().ConfigureAwait(false);
                    if (dto == null)
                        throw new GitHubApiException("GitHub returned an unexpected response while listing deployment branch policies.");

                    BranchPolicyEntryDto[] items = dto.BranchPolicies ?? new BranchPolicyEntryDto[0];
                    policies.AddRange(items.Where(p => !string.IsNullOrEmpty(p.Name)));
                    if (items.Length == 0 || policies.Count >= dto.TotalCount)
                        return policies;
                }
            }
        }

        /// <summary>Adds a deployment branch/tag policy (needs Administration: write); returns the HTTP status.</summary>
        internal async Task<HttpStatusCode> AddBranchPolicyAsync(RepositoryTarget repo, string environment, string name, string type,
            CancellationToken ct)
        {
            string body = Json.Serialize(new BranchPolicyEntryDto { Name = name, Type = type });
            var content = new StringContent(body, Encoding.UTF8, "application/json");
            string uri = SecretScope.EnvironmentPath(repo, environment) + "/deployment-branch-policies";
            using (Response response = await SendAsync(HttpMethod.Post, uri, content, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "add a deployment branch policy");
                return response.Status;
            }
        }

        /// <summary>Deletes one repository-level secret; a secret that is already gone counts as deleted.</summary>
        public async Task<SecretDeleteResult> DeleteSecretAsync(RepositoryTarget repo, string name, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            SecretNames.Validate(name);

            string uri = RepoPath(repo) + "/actions/secrets/" + Uri.EscapeDataString(name);
            using (Response response = await SendAsync(HttpMethod.Delete, uri, null, ct).ConfigureAwait(false))
            {
                if (response.Failure == Failure.Network)
                    return new SecretDeleteResult(false, "GitHub could not be reached (network error).");
                if (response.Failure == Failure.RateLimited)
                    return new SecretDeleteResult(false, "The GitHub API rate limit was exceeded.");

                switch ((int)response.Status)
                {
                    case 204:
                    case 404: return new SecretDeleteResult(true);
                    case 403: return new SecretDeleteResult(false, "Insufficient permissions to delete repository secrets.");
                    default: return new SecretDeleteResult(false, "GitHub returned HTTP " + (int)response.Status + ".");
                }
            }
        }

        private static BranchPolicyKind MapPolicy(BranchPolicyDto policy)
        {
            if (policy == null) return BranchPolicyKind.AllBranches;
            if (policy.CustomBranchPolicies) return BranchPolicyKind.CustomPolicies;
            if (policy.ProtectedBranches) return BranchPolicyKind.ProtectedBranches;
            return BranchPolicyKind.AllBranches;
        }

        private static void EnsureReachable(Response response, string action)
        {
            if (response.Failure == Failure.Network)
                throw new GitHubApiException($"Could not {action}: GitHub could not be reached.");
            if (response.Failure == Failure.RateLimited)
                throw new GitHubApiException($"Could not {action}: the GitHub API rate limit was exceeded.");
        }
    }

    /// <summary>Outcome of deleting one repository secret.</summary>
    public sealed class SecretDeleteResult
    {
        public SecretDeleteResult(bool deleted, string reason = null)
        {
            Deleted = deleted;
            Reason = reason;
        }

        public bool Deleted { get; }

        /// <summary>Fixed English failure reason; never a copy of the response body.</summary>
        public string Reason { get; }
    }
}
