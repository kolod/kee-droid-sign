using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using KeeDroidSign.Core;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Properties;
using KeeDroidSign.Services;
using KeeDroidSign.Storage;
using KeePass.UI;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace KeeDroidSign.UI
{
    /// <summary>Tools -> DroidSign -> Add key to existing app (User Story 3).</summary>
    internal sealed class AddKeyForm : HeaderedForm
    {
        private readonly KeyService _service;
        private readonly IReadOnlyList<AppKeystore> _apps;

        private readonly ComboBox _app = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        private readonly Label _number = new Label { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        private readonly TextBox _commonName = new TextBox { Width = 320 };
        private readonly TextBox _orgUnit = new TextBox { Width = 320 };
        private readonly TextBox _organization = new TextBox { Width = 320 };
        private readonly TextBox _locality = new TextBox { Width = 320 };
        private readonly TextBox _state = new TextBox { Width = 320 };
        private readonly TextBox _country = new TextBox { MaxLength = 2, CharacterCasing = CharacterCasing.Upper, Width = 40 };
        private readonly Label _status = new Label { AutoSize = true };
        private readonly Button _create = FormLayout.CreateButton(Strings.ButtonCreate);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        private CancellationTokenSource _running;

        public AddKeyForm(KeyService service, IReadOnlyList<AppKeystore> apps, string preselectPackageId)
            : base(Strings.AddKeyTitle, 660, 480)
        {
            _service = service;
            _apps = apps;
            SetHeader(Strings.AddKeyTitle, Strings.AddKeyInfo);

            TableLayoutPanel grid = Grid();
            _app.Width = Px(360);
            foreach (var box in new Control[] { _commonName, _orgUnit, _organization, _locality, _state })
                box.Width = Px(360);
            _country.Width = Px(48);
            _status.MaximumSize = new Size(TextWidth, 0);

            AddSection(grid, Strings.SectionApp);
            FormLayout.AddRow(grid, Strings.LabelApp, _app);
            FormLayout.AddRow(grid, Strings.LabelNewKeyNumber, _number);
            AddSection(grid, Strings.SectionOwner);
            AddHint(grid, Strings.AddKeyOwnerHint);
            FormLayout.AddRow(grid, Strings.LabelCommonName, _commonName);
            FormLayout.AddRow(grid, Strings.LabelOrgUnit, _orgUnit);
            FormLayout.AddRow(grid, Strings.LabelOrganization, _organization);
            FormLayout.AddRow(grid, Strings.LabelLocality, _locality);
            FormLayout.AddRow(grid, Strings.LabelState, _state);
            FormLayout.AddRow(grid, Strings.LabelCountry, _country).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, string.Empty, _status);

            foreach (AppKeystore app in apps)
                _app.Items.Add(new AppItem(app));
            _app.SelectedIndexChanged += (s, e) => OnAppChanged();
            int index = apps.ToList().FindIndex(a => a.PackageId == preselectPackageId);
            if (_app.Items.Count > 0) _app.SelectedIndex = Math.Max(0, index);

            _create.Click += OnCreate;
            _cancel.Click += (s, e) => { if (_running != null) _running.Cancel(); else Close(); };

            Content.Controls.Add(grid);
            LayOut(_cancel, _create);
            AcceptButton = _create;
        }

        public KeyEntryInfo CreatedKey { get; private set; }

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

        private AppKeystore SelectedApp
        {
            get
            {
                var item = _app.SelectedItem as AppItem;
                return item == null ? null : item.App;
            }
        }

        private void OnAppChanged()
        {
            AppKeystore app = SelectedApp;
            if (app == null) return;
            _status.Text = string.Empty;
            try
            {
                byte[] keystore = app.ReadKeystore();
                _number.Text = KeyService.NextKeyNumber(app, keystore).ToString();
                PrefillOwner(app, keystore);
                _create.Enabled = true;
            }
            catch (Exception ex)
            {
                _number.Text = "-";
                _create.Enabled = false;
                ShowError(string.Format(Strings.ErrorReadKey, ErrorText.For(ex)));
            }
        }

        /// <summary>Copies the owner of the newest existing key, so all keys of an app look alike.</summary>
        private void PrefillOwner(AppKeystore app, byte[] keystore)
        {
            string alias = KeystoreReader.ListKeyAliases(keystore, app.StorePassword)
                .OrderByDescending(AliasNumber)
                .FirstOrDefault();
            if (alias == null) return;

            X509Name subject = new X509Certificate(KeystoreReader.GetCertificate(keystore, alias, app.StorePassword)).SubjectDN;
            _commonName.Text = Read(subject, X509Name.CN);
            _orgUnit.Text = Read(subject, X509Name.OU);
            _organization.Text = Read(subject, X509Name.O);
            _locality.Text = Read(subject, X509Name.L);
            _state.Text = Read(subject, X509Name.ST);
            _country.Text = Read(subject, X509Name.C);
        }

        private static int AliasNumber(string alias)
        {
            int n;
            return int.TryParse(alias, out n) ? n : 0;
        }

        private static string Read(X509Name name, DerObjectIdentifier oid)
        {
            return name.GetValueList(oid).FirstOrDefault() ?? string.Empty;
        }

        private async void OnCreate(object sender, EventArgs e)
        {
            AppKeystore app = SelectedApp;
            if (app == null) return;

            var subject = new DistinguishedName
            {
                CommonName = _commonName.Text.Trim(),
                OrganizationalUnit = _orgUnit.Text.Trim(),
                Organization = _organization.Text.Trim(),
                Locality = _locality.Text.Trim(),
                State = _state.Text.Trim(),
                Country = _country.Text.Trim(),
            };
            try
            {
                subject.Validate();
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
                CreatedKey = await _service.AddKeyAsync(app, subject, _running.Token);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (OperationCanceledException)
            {
                ShowError(Strings.StatusCancelled);
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

        private void SetRunning(bool running)
        {
            foreach (Control c in new Control[] { _app, _commonName, _orgUnit, _organization, _locality, _state, _country, _create })
                c.Enabled = !running;
            ShowBusy(running, Strings.StatusGenerating);
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = string.Empty;
        }

        private void ShowError(string message)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = message;
        }

        private sealed class AppItem
        {
            private readonly AppKeystore _app;

            public AppItem(AppKeystore app) { _app = app; }

            public AppKeystore App { get { return _app; } }

            public override string ToString()
            {
                return _app.PackageId + " - " + _app.DisplayName;
            }
        }
    }
}
