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
using KeePass.Util;
using KeePassLib.Utility;

namespace KeeDroidSign.UI
{
    /// <summary>
    /// Tools -> DroidSign -> New signing key (User Story 1): App, Certificate owner and Key, checked
    /// on Next; Create generates the key; the last step shows what the Android Developer Console
    /// needs and offers to continue with the export wizard.
    /// </summary>
    internal sealed class NewKeyForm : HeaderedForm
    {
        private const int StepCount = 4;
        private const int DoneStep = 3;

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
        private readonly Label _status = new Label { AutoSize = true, ForeColor = Color.Firebrick };
        private readonly Button _back = FormLayout.CreateButton(Strings.ButtonBack);
        private readonly Button _next = FormLayout.CreateButton(Strings.ButtonNext);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        // Last step: the created key
        private readonly TextBox _donePackageId = ReadOnlyBox();
        private readonly TextBox _doneDisplayName = ReadOnlyBox();
        private readonly TextBox _doneSha256 = ReadOnlyBox();
        private readonly CheckBox _exportNow = new CheckBox { AutoSize = true, Checked = true, Text = Strings.CheckExportNow };
        private readonly bool _exportAvailable;

        private readonly TableLayoutPanel[] _pages;
        private CancellationTokenSource _running;
        private bool _commonNameEdited;
        private int _step;

        /// <param name="service">Creates the key.</param>
        /// <param name="settings">Defaults for the fields.</param>
        /// <param name="exportAvailable">Whether a GitHub token is configured, so the export can follow.</param>
        public NewKeyForm(KeyService service, PluginSettings settings, bool exportAvailable)
            : base(Strings.NewKeyTitle, 660, 460)
        {
            _service = service;
            _settings = settings;
            _exportAvailable = exportAvailable;

            foreach (var box in new Control[] { _packageId, _displayName, _repository, _commonName, _orgUnit,
                _organization, _locality, _state })
                box.Width = Px(360);
            _country.Width = Px(48);
            _status.MaximumSize = new Size(TextWidth, 0);
            _status.Dock = DockStyle.Top;

            TableLayoutPanel app = Grid();
            AddHint(app, Strings.NewKeyIntroApp);
            FormLayout.AddRow(app, Strings.LabelPackageId, _packageId);
            AddHint(app, Strings.HintPackageId);
            FormLayout.AddRow(app, Strings.LabelDisplayName, _displayName);
            FormLayout.AddRow(app, Strings.LabelRepository, _repository);
            AddHint(app, Strings.HintRepository);

            TableLayoutPanel owner = Grid();
            AddHint(owner, Strings.NewKeyIntroOwner);
            FormLayout.AddRow(owner, Strings.LabelCommonName, _commonName);
            FormLayout.AddRow(owner, Strings.LabelOrgUnit, _orgUnit);
            FormLayout.AddRow(owner, Strings.LabelOrganization, _organization);
            FormLayout.AddRow(owner, Strings.LabelLocality, _locality);
            FormLayout.AddRow(owner, Strings.LabelState, _state);
            FormLayout.AddRow(owner, Strings.LabelCountry, _country).Anchor = AnchorStyles.Left;

            TableLayoutPanel key = Grid();
            AddHint(key, Strings.NewKeySubtitle);
            FormLayout.AddRow(key, Strings.LabelKeySize, _keySize).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(key, Strings.LabelValidity, _validity).Anchor = AnchorStyles.Left;
            AddHint(key, Strings.PasswordsInfo);

            TableLayoutPanel done = Grid();
            AddHint(done, Strings.NewKeyDoneInfo);
            _doneSha256.Font = new Font(FontFamily.GenericMonospace, Font.Size);
            foreach (TextBox box in new[] { _donePackageId, _doneDisplayName, _doneSha256 })
                box.Width = Px(440);
            FormLayout.AddRow(done, Strings.LabelPackageId, WithCopy(_donePackageId));
            FormLayout.AddRow(done, Strings.LabelDisplayName, WithCopy(_doneDisplayName));
            FormLayout.AddRow(done, Strings.LabelSha256, WithCopy(_doneSha256));
            AddHint(done, Strings.FingerprintHint);
            _exportNow.Margin = new Padding(3, Px(12), 3, 3);
            if (!exportAvailable)
            {
                _exportNow.Checked = false;
                _exportNow.Enabled = false;
            }
            FormLayout.AddRow(done, string.Empty, _exportNow);
            if (!exportAvailable) AddHint(done, Strings.NewKeyDoneNoToken);

            _pages = new[] { app, owner, key, done };

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
            _back.Click += (s, e) => ShowStep(_step - 1);
            _next.Click += OnNext;
            _cancel.Click += OnCancel;

            // Docking runs from the last added control, so the pages are added last-to-first and the
            // status label (added first) ends up below the visible page.
            Content.Controls.Add(_status);
            for (int i = _pages.Length - 1; i >= 0; i--)
                Content.Controls.Add(_pages[i]);
            LayOut(_cancel, _next, _back);
            AcceptButton = _next;
            ShowStep(0);
        }

