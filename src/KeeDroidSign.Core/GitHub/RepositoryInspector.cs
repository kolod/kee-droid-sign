using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Whether a branch or tag pattern is protected against pushes by anyone with write access.</summary>
    public enum RefProtection
    {
        Protected,
        Unprotected,

        /// <summary>The token may not read the protection settings.</summary>
        Unknown,
    }

    /// <summary>A deployment branch/tag pattern of an environment, e.g. "main (branch)" or "v* (tag)".</summary>
    public sealed class DeploymentPattern : IEquatable<DeploymentPattern>
    {
        public const string Branch = "branch";
        public const string Tag = "tag";

        public DeploymentPattern(string name, string type)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("The pattern name is required.", nameof(name));
            if (type != Branch && type != Tag) throw new ArgumentException("The pattern type must be 'branch' or 'tag'.", nameof(type));
            Name = name;
            Type = type;
        }

        public string Name { get; }
        public string Type { get; }

        public bool Equals(DeploymentPattern other) => other != null && other.Name == Name && other.Type == Type;
        public override bool Equals(object obj) => Equals(obj as DeploymentPattern);
        public override int GetHashCode() => (Name + "\n" + Type).GetHashCode();
        public override string ToString() => $"{Name} ({Type})";
    }

    /// <summary>What the export wizard knows about the repository before anything is changed.</summary>
    public sealed class RepositoryReport
    {
        internal RepositoryReport(SecretScope scope, string defaultBranch, RefProtection defaultBranchProtection,
            EnvironmentExistence? environment, BranchPolicyKind policy, IReadOnlyList<DeploymentPattern> existingPatterns,
            IReadOnlyList<DeploymentPattern> proposedPatterns, RefProtection? tagCreation, ExportPlan plan, RepositoryCopies copies)
        {
            Scope = scope;
            DefaultBranch = defaultBranch;
            DefaultBranchProtection = defaultBranchProtection;
            Environment = environment;
            Policy = policy;
            ExistingPatterns = existingPatterns;
            ProposedPatterns = proposedPatterns;
            TagCreation = tagCreation;
            Plan = plan;
            Copies = copies;
        }

        public SecretScope Scope { get; }
        public string DefaultBranch { get; }
        public RefProtection DefaultBranchProtection { get; }

        /// <summary>Null for repository-level targets.</summary>
        public EnvironmentExistence? Environment { get; }

        /// <summary>Deployment branch policy; Unknown when missing or unreadable.</summary>
        public BranchPolicyKind Policy { get; }

        /// <summary>Custom patterns the environment already has (empty if none or unknown).</summary>
        public IReadOnlyList<DeploymentPattern> ExistingPatterns { get; }

        /// <summary>Patterns the wizard proposes to add (empty when nothing should be changed).</summary>
        public IReadOnlyList<DeploymentPattern> ProposedPatterns { get; }

        /// <summary>Whether creating v* tags is restricted; null when no v* tag pattern is or would be allowed.</summary>
        public RefProtection? TagCreation { get; }

        /// <summary>Secrets to create and to overwrite (all "create" while the environment is missing).</summary>
        public ExportPlan Plan { get; }

        /// <summary>Repository-level copies; null for repository-level targets.</summary>
        public RepositoryCopies Copies { get; }

        public bool EnvironmentMissing => Environment == EnvironmentExistence.NotFound;

        /// <summary>True when the environment should be created or restricted (some patterns proposed or it is missing).</summary>
        public bool ProtectionProposed => EnvironmentMissing || ProposedPatterns.Count > 0;
    }

    /// <summary>Inspects a repository for the export wizard; reads only, never changes anything.</summary>
    public sealed class RepositoryInspector
    {
        public const string TagPattern = "v*";

        /// <summary>A sample release tag used to evaluate tag ruleset conditions.</summary>
        internal const string SampleReleaseTag = "refs/tags/v1.0.0";

        private readonly GitHubClient _client;

        public RepositoryInspector(GitHubClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<RepositoryReport> InspectAsync(SecretScope scope, SecretMapping mapping, CancellationToken ct)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            SecretNames.Validate(mapping);
            RepositoryTarget repo = scope.Repository;

            string defaultBranch = await _client.GetDefaultBranchAsync(repo, ct).ConfigureAwait(false);
            RefProtection branchProtection = await BranchProtectionAsync(repo, defaultBranch, ct).ConfigureAwait(false);

            if (!scope.IsEnvironment)
            {
                ExportPlan repositoryPlan = await new SecretExporter(_client).PlanAsync(scope, mapping, ct).ConfigureAwait(false);
                return new RepositoryReport(scope, defaultBranch, branchProtection, null, BranchPolicyKind.Unknown,
                    new DeploymentPattern[0], new DeploymentPattern[0], null, repositoryPlan, null);
            }

            // The public key needs the export's own permission (Environments) and tells whether the environment exists.
            EnvironmentExistence existence = EnvironmentExistence.Found;
            try
            {
                await _client.GetPublicKeyAsync(scope, ct).ConfigureAwait(false);
            }
            catch (EnvironmentNotFoundException)
            {
                existence = EnvironmentExistence.NotFound;
            }

            BranchPolicyKind policy = BranchPolicyKind.Unknown;
            var existing = new List<DeploymentPattern>();
            if (existence == EnvironmentExistence.Found)
            {
                EnvironmentInfo info = await _client.GetEnvironmentAsync(repo, scope.Environment, ct).ConfigureAwait(false);
                if (info.Existence == EnvironmentExistence.Found)
                {
                    policy = info.Policy;
                    if (policy == BranchPolicyKind.CustomPolicies)
                    {
                        try
                        {
                            foreach (BranchPolicyEntryDto p in await _client.ListBranchPoliciesAsync(repo, scope.Environment, ct).ConfigureAwait(false))
                                existing.Add(new DeploymentPattern(p.Name, p.Type == DeploymentPattern.Tag ? DeploymentPattern.Tag : DeploymentPattern.Branch));
                        }
                        catch (GitHubApiException)
                        {
                            // The patterns cannot be read: report the policy as unknown and propose nothing.
                            policy = BranchPolicyKind.Unknown;
                            existing.Clear();
                        }
                    }
                }
            }

            var proposed = new List<DeploymentPattern>();
            if (existence == EnvironmentExistence.NotFound || policy == BranchPolicyKind.AllBranches || policy == BranchPolicyKind.CustomPolicies)
            {
                foreach (var pattern in new[]
                {
                    new DeploymentPattern(defaultBranch, DeploymentPattern.Branch),
                    new DeploymentPattern(TagPattern, DeploymentPattern.Tag),
                })
                {
                    if (!existing.Contains(pattern)) proposed.Add(pattern);
                }
                // An environment that already restricts itself to its own patterns is left alone
                // unless it lacks the default branch or v* (both proposed, but not forced).
            }

            bool tagsAllowed = proposed.Concat(existing).Any(p => p.Type == DeploymentPattern.Tag);
            RefProtection? tagCreation = tagsAllowed ? await TagCreationAsync(repo, ct).ConfigureAwait(false) : (RefProtection?)null;

            var exporter = new SecretExporter(_client);
            ExportPlan plan;
            if (existence == EnvironmentExistence.Found)
            {
                plan = await exporter.PlanAsync(scope, mapping, ct).ConfigureAwait(false);
            }
            else
            {
                plan = new ExportPlan(mapping.AllNames().ToList(), new string[0], scope,
                    new EnvironmentStatus(EnvironmentExistence.NotFound, BranchPolicyKind.Unknown));
            }

            RepositoryCopies copies = await exporter.FindRepositoryCopiesAsync(repo, mapping, ct).ConfigureAwait(false);

            return new RepositoryReport(scope, defaultBranch, branchProtection, existence, policy, existing, proposed,
                tagCreation, plan, copies);
        }

        private async Task<RefProtection> BranchProtectionAsync(RepositoryTarget repo, string branch, CancellationToken ct)
        {
            IReadOnlyList<string> rules = await _client.GetBranchRuleTypesAsync(repo, branch, ct).ConfigureAwait(false);
            if (rules != null && rules.Any(t => t == "pull_request" || t == "update"))
                return RefProtection.Protected;

            bool? classic = await _client.IsBranchProtectedClassicAsync(repo, branch, ct).ConfigureAwait(false);
            if (classic == true) return RefProtection.Protected;
            if (classic == false && rules != null) return RefProtection.Unprotected;
            return RefProtection.Unknown;
        }

        private async Task<RefProtection> TagCreationAsync(RepositoryTarget repo, CancellationToken ct)
        {
            IReadOnlyList<TagRuleset> rulesets = await _client.ListTagRulesetsAsync(repo, ct).ConfigureAwait(false);
            if (rulesets == null) return RefProtection.Unknown;
            return rulesets.Any(RestrictsReleaseTagCreation) ? RefProtection.Protected : RefProtection.Unprotected;
        }

        internal static bool RestrictsReleaseTagCreation(TagRuleset ruleset) =>
            ruleset.Enforcement == "active" &&
            ruleset.RuleTypes.Contains("creation") &&
            ruleset.Include.Any(p => RefPatternMatches(p, SampleReleaseTag)) &&
            !ruleset.Exclude.Any(p => RefPatternMatches(p, SampleReleaseTag));

        /// <summary>Ruleset ref pattern match (fnmatch style: '*' within a segment, '**' across, '~ALL').</summary>
        internal static bool RefPatternMatches(string pattern, string refName)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern == "~ALL") return true;
            if (pattern.StartsWith("~", StringComparison.Ordinal)) return false;

            var regex = new StringBuilder("^");
            for (int i = 0; i < pattern.Length; i++)
            {
                char c = pattern[i];
                if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*') { regex.Append(".*"); i++; }
                else if (c == '*') regex.Append("[^/]*");
                else if (c == '?') regex.Append("[^/]");
                else regex.Append(Regex.Escape(c.ToString()));
            }
            regex.Append('$');
            return Regex.IsMatch(refName, regex.ToString());
        }
    }
}
