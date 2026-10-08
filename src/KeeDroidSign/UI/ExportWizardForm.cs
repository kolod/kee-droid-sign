using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Properties;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePassLib;

namespace KeeDroidSign.UI
{
    /// <summary>
    /// Export wizard opened by "Export to GitHub": (1) choose the target, (2) inspect the repository
    /// and choose the proposed actions, (3) run them and show the results. Clicking Run is the
    /// confirmation; nothing on GitHub changes before that.
    /// </summary>
    internal sealed class ExportWizardForm : HeaderedForm
    {
        private readonly PwDatabase _database;
        private readonly Func<PluginSettings> _settings;
        private readonly ExportService _export;
        private readonly GitHubCredential _token;
        private readonly Action<PwDatabase> _databaseChanged;
        private readonly PwEntry _keyEntry;

        // Page 1: target
        private readonly Panel _targetPage = Page();
        private readonly RadioButton _useDefault = new RadioButton { AutoSize = true };
        private readonly RadioButton _repository = new RadioButton { AutoSize = true };
        private readonly RadioButton _environment = new RadioButton { AutoSize = true };
        private readonly TextBox _environmentName = new TextBox { Width = 220 };
        private readonly Label _targetError = new Label { AutoSize = true, ForeColor = Color.Firebrick };

        // Page 2: review
        private readonly Panel _reviewPage = Page();
        private readonly Label _reviewHeader = new Label { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
        private readonly Label _reviewStatus = new Label { AutoSize = true };
        private readonly FlowLayoutPanel _findings = Column();
        private readonly FlowLayoutPanel _actions = Column();
        private readonly CheckBox _protect = new CheckBox { AutoSize = true };
        private readonly List<CheckBox> _patterns = new List<CheckBox>();
        private readonly CheckBox _exportSecrets = new CheckBox { AutoSize = true, Checked = true };
        private readonly CheckBox _cleanup = new CheckBox { AutoSize = true, Checked = true, Text = Strings.ActionCleanup };
        private readonly RadioButton _tagsLeave = new RadioButton { AutoSize = true, Text = Strings.ActionLeave };
        private readonly RadioButton _tagsRestrict = new RadioButton { AutoSize = true, Text = Strings.ActionTagsRestrict };
        private readonly RadioButton _tagsRemove = new RadioButton { AutoSize = true };
        private readonly RadioButton _branchLeave = new RadioButton { AutoSize = true, Text = Strings.ActionLeave };
        private readonly RadioButton _branchProtect = new RadioButton { AutoSize = true, Text = Strings.ActionBranchProtect };

        // Page 3: results
        private readonly Panel _resultsPage = Page();
        private readonly Label _resultsStatus = new Label { AutoSize = true };
        private readonly TextBox _results = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        };


        private readonly Button _back = FormLayout.CreateButton(Strings.ButtonBack);
        private readonly Button _next = FormLayout.CreateButton(Strings.ButtonNext);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        private KeyContext _key;
        private ExportTarget _target;
        private RepositoryReport _report;
        private CancellationTokenSource _running;
        private Panel _page;

        public ExportWizardForm(KeyContext key, PwDatabase database, Func<PluginSettings> settings, ExportService export,
            GitHubCredential token, Action<PwDatabase> databaseChanged)
            : base(Strings.WizardTitle, 660, 500)
        {
            _key = key;
            _keyEntry = key.Key.Entry;
            _database = database;
            _settings = settings;
            _export = export;
            _token = token;
            _databaseChanged = databaseChanged;

            foreach (Label label in new[] { _targetError, _reviewStatus, _resultsStatus })
                label.MaximumSize = new Size(TextWidth, 0);
            _results.Size = new Size(TextWidth, Px(300));

            BuildTargetPage();
            BuildReviewPage();
            BuildResultsPage();

            _back.Click += (s, e) => ShowPage(_targetPage);
            _next.Click += OnNext;
            _cancel.Click += (s, e) => { if (_running != null) _running.Cancel(); else Close(); };

            Content.Controls.Add(_targetPage);
            Content.Controls.Add(_reviewPage);
            Content.Controls.Add(_resultsPage);
            LayOut(_cancel, _next, _back);
            ShowPage(_targetPage);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running != null) _running.Cancel();
            base.OnFormClosing(e);
        }

