using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeeDroidSign.Core;
using KeeDroidSign.Core.GitHub;
using KeeDroidSign.Properties;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePass.Util;
using KeePassLib;
using KeePassLib.Utility;

namespace KeeDroidSign.UI
{
    /// <summary>
    /// "DroidSign" tab of the entry dialog for key entries (User Story 2): certificate details,
    /// fingerprints with copy buttons, and export of the key's secrets to GitHub.
    /// </summary>
    internal sealed class EntryTabControl : UserControl
    {
        private readonly PwEntry _entry;
        private readonly PwDatabase _database;
        private readonly Func<PluginSettings> _settings;
        private readonly ExportService _export;

        private readonly Label _number = Value();
        private readonly Label _owner = Value();
        private readonly Label _valid = Value();
        private readonly TextBox _displayName = Fingerprint();
        private readonly TextBox _packageId = Fingerprint();
        private readonly TextBox _sha256 = Fingerprint();
        private readonly LinkLabel _repository = new LinkLabel
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(3, 6, 3, 3),
            Anchor = AnchorStyles.Left,
        };
        private readonly Label _warning = new Label { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Color.DarkOrange };
        private readonly Button _exportButton = FormLayout.CreateButton(Strings.ButtonExport);
        private readonly Label _exportStatus = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        private readonly TextBox _results = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 90, Visible = false,
        };

        private KeyContext _key;
        private CancellationTokenSource _running;

        public EntryTabControl(PwEntry entry, PwDatabase database, Func<PluginSettings> settings, ExportService export)
        {
            _entry = entry;
            _database = database;
            _settings = settings;
            _export = export;

            AutoScroll = true;
            var grid = FormLayout.CreateGrid();
            grid.Dock = DockStyle.Top;

            FormLayout.AddRow(grid, Strings.LabelKeyNumber, _number);
            FormLayout.AddRow(grid, Strings.LabelOwner, _owner);
            FormLayout.AddRow(grid, Strings.LabelValid, _valid);
            FormLayout.AddRow(grid, Strings.LabelDisplayName, WithCopy(_displayName));
            FormLayout.AddRow(grid, Strings.LabelPackageId, WithCopy(_packageId));
            FormLayout.AddRow(grid, Strings.LabelSha256, WithCopy(_sha256));
            FormLayout.AddNote(grid, Strings.FingerprintHint);
            FormLayout.AddRow(grid, Strings.LabelRepositoryShort, WithButton(_repository, _exportButton));
            FormLayout.AddRow(grid, string.Empty, _exportStatus);
            FormLayout.AddRow(grid, string.Empty, _results);
            FormLayout.AddRow(grid, string.Empty, _warning);

            _exportButton.Click += OnExport;
            _repository.LinkClicked += OnRepositoryClicked;
            Controls.Add(grid);

            LoadKey();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _running != null) _running.Cancel();
            base.Dispose(disposing);
        }

        private void LoadKey()
        {
            var settings = _settings();
            _key = new DroidSignStore(_database, settings).ResolveKey(_entry);
            _number.Text = _key.Alias ?? "-";
            _displayName.Text = _key.App.DisplayName;
            _packageId.Text = _key.App.PackageId;
            ShowRepository();

            var warnings = new List<string>(_key.App.Warnings);

            try
            {
                KeyDetails details = KeyService.Describe(_key);
                _owner.Text = details.Subject;
                _valid.Text = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd} - {1:yyyy-MM-dd}",
                    details.NotBefore, details.NotAfter);
                _sha256.Text = details.Sha256;
            }
            catch (Exception ex)
            {
                warnings.Add(string.Format(Strings.ErrorReadKey, ErrorText.For(ex)));
            }

            _warning.Text = string.Join(Environment.NewLine, warnings);
            UpdateExportAvailability();
        }

        private void UpdateExportAvailability()
        {
            if (_key.Problems.Count > 0)
            {
                _exportButton.Enabled = false;
                _exportStatus.Text = string.Format(Strings.ExportDisabledProblems,
                    string.Join("; ", _key.Problems.Select(Describe)));
            }
            else if (_export.ResolveToken(_database) == null)
            {
                _exportButton.Enabled = false;
                _exportStatus.Text = Strings.ExportDisabledToken;
            }
            else
            {
                _exportButton.Enabled = true;
                _exportStatus.Text = string.Empty;
            }
        }

        private async void OnExport(object sender, EventArgs e)
        {
            GitHubCredential token = _export.ResolveToken(_database);
            if (token == null) { UpdateExportAvailability(); return; }

            string repository = _key.App.Repository.ToString();
            _exportButton.Enabled = false;
            _results.Visible = false;
            _exportStatus.ForeColor = SystemColors.ControlText;
            _exportStatus.Text = string.Format(Strings.StatusExporting, repository);
            _running = new CancellationTokenSource();
            try
            {
                ExportPlan plan = await _export.PlanAsync(_key, token, _running.Token);
                bool overwrite = false;
                if (plan.ToOverwrite.Count > 0)
                {
                    overwrite = MessageService.AskYesNo(string.Format(Strings.AskOverwrite, repository,
                        Environment.NewLine, string.Join(Environment.NewLine, plan.ToOverwrite)));
                    if (!overwrite)
                    {
                        _exportStatus.Text = Strings.ExportCancelledByUser;
                        return;
                    }
                }

                ExportResult result = await _export.ExportAsync(_key, token, overwrite, _running.Token);
                _exportStatus.ForeColor = result.Succeeded ? Color.DarkGreen : Color.Firebrick;
                _exportStatus.Text = string.Format(Strings.ExportDone, repository);
                _results.Text = string.Join(Environment.NewLine, result.Outcomes.Select(o => o.ToString()));
                _results.Visible = true;
            }
            catch (OperationCanceledException)
            {
                _exportStatus.Text = Strings.StatusCancelled;
            }
            catch (Exception ex)
            {
                // Never let an async void handler take KeePass down; ErrorText never shows secrets.
                _exportStatus.ForeColor = Color.Firebrick;
                _exportStatus.Text = ErrorText.For(ex);
            }
            finally
            {
                if (_running != null) _running.Dispose();
                _running = null;
                if (!IsDisposed) _exportButton.Enabled = true;
            }
        }

        /// <summary>Shows the repository as a link to its GitHub page, or as plain text if it is not one.</summary>
        private void ShowRepository()
        {
            string url = _key.App.RepositoryWebUrl;
            _repository.Text = url ?? _key.App.RepositoryUrl;
            _repository.Links.Clear();
            if (url != null)
                _repository.Links.Add(0, url.Length, url);
        }

        private void OnRepositoryClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var url = e.Link.LinkData as string;
            if (url == null) return;
            try
            {
                // Shell execute opens the URL in the system's default browser.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                e.Link.Visited = true;
            }
            catch (Exception ex)
            {
                _exportStatus.ForeColor = Color.Firebrick;
                _exportStatus.Text = string.Format(Strings.ErrorOpenBrowser, ex.Message);
            }
        }

        /// <summary>A value on the left and a button on the right, in one row.</summary>
        private static Control WithButton(Control value, Button button)
        {
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.Controls.Add(value, 0, 0);
            row.Controls.Add(button, 1, 0);
            return row;
        }

        private Control WithCopy(TextBox box)
        {
            var copy = FormLayout.CreateButton(Strings.ButtonCopy);
            copy.Click += (s, e) =>
            {
                if (box.TextLength > 0)
                    ClipboardUtil.Copy(box.Text, false, true, _entry, _database, IntPtr.Zero);
            };
            var row = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.Dock = DockStyle.Fill;
            row.Controls.Add(box, 0, 0);
            row.Controls.Add(copy, 1, 0);
            return row;
        }

        private static string Describe(KeyProblem problem)
        {
            switch (problem)
            {
                case KeyProblem.MissingKeystoreEntry: return Strings.ProblemMissingKeystoreEntry;
                case KeyProblem.MissingKeystoreFile: return Strings.ProblemMissingKeystoreFile;
                case KeyProblem.MissingRepository: return Strings.ProblemMissingRepository;
                case KeyProblem.InvalidRepository: return Strings.ProblemInvalidRepository;
                default: return Strings.ProblemMissingKeyNumber;
            }
        }

        private static Label Value()
        {
            return new Label { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 6, 3, 3) };
        }

        private static TextBox Fingerprint()
        {
            return new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(FontFamily.GenericMonospace, SystemFonts.MessageBoxFont.Size),
                Width = 460,
            };
        }
    }
}
