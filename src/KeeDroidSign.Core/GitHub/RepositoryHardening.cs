using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Optional repository changes the export wizard offers when the refs an environment allows are
    /// not protected. All need a token with the repository Administration permission and only add
    /// rules (new rulesets) or remove the plugin's own deployment pattern; nothing else is touched.
    /// </summary>
    public sealed class RepositoryHardening
    {
        /// <summary>Repository role "admin" for ruleset bypass actors.</summary>
        internal const long AdminRoleId = 5;

        public const string TagRulesetName = "KeeDroidSign: release tags";
        public const string BranchRulesetName = "KeeDroidSign: default branch";

        private readonly GitHubClient _client;

        public RepositoryHardening(GitHubClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Adds a tag ruleset so that only repository admins can create, move or delete <c>v*</c> tags.
        /// </summary>
        public async Task<ProtectionResult> RestrictReleaseTagsAsync(RepositoryTarget repo, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            var body = new RulesetCreateDto
            {
                Name = TagRulesetName,
                Target = "tag",
                Enforcement = "active",
                BypassActors = new[] { new BypassActorDto { ActorId = AdminRoleId, ActorType = "RepositoryRole", BypassMode = "always" } },
                Conditions = Conditions("refs/tags/" + RepositoryInspector.TagPattern),
                Rules = new[] { Rule("creation"), Rule("update"), Rule("deletion") },
            };
            HttpStatusCode status = await _client.CreateRulesetAsync(repo, body, ct).ConfigureAwait(false);
            return Map(status, "Only repository admins can now create, move or delete v* tags (ruleset \"" + TagRulesetName + "\").");
        }

        /// <summary>
        /// Adds a branch ruleset for the default branch: changes only through pull requests, no force
        /// pushes, no deletion. Repository admins can still merge pull requests without approvals.
        /// </summary>
        public async Task<ProtectionResult> ProtectDefaultBranchAsync(RepositoryTarget repo, string branch, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            var body = new RulesetCreateDto
            {
                Name = BranchRulesetName,
                Target = "branch",
                Enforcement = "active",
                BypassActors = new[] { new BypassActorDto { ActorId = AdminRoleId, ActorType = "RepositoryRole", BypassMode = "pull_request" } },
                Conditions = Conditions("~DEFAULT_BRANCH"),
                Rules = new[]
                {
                    Rule("deletion"),
                    Rule("non_fast_forward"),
                    new RulesetRuleDto
                    {
                        Type = "pull_request",
                        Parameters = new PullRequestParametersDto
                        {
                            RequiredApprovingReviewCount = 0,
                            DismissStaleReviewsOnPush = false,
                            RequireCodeOwnerReview = false,
                            RequireLastPushApproval = false,
                            RequiredReviewThreadResolution = false,
                        },
                    },
                },
            };
            HttpStatusCode status = await _client.CreateRulesetAsync(repo, body, ct).ConfigureAwait(false);
            return Map(status, $"Default branch {branch} now accepts changes only through pull requests (ruleset \"{BranchRulesetName}\").");
        }

        /// <summary>Removes the <c>v*</c> tag pattern from the environment's deployment policies.</summary>
        public async Task<ProtectionResult> RemoveTagPatternAsync(RepositoryTarget repo, string environment, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            EnvironmentNames.Validate(environment);

            var policies = await _client.ListBranchPoliciesAsync(repo, environment, ct).ConfigureAwait(false);
            BranchPolicyEntryDto tag = policies.FirstOrDefault(p => p.Name == RepositoryInspector.TagPattern && p.Type == DeploymentPattern.Tag);
            if (tag == null)
                return new ProtectionResult(ProtectionStatus.Protected, false, null,
                    $"Environment '{environment}' does not allow v* tags.");

            HttpStatusCode status = await _client.DeleteBranchPolicyAsync(repo, environment, tag.Id, ct).ConfigureAwait(false);
            return Map(status, $"Environment '{environment}' no longer allows v* tags.");
        }

        private static ProtectionResult Map(HttpStatusCode status, string success)
        {
            switch ((int)status)
            {
                case 200:
                case 201:
                case 204:
                    return new ProtectionResult(ProtectionStatus.Protected, false, null, success);
                case 401:
                    return new ProtectionResult(ProtectionStatus.Failed, false, null, "The token is invalid or expired.");
                case 403:
                case 404:
                    return new ProtectionResult(ProtectionStatus.NeedsAdministration, false, null,
                        "This change needs a token with the repository Administration permission.");
                case 422:
                    return new ProtectionResult(ProtectionStatus.Failed, false, null,
                        "GitHub rejected the change (a ruleset with this name may already exist, or rulesets need a paid plan for private repositories).");
                default:
                    return new ProtectionResult(ProtectionStatus.Failed, false, null, $"GitHub returned HTTP {(int)status}.");
            }
        }

        private static RulesetConditionsCreateDto Conditions(string include) =>
            new RulesetConditionsCreateDto { RefName = new RefNameConditionDto { Include = new[] { include }, Exclude = new string[0] } };

        private static RulesetRuleDto Rule(string type) => new RulesetRuleDto { Type = type };
    }

    public sealed partial class GitHubClient
    {
        internal async Task<HttpStatusCode> CreateRulesetAsync(RepositoryTarget repo, RulesetCreateDto body, CancellationToken ct)
        {
            var content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
            using (Response response = await SendAsync(HttpMethod.Post, RepoPath(repo) + "/rulesets", content, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "create a ruleset");
                return response.Status;
            }
        }

        internal async Task<HttpStatusCode> DeleteBranchPolicyAsync(RepositoryTarget repo, string environment, long id, CancellationToken ct)
        {
            string uri = SecretScope.EnvironmentPath(repo, environment) + "/deployment-branch-policies/" + id;
            using (Response response = await SendAsync(HttpMethod.Delete, uri, null, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "remove a deployment branch policy");
                return response.Status;
            }
        }
    }

    [DataContract]
    internal sealed class RulesetCreateDto
    {
        [DataMember(Name = "name", Order = 0)] public string Name { get; set; }
        [DataMember(Name = "target", Order = 1)] public string Target { get; set; }
        [DataMember(Name = "enforcement", Order = 2)] public string Enforcement { get; set; }
        [DataMember(Name = "bypass_actors", Order = 3)] public BypassActorDto[] BypassActors { get; set; }
        [DataMember(Name = "conditions", Order = 4)] public RulesetConditionsCreateDto Conditions { get; set; }
        [DataMember(Name = "rules", Order = 5)] public RulesetRuleDto[] Rules { get; set; }
    }

    [DataContract]
    internal sealed class BypassActorDto
    {
        [DataMember(Name = "actor_id", Order = 0)] public long ActorId { get; set; }
        [DataMember(Name = "actor_type", Order = 1)] public string ActorType { get; set; }
        [DataMember(Name = "bypass_mode", Order = 2)] public string BypassMode { get; set; }
    }

    [DataContract]
    internal sealed class RulesetConditionsCreateDto
    {
        [DataMember(Name = "ref_name")] public RefNameConditionDto RefName { get; set; }
    }

    [DataContract]
    internal sealed class RulesetRuleDto
    {
        [DataMember(Name = "type", Order = 0)] public string Type { get; set; }
        [DataMember(Name = "parameters", EmitDefaultValue = false, Order = 1)] public PullRequestParametersDto Parameters { get; set; }
    }

    [DataContract]
    internal sealed class PullRequestParametersDto
    {
        [DataMember(Name = "dismiss_stale_reviews_on_push", Order = 0)] public bool DismissStaleReviewsOnPush { get; set; }
        [DataMember(Name = "require_code_owner_review", Order = 1)] public bool RequireCodeOwnerReview { get; set; }
        [DataMember(Name = "require_last_push_approval", Order = 2)] public bool RequireLastPushApproval { get; set; }
        [DataMember(Name = "required_approving_review_count", Order = 3)] public int RequiredApprovingReviewCount { get; set; }
        [DataMember(Name = "required_review_thread_resolution", Order = 4)] public bool RequiredReviewThreadResolution { get; set; }
    }
}