        private void BuildTargetPage()
        {
            var column = Column();
            column.Dock = DockStyle.Top;

            string defaultEnvironment = _settings().DefaultEnvironment;
            _useDefault.Text = ExportWizardText.DefaultChoice(defaultEnvironment);
            _repository.Text = Strings.RadioRepositoryLevel;
            _environment.Text = Strings.RadioEnvironment;
            var environmentRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            environmentRow.Controls.AddRange(new Control[] { _environment, _environmentName });

            column.Controls.Add(_useDefault);
            column.Controls.Add(Hint(string.IsNullOrEmpty(defaultEnvironment)
                ? Strings.TargetHintDefaultRepository
                : string.Format(Strings.TargetHintDefaultEnvironment, defaultEnvironment)));
            column.Controls.Add(_repository);
            column.Controls.Add(Hint(Strings.TargetHintRepository));
            column.Controls.Add(environmentRow);
            column.Controls.Add(Hint(Strings.TargetHintEnvironment));
            column.Controls.Add(_targetError);
            Label remembered = Wrapped(Strings.TargetRemembered);
            remembered.ForeColor = SystemColors.GrayText;
            remembered.Margin = new Padding(3, Px(16), 3, 3);
            column.Controls.Add(remembered);

            ExportTargetOverride current = _key.App.TargetOverride;
            switch (current.Kind)
            {
                case ExportTargetKind.Repository: _repository.Checked = true; break;
                case ExportTargetKind.Environment:
                    _environment.Checked = true;
                    _environmentName.Text = current.Environment;
                    break;
                default: _useDefault.Checked = true; break;
            }
            // "Environment:" sits in its own row container, so WinForms does not group it with the
            // other two; keep the three choices mutually exclusive by hand.
            var choices = new[] { _useDefault, _repository, _environment };
            foreach (RadioButton choice in choices)
            {
                RadioButton selected = choice;
                selected.CheckedChanged += (s, e) =>
                {
                    if (!selected.Checked) return;
                    foreach (RadioButton other in choices)
                        if (other != selected) other.Checked = false;
                };
            }
            _environment.CheckedChanged += (s, e) => _environmentName.Enabled = _environment.Checked;
            _environmentName.Enabled = _environment.Checked;
            _targetPage.Controls.Add(column);
        }

        private void BuildReviewPage()
        {
            var column = Column();
            column.Dock = DockStyle.Top;
            column.Controls.Add(_reviewHeader);
            column.Controls.Add(_reviewStatus);
            column.Controls.Add(_findings);
            column.Controls.Add(_actions);
            _reviewPage.Controls.Add(column);
            _protect.CheckedChanged += (s, e) => UpdateActions();
            _exportSecrets.CheckedChanged += (s, e) => UpdateActions();
            foreach (RadioButton choice in new[] { _tagsLeave, _tagsRestrict, _tagsRemove, _branchLeave, _branchProtect })
                choice.CheckedChanged += (s, e) => UpdateActions();
        }

        private void BuildResultsPage()
        {
            var column = Column();
            column.Dock = DockStyle.Top;
            column.Controls.Add(_resultsStatus);
            column.Controls.Add(_results);
            _resultsPage.Controls.Add(column);
        }

        private void ShowPage(Panel page)
        {
            _page = page;
            int step = page == _targetPage ? 1 : page == _reviewPage ? 2 : 3;
            SetHeader(step == 1 ? Strings.WizardStepTarget : step == 2 ? Strings.WizardStepReview : Strings.WizardStepResults,
                string.Format(Strings.WizardStepFmt, step, 3,
                    _key.App.DisplayName + " \u2192 " + (_key.App.RepositoryWebUrl ?? _key.App.RepositoryUrl)));
            _targetPage.Visible = page == _targetPage;
            _reviewPage.Visible = page == _reviewPage;
            _resultsPage.Visible = page == _resultsPage;

            _back.Visible = page == _reviewPage;
            _next.Visible = page != _resultsPage;
            _next.Text = page == _reviewPage ? Strings.ButtonRun : Strings.ButtonNext;
            _cancel.Text = page == _resultsPage ? Strings.ButtonClose : Strings.ButtonCancel;
            AcceptButton = page == _resultsPage ? _cancel : _next;
            UpdateActions();
        }

