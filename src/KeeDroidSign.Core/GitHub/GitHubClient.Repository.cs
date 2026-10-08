using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Read-only repository facts used by the export wizard (all need Metadata: read only,
    /// except classic branch protection, which needs Contents: read).</summary>
    public sealed partial class GitHubClient
    {
        /// <summary>The repository's default branch, e.g. "main" or "master".</summary>
        public async Task<string> GetDefaultBranchAsync(RepositoryTarget repo, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            using (Response response = await SendAsync(HttpMethod.Get, RepoPath(repo), null, ct).ConfigureAwait(false))
            {
                EnsureSuccess(response, "read the repository");
                RepositoryDto dto = await response.ReadJsonAsync<RepositoryDto>().ConfigureAwait(false);
                if (dto == null || string.IsNullOrEmpty(dto.DefaultBranch))
                    throw new GitHubApiException("GitHub returned an unexpected response while reading the repository.");
                return dto.DefaultBranch;
            }
        }

        /// <summary>Types of the active ruleset rules that apply to a branch, or null if they cannot be read.</summary>
        public async Task<IReadOnlyList<string>> GetBranchRuleTypesAsync(RepositoryTarget repo, string branch, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (!IsPlainRefName(branch)) return null;

            string uri = RepoPath(repo) + "/rules/branches/" + Uri.EscapeDataString(branch) + "?per_page=100";
            using (Response response = await SendAsync(HttpMethod.Get, uri, null, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "read the branch rules");
                if (response.Status != HttpStatusCode.OK) return null;
                RuleDto[] rules = await response.ReadJsonAsync<RuleDto[]>().ConfigureAwait(false);
                return rules?.Select(r => r.Type).Where(t => !string.IsNullOrEmpty(t)).ToList();
            }
        }

        /// <summary>Whether classic branch protection is enabled, or null if it cannot be read (needs Contents: read).</summary>
        public async Task<bool?> IsBranchProtectedClassicAsync(RepositoryTarget repo, string branch, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));
            if (!IsPlainRefName(branch)) return null;

            string uri = RepoPath(repo) + "/branches/" + Uri.EscapeDataString(branch);
            using (Response response = await SendAsync(HttpMethod.Get, uri, null, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "read the branch");
                if (response.Status != HttpStatusCode.OK) return null;
                BranchDto dto = await response.ReadJsonAsync<BranchDto>().ConfigureAwait(false);
                return dto?.Protected;
            }
        }

        /// <summary>Tag rulesets (including inherited ones) with their conditions and rules, or null if not readable.</summary>
        public async Task<IReadOnlyList<TagRuleset>> ListTagRulesetsAsync(RepositoryTarget repo, CancellationToken ct)
        {
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            RulesetDto[] summaries;
            string uri = RepoPath(repo) + "/rulesets?targets=tag&includes_parents=true&per_page=100";
            using (Response response = await SendAsync(HttpMethod.Get, uri, null, ct).ConfigureAwait(false))
            {
                EnsureReachable(response, "list the rulesets");
                if (response.Status != HttpStatusCode.OK) return null;
                summaries = await response.ReadJsonAsync<RulesetDto[]>().ConfigureAwait(false);
                if (summaries == null) return null;
            }

            var result = new List<TagRuleset>();
            foreach (RulesetDto summary in summaries.Where(s => s.Target == null || s.Target == "tag"))
            {
                using (Response response = await SendAsync(HttpMethod.Get, RepoPath(repo) + "/rulesets/" + summary.Id, null, ct)
                    .ConfigureAwait(false))
                {
                    EnsureReachable(response, "read a ruleset");
                    if (response.Status != HttpStatusCode.OK) return null;
                    RulesetDto dto = await response.ReadJsonAsync<RulesetDto>().ConfigureAwait(false);
                    if (dto == null) return null;
                    result.Add(new TagRuleset(dto.Name, dto.Enforcement,
                        dto.Conditions?.RefName?.Include ?? new string[0],
                        dto.Conditions?.RefName?.Exclude ?? new string[0],
                        (dto.Rules ?? new RuleDto[0]).Select(r => r.Type).Where(t => t != null).ToList()));
                }
            }
            return result;
        }

        /// <summary>Branch names with '/' are not queried: .NET Framework may unescape "%2F" in paths.</summary>
        private static bool IsPlainRefName(string name) => !string.IsNullOrEmpty(name) && !name.Contains('/');
    }

    /// <summary>A tag ruleset: enforcement, ref name patterns and rule types.</summary>
    public sealed class TagRuleset
    {
        public TagRuleset(string name, string enforcement, IReadOnlyList<string> include, IReadOnlyList<string> exclude,
            IReadOnlyList<string> ruleTypes)
        {
            Name = name;
            Enforcement = enforcement;
            Include = include;
            Exclude = exclude;
            RuleTypes = ruleTypes;
        }

        public string Name { get; }

        /// <summary>"active", "disabled" or "evaluate".</summary>
        public string Enforcement { get; }

        public IReadOnlyList<string> Include { get; }
        public IReadOnlyList<string> Exclude { get; }
        public IReadOnlyList<string> RuleTypes { get; }
    }

    [DataContract]
    internal sealed class RuleDto
    {
        [DataMember(Name = "type")] public string Type { get; set; }
    }

    [DataContract]
    internal sealed class BranchDto
    {
        [DataMember(Name = "protected")] public bool Protected { get; set; }
    }

    [DataContract]
    internal sealed class RulesetDto
    {
        [DataMember(Name = "id")] public long Id { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "target")] public string Target { get; set; }
        [DataMember(Name = "enforcement")] public string Enforcement { get; set; }
        [DataMember(Name = "conditions")] public RulesetConditionsDto Conditions { get; set; }
        [DataMember(Name = "rules")] public RuleDto[] Rules { get; set; }
    }

    [DataContract]
    internal sealed class RulesetConditionsDto
    {
        [DataMember(Name = "ref_name")] public RefNameConditionDto RefName { get; set; }
    }

    [DataContract]
    internal sealed class RefNameConditionDto
    {
        [DataMember(Name = "include")] public string[] Include { get; set; }
        [DataMember(Name = "exclude")] public string[] Exclude { get; set; }
    }
}
