using System;
using System.Collections.Generic;
using System.Linq;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Properties;
using KeeDroidSign.Storage;

namespace KeeDroidSign.Services
{
    /// <summary>How serious a finding of the repository inspection is.</summary>
    public enum FindingLevel
    {
        Ok,
        Info,
        Warning,

        /// <summary>The export cannot run as long as this is so.</summary>
        Blocking,

        /// <summary>The token may not read this.</summary>
        Unknown,
    }

    /// <summary>One line of the wizard's review page.</summary>
    public sealed class WizardFinding
    {
        private readonly FindingLevel _level;
        private readonly string _text;

        public WizardFinding(FindingLevel level, string text)
        {
            _level = level;
            _text = text;
        }

        public FindingLevel Level { get { return _level; } }
        public string Text { get { return _text; } }

        public override string ToString()
        {
            return Symbol(_level) + " " + _text;
        }

        public static string Symbol(FindingLevel level)
        {
            switch (level)
            {
                case FindingLevel.Ok: return "\u2713";
                case FindingLevel.Warning: return "\u26A0";
                case FindingLevel.Blocking: return "\u2717";
                case FindingLevel.Unknown: return "?";
                default: return "\u2022";
            }
        }
    }

    /// <summary>
    /// Texts of the export wizard. The review always names the target repository (and environment),
    /// so a key cannot be sent to an unexpected repository (e.g. a tampered URL field) by one click.
    /// </summary>
    public static class ExportWizardText
    {
        /// <summary>"owner/name, environment release" or "owner/name, repository secrets".</summary>
        public static string Target(ExportTarget target)
        {
            if (target == null) throw new ArgumentNullException("target");
            string repository = target.Repository == null ? "?" : target.Repository.ToString();
            return target.IsEnvironment
                ? string.Format(Strings.TargetEnvironmentFmt, repository, target.Environment)
                : string.Format(Strings.TargetRepositoryFmt, repository);
        }

        /// <summary>Label of the "use the default" choice on the target page.</summary>
        public static string DefaultChoice(string defaultEnvironment)
        {
            return string.Format(Strings.RadioUseDefault, string.IsNullOrEmpty(defaultEnvironment)
                ? Strings.DefaultTargetRepository
                : string.Format(Strings.DefaultTargetEnvironment, defaultEnvironment));
        }

        public static IList<WizardFinding> Findings(ExportTarget target, RepositoryReport report)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (report == null) throw new ArgumentNullException("report");

            var findings = new List<WizardFinding>();
            string environment = target.Environment;

            if (!target.IsEnvironment)
            {
                findings.Add(new WizardFinding(FindingLevel.Warning,
                    string.Format(Strings.FindingRepositorySecrets, target.Repository)));
            }
            else if (report.EnvironmentMissing)
            {
                findings.Add(new WizardFinding(FindingLevel.Blocking, string.Format(Strings.FindingEnvironmentMissing, environment)));
            }
            else
            {
                switch (report.Policy)
                {
                    case BranchPolicyKind.AllBranches:
                        findings.Add(new WizardFinding(FindingLevel.Warning,
                            string.Format(Strings.FindingEnvironmentAllBranches, environment)));
                        break;
                    case BranchPolicyKind.ProtectedBranches:
                        findings.Add(new WizardFinding(FindingLevel.Ok,
                            string.Format(Strings.FindingEnvironmentProtectedBranches, environment)));
                        break;
                    case BranchPolicyKind.CustomPolicies:
                        findings.Add(report.ExistingPatterns.Count == 0
                            ? new WizardFinding(FindingLevel.Info, string.Format(Strings.FindingEnvironmentNoPatterns, environment))
                            : new WizardFinding(FindingLevel.Ok, string.Format(Strings.FindingEnvironmentPatterns, environment,
                                string.Join(", ", report.ExistingPatterns.Select(p => p.ToString())))));
                        break;
                    default:
                        findings.Add(new WizardFinding(FindingLevel.Unknown, string.Format(Strings.FindingEnvironmentUnknown, environment)));
                        break;
                }
            }