        /// <summary>Set when the dialog closed successfully.</summary>
        public KeyEntryInfo CreatedKey { get; private set; }

        /// <summary>Set when the user chose to add a key to an existing app instead.</summary>
        public string AddKeyToPackageId { get; private set; }

        /// <summary>True when the user asked to export the new key's secrets right after this window.</summary>
        public bool ExportRequested
        {
            get { return CreatedKey != null && _exportAvailable && _exportNow.Checked; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            GlobalWindowManager.AddWindow(this);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running != null) _running.Cancel();
            // Once the key exists, closing the window in any way still reports it as created.
            if (CreatedKey != null) DialogResult = DialogResult.OK;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            GlobalWindowManager.RemoveWindow(this);
            base.OnFormClosed(e);
        }

        private void ShowStep(int step)
        {
            _step = Math.Max(0, Math.Min(StepCount - 1, step));
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].Visible = i == _step;

            string title = _step == 0 ? Strings.SectionApp : _step == 1 ? Strings.SectionOwner
                : _step == 2 ? Strings.SectionKey : Strings.NewKeyStepDone;
            SetHeader(title, string.Format(Strings.WizardStepFmt, _step + 1, StepCount, Strings.NewKeyTitle));
            _status.Text = string.Empty;
            // After the key exists there is no way back and nothing to cancel.
            _back.Visible = _step > 0 && _step < DoneStep;
            _cancel.Visible = _step < DoneStep;
            _next.Text = _step == DoneStep ? Strings.ButtonFinish : _step == DoneStep - 1 ? Strings.ButtonCreate : Strings.ButtonNext;

            Control first = _step == 0 ? (Control)_packageId : _step == 1 ? (Control)_commonName
                : _step == 2 ? (Control)_keySize : _next;
            first.Select();
        }

        private NewAppRequest BuildRequest()
        {
            return new NewAppRequest
            {
                PackageId = _packageId.Text.Trim(),
                DisplayName = _displayName.Text.Trim(),
                Repository = _repository.Text.Trim(),
                Subject = BuildSubject(),
                KeySize = (int)_keySize.SelectedItem,
                ValidityYears = (int)_validity.Value,
            };
        }

        private DistinguishedName BuildSubject()
        {
            return new DistinguishedName
            {
                CommonName = _commonName.Text.Trim(),
                OrganizationalUnit = _orgUnit.Text.Trim(),
                Organization = _organization.Text.Trim(),
                Locality = _locality.Text.Trim(),
                State = _state.Text.Trim(),
                Country = _country.Text.Trim(),
            };
        }

        /// <summary>Checks the current step; on the Key step generates the key; on the last step closes.</summary>
        private void OnNext(object sender, EventArgs e)
        {
            if (_step == DoneStep)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            try
            {
                if (_step == 0) DroidSignStore.ValidateAppFields(BuildRequest());
                else if (_step == 1) BuildSubject().Validate();
                else DroidSignStore.ValidateNewApp(BuildRequest());
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
                return;
            }

            if (_step < DoneStep - 1)
                ShowStep(_step + 1);
            else
                Create(BuildRequest());
        }

        private async void Create(NewAppRequest request)
        {
            SetRunning(true);
            _running = new CancellationTokenSource();
            try
            {
                CreatedKey = await _service.CreateAppAsync(request, _running.Token);
                ShowCreated();
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
                ShowStep(0);
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

        /// <summary>Fills the last step with the new key's details (the fingerprint for the Play Console).</summary>
        private void ShowCreated()
        {
            KeyContext key = _service.ResolveKey(CreatedKey.Entry);
            _donePackageId.Text = key.App.PackageId;
            _doneDisplayName.Text = key.App.DisplayName;
            try
            {
                _doneSha256.Text = KeyService.Describe(key).Sha256;
            }
            catch (Exception ex)
            {
                _doneSha256.Text = string.Empty;
                ShowError(string.Format(Strings.ErrorReadKey, ErrorText.For(ex)));
            }
            ShowStep(DoneStep);
        }

        private Control WithCopy(TextBox box)
        {
            Button copy = FormLayout.CreateButton(Strings.ButtonCopy);
            copy.Click += (s, e) =>
            {
                if (box.TextLength > 0 && CreatedKey != null)
                    ClipboardUtil.Copy(box.Text, false, true, CreatedKey.Entry, _service.Database, IntPtr.Zero);
            };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.Add(box);
            row.Controls.Add(copy);
            return row;
        }

        private static TextBox ReadOnlyBox()
        {
            return new TextBox { ReadOnly = true, BorderStyle = BorderStyle.FixedSingle };
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
            _back.Enabled = !running;
            _next.Enabled = !running;
            ShowBusy(running, Strings.StatusGenerating);
        }

        private void ShowError(string message)
        {
            _status.Text = message;
        }
    }
}