        private void OnNext(object sender, EventArgs e)
        {
            if (_page == _targetPage) GoToReview();
            else if (_page == _reviewPage) Run();
        }

        /// <summary>Stores a changed target on the app, then inspects the repository.</summary>
        private async void GoToReview()
        {
            ExportTargetOverride chosen;
            try
            {
                chosen = _repository.Checked ? ExportTargetOverride.Repository
                    : _environment.Checked ? ExportTargetOverride.ForEnvironment(_environmentName.Text.Trim())
                    : ExportTargetOverride.Default;
            }
            catch (ArgumentException ex)
            {
                _targetError.Text = ex.Message;
                return;
            }
            _targetError.Text = string.Empty;

            if (chosen.ToFieldValue() != _key.App.TargetOverride.ToFieldValue())
            {
                var store = new DroidSignStore(_database, _settings());
                store.SetExportTarget(_key.App, chosen);
                _key = store.ResolveKey(_keyEntry);
                if (_databaseChanged != null) _databaseChanged(_database);
            }

            _target = _export.ResolveTarget(_key);
            _report = null;
            _reviewHeader.Text = string.Format(Strings.WizardReviewHeader, ExportWizardText.Target(_target));
            _reviewStatus.ForeColor = SystemColors.ControlText;
            _reviewStatus.Text = string.Empty;
            ShowBusy(true, string.Format(Strings.StatusInspecting, ExportWizardText.Target(_target)));
            _findings.Controls.Clear();
            _actions.Controls.Clear();
            _running = new CancellationTokenSource();
            ShowPage(_reviewPage);

            try
            {
                _report = await _export.InspectAsync(_key, _token, _running.Token);
                if (IsDisposed) return;
                ShowBusy(false, null);
                ShowReport();
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _reviewStatus.Text = Strings.StatusCancelled;
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                _reviewStatus.ForeColor = Color.Firebrick;
                _reviewStatus.Text = ErrorText.For(ex);
            }
            finally
            {
                if (!IsDisposed) ShowBusy(false, null);
                EndRun();
            }
        }


        private void ShowReport()
        {
            _findings.Controls.Add(Heading(Strings.WizardFindings));
            foreach (WizardFinding finding in ExportWizardText.Findings(_target, _report))
            {
                Label line = Wrapped(finding.ToString());
                if (finding.Level == FindingLevel.Warning) line.ForeColor = Color.DarkOrange;
                else if (finding.Level == FindingLevel.Blocking) line.ForeColor = Color.Firebrick;
                else if (finding.Level == FindingLevel.Ok) line.ForeColor = Color.DarkGreen;
                _findings.Controls.Add(line);
            }

            _actions.Controls.Add(Heading(Strings.WizardActions));
            _patterns.Clear();
            if (_target.IsEnvironment && _report.ProtectionProposed)
            {
                _protect.Text = ExportWizardText.ProtectAction(_target, _report);
                _protect.Checked = true;
                _actions.Controls.Add(_protect);
                foreach (DeploymentPattern pattern in _report.ProposedPatterns)
                {
                    var box = new CheckBox { AutoSize = true, Checked = true, Text = pattern.ToString(), Tag = pattern,
                        Margin = new Padding(24, 0, 3, 0) };
                    _patterns.Add(box);
                    _actions.Controls.Add(box);
                }
                Label note = Wrapped(Strings.ActionProtectNote);
                note.ForeColor = SystemColors.GrayText;
                note.Margin = new Padding(24, 0, 3, 6);
                _actions.Controls.Add(note);
            }

            AddHardeningChoices();

            _exportSecrets.Text = ExportWizardText.ExportAction(_target, _report);
            _exportSecrets.Checked = true;
            _actions.Controls.Add(_exportSecrets);

            if (_report.Copies != null && _report.Copies.Checked && _report.Copies.Names.Count > 0)
            {
                _cleanup.Checked = true;
                _actions.Controls.Add(_cleanup);
            }

            if (ExportWizardText.NeedsManualSteps(_target, _report))
            {
                Label steps = Wrapped(ExportWizardText.ManualSteps(_target, _report));
                steps.ForeColor = SystemColors.GrayText;
                _actions.Controls.Add(steps);
            }
            UpdateActions();
        }

