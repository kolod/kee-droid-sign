using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KeeDroidSign.Properties;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeePassLib;
using KeePassLib.Utility;

namespace KeeDroidSign.UI
{
    /// <summary>"DroidSign" tab in KeePass's Options dialog (User Story 4).</summary>
    internal sealed class OptionsTabControl : UserControl
    {
        private readonly PluginSettings _settings;
        private readonly PwDatabase _database;
        private readonly Action<PwGroup> _groupCreated;

        private readonly ComboBox _rootGroup = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Width = 260,
        };
        private readonly Button _createGroup = FormLayout.CreateButton(Strings.ButtonCreateGroup);
        private readonly Label _groupStatus = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        private readonly Label _tokenEntry = new Label { AutoSize = true, MaximumSize = new Size(360, 0), Margin = new Padding(3, 6, 3, 3) };
        private readonly ComboBox _keySize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _validity = new NumericUpDown { Minimum = 25, Maximum = 100 };
        private readonly NumericUpDown _passwordLength = new NumericUpDown { Minimum = 8, Maximum = 128 };
        private readonly TextBox _secretKeystore = new TextBox { Width = 260 };
        private readonly TextBox _secretStorePassword = new TextBox { Width = 260 };
        private readonly TextBox _secretKeyAlias = new TextBox { Width = 260 };
        private readonly TextBox _secretKeyPassword = new TextBox { Width = 260 };
        private readonly CheckBox _saveAfterKeyChange = new CheckBox { AutoSize = true };
        private readonly TextBox _defaultCommonName = new TextBox { Width = 260 };
        private readonly TextBox _defaultOrgUnit = new TextBox { Width = 260 };
        private readonly TextBox _defaultOrganization = new TextBox { Width = 260 };
        private readonly TextBox _defaultLocality = new TextBox { Width = 260 };
        private readonly TextBox _defaultState = new TextBox { Width = 260 };
        private readonly TextBox _defaultCountry = new TextBox { MaxLength = 2, CharacterCasing = CharacterCasing.Upper, Width = 40 };
        private readonly TextBox _defaultGitHubOwner = new TextBox { Width = 260 };
        private readonly Label _error = new Label { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Color.Firebrick };

        private string _tokenUuid;

