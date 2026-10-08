using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Properties;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePass.UI;
using KeePassLib.Utility;

namespace KeeDroidSign.UI
{
    /// <summary>Tools -> DroidSign -> New signing key (User Story 1).</summary>
    internal sealed class NewKeyForm : Form
    {
        private readonly KeyService _service;
        private readonly PluginSettings _settings;

        private readonly TextBox _packageId = new TextBox();
        private readonly TextBox _displayName = new TextBox();
        private readonly TextBox _repository = new TextBox();
        private readonly TextBox _commonName = new TextBox();
        private readonly TextBox _orgUnit = new TextBox();
        private readonly TextBox _organization = new TextBox();
        private readonly TextBox _locality = new TextBox();
        private readonly TextBox _state = new TextBox();
        private readonly TextBox _country = new TextBox { MaxLength = 2, CharacterCasing = CharacterCasing.Upper };
        private readonly ComboBox _keySize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _validity = new NumericUpDown { Minimum = 25, Maximum = 100 };
        private readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        private readonly ProgressBar _progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Visible = false, Height = 12 };
        private readonly Button _create = FormLayout.CreateButton(Strings.ButtonCreate);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        private CancellationTokenSource _running;
        private bool _commonNameEdited;

        public NewKeyForm(KeyService service, PluginSettings settings)
        {
            _service = service;
            _settings = settings;

            Text = Strings.NewKeyTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            var grid = FormLayout.CreateGrid();
            foreach (var box in new Control[] { _packageId, _displayName, _repository, _commonName, _orgUnit,
                _organization, _locality, _state })
                box.Width = 320;
            _country.Width = 40;

            FormLayout.AddRow(grid, Strings.LabelPackageId, _packageId);
            FormLayout.AddNote(grid, Strings.HintPackageId);
            FormLayout.AddRow(grid, Strings.LabelDisplayName, _displayName);
            FormLayout.AddRow(grid, Strings.LabelRepository, _repository);
            FormLayout.AddNote(grid, Strings.HintRepository);
            FormLayout.AddRow(grid, Strings.LabelCommonName, _commonName);
            FormLayout.AddRow(grid, Strings.LabelOrgUnit, _orgUnit);
            FormLayout.AddRow(grid, Strings.LabelOrganization, _organization);
            FormLayout.AddRow(grid, Strings.LabelLocality, _locality);
            FormLayout.AddRow(grid, Strings.LabelState, _state);
            FormLayout.AddRow(grid, Strings.LabelCountry, _country).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelKeySize, _keySize).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelValidity, _validity).Anchor = AnchorStyles.Left;
            FormLayout.AddNote(grid, Strings.PasswordsInfo);
            FormLayout.AddRow(grid, string.Empty, _progress);
            FormLayout.AddRow(grid, string.Empty, _status);

            _keySize.Items.AddRange(new object[] { 2048, 3072, 4096 });
            _keySize.SelectedItem = settings.KeySize;
            _validity.Value = Math.Max(_validity.Minimum, Math.Min(_validity.Maximum, settings.ValidityYears));

            // Defaults from Tools > Options > DroidSign. Without a default owner name the CN follows
            // the display name until the user edits it.
            DistinguishedName defaults = settings.DefaultSubject(string.Empty);
            _commonName.Text = defaults.CommonName;
            _commonNameEdited = !string.IsNullOrEmpty(defaults.CommonName);
            _orgUnit.Text = defaults.OrganizationalUnit;
            _organization.Text = defaults.Organization;
            _locality.Text = defaults.Locality;
            _state.Text = defaults.State;
            _country.Text = defaults.Country;
            _repository.Text = settings.RepositoryPrefill;
            _packageId.Text = settings.PackageIdPrefill;

            _displayName.TextChanged += (s, e) => { if (!_commonNameEdited) _commonName.Text = _displayName.Text; };
            _commonName.TextChanged += (s, e) => { if (_commonName.Focused) _commonNameEdited = true; };
            _create.Click += OnCreate;
            _cancel.Click += OnCancel;

            Controls.Add(grid);
            Controls.Add(FormLayout.CreateButtonBar(_cancel, _create));
            AcceptButton = _create;
        }

        /// <summary>Set when the dialog closed successfully.</summary>
        public KeyEntryInfo CreatedKey { get; private set; }

        /// <summary>Set when the user chose to add a key to an existing app instead.</summary>
        public string AddKeyToPackageId { get; private set; }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            GlobalWindowManager.AddWindow(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running != null) _running.Cancel();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            GlobalWindowManager.RemoveWindow(this);
            base.OnFormClosed(e);
        }

        private NewAppRequest BuildRequest()
        {
            return new NewAppRequest
            {
                PackageId = _packageId.Text.Trim(),
                DisplayName = _displayName.Text.Trim(),
                Repository = _repository.Text.Trim(),
                Subject = new DistinguishedName
                {
                    CommonName = _commonName.Text.Trim(),
                    OrganizationalUnit = _orgUnit.Text.Trim(),
                    Organization = _organization.Text.Trim(),
                    Locality = _locality.Text.Trim(),
                    State = _state.Text.Trim(),
                    Country = _country.Text.Trim(),
                },
                KeySize = (int)_keySize.SelectedItem,
                ValidityYears = (int)_validity.Value,
            };
        }

        private async void OnCreate(object sender, EventArgs e)
        {
            NewAppRequest request = BuildRequest();
            try
            {
                DroidSignStore.ValidateNewApp(request);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
                return;
            }

            SetRunning(true);
            _running = new CancellationTokenSource();
            try
            {
                CreatedKey = await _service.CreateAppAsync(request, _running.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                ShowError(Strings.StatusCancelled);
            }
            catch (InvalidOperationException)
            {
                if (MessageService.AskYesNo(string.Format(Strings.AppExistsAskAddKey, request.PackageId)))
                {
                    AddKeyToPackageId = request.PackageId;
                    DialogResult = DialogResult.Retry;
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                // Never let an async void handler take KeePass down.
                ShowError(ErrorText.For(ex));
            }
            finally
            {
                if (_running != null) _running.Dispose();
                _running = null;
                if (!IsDisposed) SetRunning(false);
            }
        }

        private void OnCancel(object sender, EventArgs e)
        {
            if (_running != null)
                _running.Cancel();
            else
                Close();
        }

        private void SetRunning(bool running)
        {
            foreach (Control c in new Control[] { _packageId, _displayName, _repository, _commonName, _orgUnit,
                _organization, _locality, _state, _country, _keySize, _validity, _create })
                c.Enabled = !running;
            _progress.Visible = running;
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = running ? Strings.StatusGenerating : string.Empty;
        }

        private void ShowError(string message)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = message;
        }
    }
}