        /// <summary>
        /// One choice per weakness that makes the environment's protection pointless: anyone with write
        /// access can create a v* tag, or push to an unprotected default branch. "Leave as is" is the default.
        /// </summary>
        private void AddHardeningChoices()
        {
            bool tagsAllowed = _report.TagCreation.HasValue && _report.TagCreation.Value != RefProtection.Protected;
            bool branchOpen = _report.DefaultBranchProtection != RefProtection.Protected;
            if (!tagsAllowed && !branchOpen) return;

            if (tagsAllowed)
            {
                _actions.Controls.Add(SubHeading(Strings.ActionTagsGroup));
                var group = ChoiceGroup();
                _tagsLeave.Checked = true;
                group.Controls.Add(_tagsLeave);
                group.Controls.Add(_tagsRestrict);
                if (_report.ExistingPatterns.Any(p => p.Type == DeploymentPattern.Tag && p.Name == RepositoryInspector.TagPattern))
                {
                    _tagsRemove.Text = string.Format(Strings.ActionTagsRemove, _target.Environment, _report.DefaultBranch);
                    group.Controls.Add(_tagsRemove);
                }
                _actions.Controls.Add(group);
            }
            if (branchOpen)
            {
                _actions.Controls.Add(SubHeading(string.Format(Strings.ActionBranchGroup, _report.DefaultBranch)));
                var group = ChoiceGroup();
                _branchLeave.Checked = true;
                group.Controls.Add(_branchLeave);
                group.Controls.Add(_branchProtect);
                _actions.Controls.Add(group);
            }
            Label note = Wrapped(Strings.ActionNeedsAdministration);
            note.ForeColor = SystemColors.GrayText;
            note.Margin = new Padding(Px(20), 0, 3, Px(8));
            _actions.Controls.Add(note);
        }

        /// <summary>Radio buttons in their own container, so each group is exclusive on its own.</summary>
        private FlowLayoutPanel ChoiceGroup()
        {
            FlowLayoutPanel group = Column();
            group.Margin = new Padding(Px(20), 0, 3, Px(4));
            return group;
        }

        private Label SubHeading(string text)
        {
            Label label = Wrapped(text);
            label.Margin = new Padding(3, Px(6), 3, 0);
            return label;
        }

        private static bool Chosen(RadioButton choice)
        {
            return choice.Parent != null && choice.Parent.Parent != null && choice.Checked;
        }

        /// <summary>The export needs an existing environment, or the create action selected.</summary>
        private void UpdateActions()
        {
            foreach (CheckBox box in _patterns) box.Enabled = _protect.Checked;
            bool blocked = _report != null && _report.EnvironmentMissing && !_protect.Checked;
            if (blocked && _exportSecrets.Enabled)
            {
                _exportSecrets.Enabled = false;
                _exportSecrets.Checked = false;
            }
            else if (!blocked && !_exportSecrets.Enabled)
            {
                _exportSecrets.Enabled = true;
                _exportSecrets.Checked = true;
            }
            // Repository-level copies are deleted only after the export, so cleanup needs the export.
            _cleanup.Enabled = _exportSecrets.Checked;

            if (_page == _reviewPage)
                _next.Enabled = _running == null && _report != null &&
                    ((_protect.Checked && _protect.Parent != null) || _exportSecrets.Checked ||
                     Chosen(_tagsRestrict) || Chosen(_tagsRemove) || Chosen(_branchProtect));
            else if (_page == _targetPage)
                _next.Enabled = _running == null;
            _back.Enabled = _running == null;
        }