        /// <param name="settings">Current settings shown in the tab.</param>
        /// <param name="activeDatabase">Database whose first-level groups are offered; may be null.</param>
        /// <param name="groupCreated">Called after "Create group" added a group (to refresh KeePass).</param>
        public OptionsTabControl(PluginSettings settings, PwDatabase activeDatabase, Action<PwGroup> groupCreated)
        {
            _settings = settings;
            _database = activeDatabase;
            _groupCreated = groupCreated;
            AutoScroll = true;

            _rootGroup.Items.AddRange(RootGroups.ListNames(activeDatabase).Cast<object>().ToArray());
            _rootGroup.TextChanged += (s, e) => UpdateCreateGroup();
            _createGroup.Click += OnCreateGroup;
            var groupRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            groupRow.Controls.AddRange(new Control[] { _rootGroup, _createGroup });

            var select = FormLayout.CreateButton(Strings.ButtonSelect);
            var clear = FormLayout.CreateButton(Strings.ButtonClear);
            select.Enabled = activeDatabase != null;
            select.Click += OnSelectToken;
            clear.Click += (s, e) => { _tokenUuid = string.Empty; ShowToken(); };
            var tokenRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            tokenRow.Controls.AddRange(new Control[] { _tokenEntry, select, clear });

            var grid = FormLayout.CreateGrid();
            grid.Dock = DockStyle.Top;
            FormLayout.AddRow(grid, Strings.OptionsRootGroup, groupRow).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, string.Empty, _groupStatus);
            FormLayout.AddRow(grid, Strings.OptionsTokenEntry, tokenRow).Anchor = AnchorStyles.Left;
            FormLayout.AddNote(grid, activeDatabase != null ? Strings.OptionsTokenInfo : Strings.OptionsNoDatabase);
            FormLayout.AddRow(grid, Strings.LabelKeySize, _keySize).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelValidity, _validity).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.OptionsPasswordLength, _passwordLength).Anchor = AnchorStyles.Left;
            _saveAfterKeyChange.Text = Strings.OptionsSaveAfterKeyChange;
            FormLayout.AddRow(grid, string.Empty, _saveAfterKeyChange).Anchor = AnchorStyles.Left;
            FormLayout.AddNote(grid, Strings.OptionsDefaultsHeader);
            FormLayout.AddRow(grid, Strings.LabelCommonName, _defaultCommonName).Anchor = AnchorStyles.Left;
            FormLayout.AddNote(grid, Strings.OptionsDefaultCommonNameHint);
            FormLayout.AddRow(grid, Strings.LabelOrgUnit, _defaultOrgUnit).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelOrganization, _defaultOrganization).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelLocality, _defaultLocality).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelState, _defaultState).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.LabelCountry, _defaultCountry).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.OptionsGitHubOwner, _defaultGitHubOwner).Anchor = AnchorStyles.Left;
            FormLayout.AddNote(grid, Strings.OptionsSecretsHeader);
            FormLayout.AddRow(grid, Strings.OptionsSecretKeystore, _secretKeystore).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.OptionsSecretStorePassword, _secretStorePassword).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.OptionsSecretKeyAlias, _secretKeyAlias).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, Strings.OptionsSecretKeyPassword, _secretKeyPassword).Anchor = AnchorStyles.Left;
            FormLayout.AddRow(grid, string.Empty, _error);
            Controls.Add(grid);

            _keySize.Items.AddRange(new object[] { 2048, 3072, 4096 });
            _rootGroup.Text = settings.RootGroup;
            _tokenUuid = settings.TokenEntryUuid;
            _keySize.SelectedItem = settings.KeySize;
            if (_keySize.SelectedIndex < 0) _keySize.SelectedItem = 4096;
            _validity.Value = Math.Max(_validity.Minimum, Math.Min(_validity.Maximum, settings.ValidityYears));
            _passwordLength.Value = Math.Max(_passwordLength.Minimum, Math.Min(_passwordLength.Maximum, settings.PasswordLength));
            _saveAfterKeyChange.Checked = settings.SaveAfterKeyChange;
            _secretKeystore.Text = settings.SecretKeystoreBase64;
            _secretStorePassword.Text = settings.SecretStorePassword;
            _secretKeyAlias.Text = settings.SecretKeyAlias;
            _secretKeyPassword.Text = settings.SecretKeyPassword;
            _defaultCommonName.Text = settings.DefaultCommonName;
            _defaultOrgUnit.Text = settings.DefaultOrganizationalUnit;
            _defaultOrganization.Text = settings.DefaultOrganization;
            _defaultLocality.Text = settings.DefaultLocality;
            _defaultState.Text = settings.DefaultState;
            _defaultCountry.Text = settings.DefaultCountry;
            _defaultGitHubOwner.Text = settings.DefaultGitHubOwner;
            ShowToken();
            UpdateCreateGroup();
        }

        private void UpdateCreateGroup()
        {
            string name = _rootGroup.Text.Trim();
            _createGroup.Enabled = _database != null && RootGroups.IsValidName(name) && !RootGroups.Exists(_database, name);
        }

        private void OnCreateGroup(object sender, EventArgs e)
        {
            string name = _rootGroup.Text.Trim();
            try
            {
                PwGroup group = RootGroups.Create(_database, name);
                _rootGroup.Items.Add(group.Name);
                _rootGroup.Text = group.Name;
                _groupStatus.ForeColor = SystemColors.ControlText;
                _groupStatus.Text = string.Format(Strings.GroupCreated, group.Name);
                if (_groupCreated != null) _groupCreated(group);
            }
            catch (Exception ex)
            {
                _groupStatus.ForeColor = Color.Firebrick;
                _groupStatus.Text = ErrorText.For(ex);
            }
            UpdateCreateGroup();
        }

        /// <summary>Copies the inputs into a validated settings object, or returns an error message.</summary>
        public PluginSettings TryBuild(out string error)
        {
            PluginSettings result = _settings.Clone();
            result.RootGroup = _rootGroup.Text.Trim();
            result.TokenEntryUuid = _tokenUuid ?? string.Empty;
            result.KeySize = (int)_keySize.SelectedItem;
            result.ValidityYears = (int)_validity.Value;
            result.PasswordLength = (int)_passwordLength.Value;
            result.SaveAfterKeyChange = _saveAfterKeyChange.Checked;
            result.SecretKeystoreBase64 = _secretKeystore.Text.Trim();
            result.SecretStorePassword = _secretStorePassword.Text.Trim();
            result.SecretKeyAlias = _secretKeyAlias.Text.Trim();
            result.SecretKeyPassword = _secretKeyPassword.Text.Trim();
            result.DefaultCommonName = _defaultCommonName.Text.Trim();
            result.DefaultOrganizationalUnit = _defaultOrgUnit.Text.Trim();
            result.DefaultOrganization = _defaultOrganization.Text.Trim();
            result.DefaultLocality = _defaultLocality.Text.Trim();
            result.DefaultState = _defaultState.Text.Trim();
            result.DefaultCountry = _defaultCountry.Text.Trim();
            result.DefaultGitHubOwner = _defaultGitHubOwner.Text.Trim();

            try
            {
                result.Validate();
                error = null;
                _error.Text = string.Empty;
                return result;
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                _error.Text = ex.Message;
                return null;
            }
        }

        private void OnSelectToken(object sender, EventArgs e)
        {
            using (var picker = new EntryPickerForm(_database))
            {
                if (picker.ShowDialog(FindForm()) == DialogResult.OK && picker.SelectedUuid != null)
                {
                    _tokenUuid = picker.SelectedUuid.ToHexString();
                    ShowToken();
                }
            }
        }

        private void ShowToken()
        {
            if (string.IsNullOrEmpty(_tokenUuid))
            {
                _tokenEntry.Text = Strings.OptionsTokenNotSelected;
                return;
            }

            PwEntry entry = null;
            try
            {
                if (_database != null)
                    entry = _database.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(_tokenUuid)), true);
            }
            catch (ArgumentException)
            {
                entry = null;
            }

            _tokenEntry.Text = entry == null
                ? Strings.OptionsTokenNotFound
                : entry.Strings.ReadSafe(PwDefs.TitleField) + " (" +
                  (entry.ParentGroup != null ? entry.ParentGroup.GetFullPath(" / ", false) : string.Empty) + ")";
        }
    }
}
