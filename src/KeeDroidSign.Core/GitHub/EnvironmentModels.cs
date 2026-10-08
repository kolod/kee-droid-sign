using System.Collections.Generic;
using System.Runtime.Serialization;

namespace KeeDroidSign.Core.GitHub
{
    /// <summary>Whether a deployment environment exists, as far as the token can tell.</summary>
    public enum EnvironmentExistence
    {
        Found,
        NotFound,

        /// <summary>The token may not read the environment (HTTP 403).</summary>
        NoPermission,
    }

    /// <summary>Which branches and tags may use an environment (its deployment branch policy).</summary>
    public enum BranchPolicyKind
    {
        /// <summary>No policy: every branch and tag may use the environment and read its secrets.</summary>
        AllBranches,

        /// <summary>Only protected branches.</summary>
        ProtectedBranches,

        /// <summary>Only branches and tags matching custom name patterns.</summary>
        CustomPolicies,

        /// <summary>The policy could not be read (the token lacks the Actions read permission).</summary>
        Unknown,
    }

    /// <summary>Existence and branch policy of an environment, checked before an export.</summary>
    public sealed class EnvironmentStatus
    {
        public EnvironmentStatus(EnvironmentExistence existence, BranchPolicyKind policy)
        {
            Existence = existence;
            Policy = policy;
        }

        public EnvironmentExistence Existence { get; }
        public BranchPolicyKind Policy { get; }

        public override string ToString() => $"EnvironmentStatus({Existence}, {Policy})";
    }

    /// <summary>An environment as returned by <c>GET /repos/{owner}/{repo}/environments/{name}</c>.</summary>
    public sealed class EnvironmentInfo
    {
        internal EnvironmentInfo(EnvironmentExistence existence, BranchPolicyKind policy, EnvironmentDto dto)
        {
            Existence = existence;
            Policy = policy;
            Dto = dto;
        }

        public EnvironmentExistence Existence { get; }
        public BranchPolicyKind Policy { get; }

        /// <summary>The raw response (null unless found); used to keep protection rules on update.</summary>
        internal EnvironmentDto Dto { get; }
    }

    /// <summary>Outcome of <see cref="EnvironmentProtector.ProtectAsync"/>.</summary>
    public enum ProtectionStatus
    {
        /// <summary>The environment now allows only the required branch and tag patterns (and any it had).</summary>
        Protected,

        /// <summary>The environment is limited to protected branches; it was left unchanged.</summary>
        AlreadyRestricted,

        /// <summary>Changing the environment needs the repository Administration permission.</summary>
        NeedsAdministration,

        /// <summary>Reading the environment needs the repository Actions read permission.</summary>
        NeedsActionsRead,
        Failed,
    }

    /// <summary>Result of creating or restricting an environment.</summary>
    public sealed class ProtectionResult
    {
        public ProtectionResult(ProtectionStatus status, bool created, IReadOnlyList<string> addedPolicies, string message)
        {
            Status = status;
            Created = created;
            AddedPolicies = addedPolicies ?? new string[0];
            Message = message;
        }

        public ProtectionStatus Status { get; }

        /// <summary>True when the environment did not exist and was created.</summary>
        public bool Created { get; }

        /// <summary>Branch/tag patterns added, e.g. "main (branch)", "v* (tag)".</summary>
        public IReadOnlyList<string> AddedPolicies { get; }

        /// <summary>Fixed English explanation; never contains the token or a response body.</summary>
        public string Message { get; }

        public bool Succeeded => Status == ProtectionStatus.Protected || Status == ProtectionStatus.AlreadyRestricted;

        public override string ToString() => $"ProtectionResult({Status}, Created={Created}, Added={string.Join(", ", AddedPolicies)})";
    }

    /// <summary>The environment does not exist, or the token cannot see it.</summary>
    public sealed class EnvironmentNotFoundException : GitHubApiException
    {
        public EnvironmentNotFoundException(RepositoryTarget repository, string environment)
            : base($"Environment '{environment}' does not exist in {repository}, or the token cannot see it.")
        {
            Repository = repository;
            Environment = environment;
        }

        public RepositoryTarget Repository { get; }
        public string Environment { get; }
    }

    [DataContract]
    internal sealed class EnvironmentDto
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "deployment_branch_policy")] public BranchPolicyDto DeploymentBranchPolicy { get; set; }
        [DataMember(Name = "protection_rules")] public ProtectionRuleDto[] ProtectionRules { get; set; }
    }

    [DataContract]
    internal sealed class BranchPolicyDto
    {
        [DataMember(Name = "protected_branches", Order = 0)] public bool ProtectedBranches { get; set; }
        [DataMember(Name = "custom_branch_policies", Order = 1)] public bool CustomBranchPolicies { get; set; }
    }

    [DataContract]
    internal sealed class ProtectionRuleDto
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "wait_timer")] public int? WaitTimer { get; set; }
        [DataMember(Name = "prevent_self_review")] public bool? PreventSelfReview { get; set; }
        [DataMember(Name = "reviewers")] public RuleReviewerDto[] Reviewers { get; set; }
    }

    [DataContract]
    internal sealed class RuleReviewerDto
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "reviewer")] public ReviewerAccountDto Reviewer { get; set; }
    }

    [DataContract]
    internal sealed class ReviewerAccountDto
    {
        [DataMember(Name = "id")] public long Id { get; set; }
    }

    /// <summary>Body of <c>PUT /repos/{owner}/{repo}/environments/{name}</c>; omitted members are not sent.</summary>
    [DataContract]
    internal sealed class EnvironmentPutDto
    {
        [DataMember(Name = "wait_timer", EmitDefaultValue = false, Order = 0)] public int? WaitTimer { get; set; }
        [DataMember(Name = "prevent_self_review", EmitDefaultValue = false, Order = 1)] public bool? PreventSelfReview { get; set; }
        [DataMember(Name = "reviewers", EmitDefaultValue = false, Order = 2)] public ReviewerRefDto[] Reviewers { get; set; }
        [DataMember(Name = "deployment_branch_policy", Order = 3)] public BranchPolicyDto DeploymentBranchPolicy { get; set; }
    }

    [DataContract]
    internal sealed class ReviewerRefDto
    {
        [DataMember(Name = "type", Order = 0)] public string Type { get; set; }
        [DataMember(Name = "id", Order = 1)] public long Id { get; set; }
    }

    [DataContract]
    internal sealed class BranchPolicyListDto
    {
        [DataMember(Name = "total_count")] public int TotalCount { get; set; }
        [DataMember(Name = "branch_policies")] public BranchPolicyEntryDto[] BranchPolicies { get; set; }
    }

    [DataContract]
    internal sealed class BranchPolicyEntryDto
    {
        [DataMember(Name = "id", EmitDefaultValue = false, Order = 0)] public long Id { get; set; }
        [DataMember(Name = "name", Order = 0)] public string Name { get; set; }
        [DataMember(Name = "type", EmitDefaultValue = false, Order = 1)] public string Type { get; set; }
    }
}