        private async void Run()
        {
            if (_report == null) return;
            bool protect = _protect.Parent != null && _protect.Checked;
            IReadOnlyList<DeploymentPattern> patterns = _patterns.Where(b => b.Checked).Select(b => (DeploymentPattern)b.Tag).ToList();
            bool export = _exportSecrets.Checked;
            bool cleanup = _cleanup.Parent != null && _cleanup.Enabled && _cleanup.Checked;
            bool restrictTags = Chosen(_tagsRestrict);
            bool removeTags = Chosen(_tagsRemove);
            bool protectBranch = Chosen(_branchProtect);

            var lines = new List<string>();
            _results.Text = string.Empty;
            _resultsStatus.ForeColor = SystemColors.ControlText;
            _resultsStatus.Text = Strings.StatusRunning;
            ShowPage(_resultsPage);
            _running = new CancellationTokenSource();
            bool failed = false;
            try
            {
                bool environmentReady = !_report.EnvironmentMissing;
                if (protect)
                {
                    ProtectionResult result = await _export.ProtectEnvironmentAsync(_key, _token, patterns, _running.Token);
                    lines.Add(ExportWizardText.DescribeProtection(result, _target, _report));
                    if (result.Succeeded) environmentReady = true;
                    else failed = true;
                }

                // Repository hardening: a failure is reported but does not stop the export.
                if (removeTags)
                {
                    ProtectionResult result = await _export.RemoveTagPatternAsync(_key, _token, _running.Token);
                    lines.Add(result.Message);
                    failed |= !result.Succeeded;
                }
                if (restrictTags)
                {
                    ProtectionResult result = await _export.RestrictReleaseTagsAsync(_key, _token, _running.Token);
                    lines.Add(result.Message);
                    failed |= !result.Succeeded;
                }
                if (protectBranch)
                {
                    ProtectionResult result = await _export.ProtectDefaultBranchAsync(_key, _token, _report.DefaultBranch, _running.Token);
                    lines.Add(result.Message);
                    failed |= !result.Succeeded;
                }

                bool exported = false;
                if (export && !environmentReady)
                {
                    lines.Add(string.Format(Strings.ResultSkippedExport, _target.Environment));
                    failed = true;
                }
                else if (export)
                {
                    ExportResult result = await _export.ExportAsync(_key, _token, true, _running.Token);
                    lines.AddRange(result.Outcomes.Select(o => o.ToString()));
                    exported = result.Succeeded;
                    failed |= !result.Succeeded;
                }

                if (cleanup && exported)
                {
                    ExportResult deleted = await _export.DeleteRepositoryCopiesAsync(_key, _token, _report.Copies.Names, _running.Token);
                    lines.AddRange(deleted.Outcomes.Select(o => "repository " + o));
                    if (!deleted.Succeeded)
                    {
                        failed = true;
                        lines.Add(string.Format(Strings.WarningRepositoryCopiesKept, _target.Repository));
                    }
                }
                else if (_report.Copies != null && _report.Copies.Names.Count > 0)
                {
                    if (cleanup) lines.Add(Strings.ResultSkippedCleanup);
                    lines.Add(string.Format(Strings.WarningRepositoryCopiesKept, _target.Repository));
                }

                _resultsStatus.ForeColor = failed ? Color.Firebrick : Color.DarkGreen;
                _resultsStatus.Text = failed ? Strings.ResultFailed : Strings.ResultDone;
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _resultsStatus.Text = Strings.StatusCancelled;
            }
            catch (Exception ex)
            {
                // Never let an async void handler take KeePass down; ErrorText never shows secrets.
                if (IsDisposed) return;
                _resultsStatus.ForeColor = Color.Firebrick;
                _resultsStatus.Text = ErrorText.For(ex);
            }
            finally
            {
                if (!IsDisposed) _results.Text = string.Join(Environment.NewLine, lines);
                EndRun();
            }
        }

        private void EndRun()
        {
            if (_running != null) _running.Dispose();
            _running = null;
            if (!IsDisposed) UpdateActions();
        }

        /// <summary>A wizard page: stacked in the content area; only the current one is visible.</summary>
        private static Panel Page()
        {
            return new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        }
    }
}
