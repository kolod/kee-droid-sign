using System;
using System.Text;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Properties;

namespace KeeDroidSign.Services
{
    /// <summary>
    /// Text of the confirmation shown before every export. It always names the target repository,
    /// so a key cannot be sent to an unexpected repository (e.g. a tampered URL field) by one click.
    /// </summary>
    public static class ExportConfirmation
    {
        public static string Build(string repository, ExportPlan plan)
        {
            if (plan == null) throw new ArgumentNullException("plan");

            var text = new StringBuilder();
            text.AppendLine(string.Format(Strings.ConfirmExport, repository));
            if (plan.ToCreate.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(Strings.ConfirmExportCreate);
                foreach (string name in plan.ToCreate) text.AppendLine("  " + name);
            }
            if (plan.ToOverwrite.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(Strings.ConfirmExportOverwrite);
                foreach (string name in plan.ToOverwrite) text.AppendLine("  " + name);
            }
            return text.ToString().TrimEnd();
        }
    }
}
