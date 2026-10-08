using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>
    /// Creates an environment, or restricts an existing one, so that only the given branch/tag
    /// patterns (plus any it already has) can use it. Needs a token with the repository
    /// Administration permission; the export itself never does. Existing reviewers, wait timer and
    /// branch/tag patterns are kept.
    /// </summary>
    public sealed class EnvironmentProtector
    {
        private readonly GitHubClient _client;

        public EnvironmentProtector(GitHubClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<ProtectionResult> ProtectAsync(RepositoryTarget repo, string environment,
            IReadOnlyList<DeploymentPattern> patterns, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (patterns == null) throw new ArgumentNullException(nameof(patterns));
            EnvironmentNames.Validate(environment);

            EnvironmentInfo info = await _client.GetEnvironmentAsync(repo, environment, ct).ConfigureAwait(false);
            if (info.Existence == EnvironmentExistence.NoPermission)
                return Result(ProtectionStatus.NeedsActionsRead, false, null,
                    "The token may not read the environment (it needs the Actions read permission).");
            if (info.Existence == EnvironmentExistence.Found && info.Policy == BranchPolicyKind.ProtectedBranches)
                return Result(ProtectionStatus.AlreadyRestricted, false, null,
                    $"Environment '{environment}' is already limited to protected branches; it was not changed.");

            bool created = info.Existence == EnvironmentExistence.NotFound;
            if (created || info.Policy == BranchPolicyKind.AllBranches)
            {
                HttpStatusCode status = await _client.PutEnvironmentAsync(repo, environment, PutBody(info.Dto), ct).ConfigureAwait(false);
                ProtectionResult failure = MapWriteFailure(status, environment);
                if (failure != null)
                    return failure;
            }

            var existing = created
                ? new List<BranchPolicyEntryDto>()
                : (await _client.ListBranchPoliciesAsync(repo, environment, ct).ConfigureAwait(false)).ToList();

            var added = new List<string>();
            foreach (DeploymentPattern policy in patterns)
            {
                if (existing.Any(p => p.Name == policy.Name && (p.Type ?? DeploymentPattern.Branch) == policy.Type))
                    continue;

                HttpStatusCode status = await _client.AddBranchPolicyAsync(repo, environment, policy.Name, policy.Type, ct)
                    .ConfigureAwait(false);
                // 303: the pattern already exists.
                if (status == HttpStatusCode.SeeOther)
                    continue;
                ProtectionResult failure = MapWriteFailure(status, environment);
                if (failure != null)
                    return failure;
                added.Add(policy.ToString());
            }

            string allowed = string.Join(", ", existing.Select(p => new DeploymentPattern(p.Name, p.Type ?? DeploymentPattern.Branch).ToString())
                .Concat(added).Distinct());
            if (allowed.Length == 0) allowed = "none yet";
            return Result(ProtectionStatus.Protected, created, added, created
                ? $"Environment '{environment}' was created; allowed branches and tags: {allowed}."
                : $"Environment '{environment}' now allows only these branches and tags: {allowed}.");
        }

        /// <summary>The PUT body: custom branch policies plus the protection rules the environment already has.</summary>
        internal static EnvironmentPutDto PutBody(EnvironmentDto existing)
        {
            var body = new EnvironmentPutDto
            {
                DeploymentBranchPolicy = new BranchPolicyDto { ProtectedBranches = false, CustomBranchPolicies = true },
            };
            if (existing == null || existing.ProtectionRules == null)
                return body;

            foreach (ProtectionRuleDto rule in existing.ProtectionRules)
            {
                if (rule.Type == "wait_timer" && rule.WaitTimer.HasValue)
                {
                    body.WaitTimer = rule.WaitTimer;
                }
                else if (rule.Type == "required_reviewers")
                {
                    body.PreventSelfReview = rule.PreventSelfReview;
                    body.Reviewers = (rule.Reviewers ?? new RuleReviewerDto[0])
                        .Where(r => r.Reviewer != null && !string.IsNullOrEmpty(r.Type))
                        .Select(r => new ReviewerRefDto { Type = r.Type, Id = r.Reviewer.Id })
                        .ToArray();
                }
            }
            return body;
        }

        private static ProtectionResult MapWriteFailure(HttpStatusCode status, string environment)
        {
            switch ((int)status)
            {
                case 200:
                case 201:
                    return null;
                case 401:
                    return Result(ProtectionStatus.Failed, false, null, "The token is invalid or expired.");
                case 403:
                case 404:
                    return Result(ProtectionStatus.NeedsAdministration, false, null,
                        $"Changing environment '{environment}' needs a token with the repository Administration permission.");
                case 422:
                    return Result(ProtectionStatus.Failed, false, null,
                        "GitHub rejected the environment settings (environments may need a paid plan for private repositories).");
                default:
                    return Result(ProtectionStatus.Failed, false, null, $"GitHub returned HTTP {(int)status}.");
            }
        }

        private static ProtectionResult Result(ProtectionStatus status, bool created, IReadOnlyList<string> added, string message) =>
            new ProtectionResult(status, created, added, message);
    }
}