            // The default branch matters for repository secrets too: anything pushed there can read them.
            switch (report.DefaultBranchProtection)
            {
                case RefProtection.Protected:
                    findings.Add(new WizardFinding(FindingLevel.Ok, string.Format(Strings.FindingBranchProtected, report.DefaultBranch)));
                    break;
                case RefProtection.Unprotected:
                    findings.Add(new WizardFinding(FindingLevel.Warning, string.Format(Strings.FindingBranchUnprotected, report.DefaultBranch)));
                    break;
                default:
                    findings.Add(new WizardFinding(FindingLevel.Unknown, string.Format(Strings.FindingBranchUnknown, report.DefaultBranch)));
                    break;
            }

            if (report.TagCreation.HasValue)
            {
                switch (report.TagCreation.Value)
                {
                    case RefProtection.Protected:
                        findings.Add(new WizardFinding(FindingLevel.Ok, Strings.FindingTagsRestricted));
                        break;
                    case RefProtection.Unprotected:
                        findings.Add(new WizardFinding(FindingLevel.Warning, Strings.FindingTagsUnrestricted));
                        break;
                    default:
                        findings.Add(new WizardFinding(FindingLevel.Unknown, Strings.FindingTagsUnknown));
                        break;
                }
            }

            if (report.Plan.ToCreate.Count > 0)
                findings.Add(new WizardFinding(FindingLevel.Info,
                    string.Format(Strings.FindingSecretsNew, string.Join(", ", report.Plan.ToCreate))));
            if (report.Plan.ToOverwrite.Count > 0)
                findings.Add(new WizardFinding(FindingLevel.Info,
                    string.Format(Strings.FindingSecretsOverwrite, string.Join(", ", report.Plan.ToOverwrite))));

            if (report.Copies != null)
            {
                if (!report.Copies.Checked)
                    findings.Add(new WizardFinding(FindingLevel.Unknown, Strings.WarningRepositoryCopiesUnchecked));
                else if (report.Copies.Names.Count > 0)
                    findings.Add(new WizardFinding(FindingLevel.Warning,
                        string.Format(Strings.FindingCopies, string.Join(", ", report.Copies.Names))));
            }
            return findings;
        }

        public static string ProtectAction(ExportTarget target, RepositoryReport report)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (report == null) throw new ArgumentNullException("report");
            return string.Format(report.EnvironmentMissing ? Strings.ActionCreateEnvironment : Strings.ActionRestrictEnvironment,
                target.Environment);
        }

        public static string ExportAction(ExportTarget target, RepositoryReport report)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (report == null) throw new ArgumentNullException("report");
            return report.EnvironmentMissing
                ? string.Format(Strings.ActionExportBlocked, target.Environment)
                : string.Format(Strings.ActionExport, report.Plan.ToCreate.Count + report.Plan.ToOverwrite.Count);
        }

        /// <summary>Manual setup steps, naming the repository's real default branch.</summary>
        public static string ManualSteps(ExportTarget target, RepositoryReport report)
        {
            if (target == null) throw new ArgumentNullException("target");
            if (report == null) throw new ArgumentNullException("report");
            return string.Format(Strings.EnvironmentManualSteps, target.Environment, report.DefaultBranch);
        }

        /// <summary>True when the review should show the manual steps (environment missing or not restricted).</summary>
        public static bool NeedsManualSteps(ExportTarget target, RepositoryReport report)
        {
            return target != null && report != null && target.IsEnvironment &&
                (report.EnvironmentMissing || report.Policy == BranchPolicyKind.AllBranches);
        }

        /// <summary>Result text of the protect action, with the manual steps when the token could not do it.</summary>
        public static string DescribeProtection(ProtectionResult result, ExportTarget target, RepositoryReport report)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (result.Status == ProtectionStatus.NeedsAdministration || result.Status == ProtectionStatus.NeedsActionsRead)
                return result.Message + Environment.NewLine + ManualSteps(target, report);
            return result.Message;
        }
    }
}
