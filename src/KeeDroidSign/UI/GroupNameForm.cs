using System;
using System.Drawing;
using System.Windows.Forms;
using KeeDroidSign.Properties;
using KeeDroidSign.Storage;
using KeePassLib;

namespace KeeDroidSign.UI
{
    /// <summary>Asks for the name of a new first-level group (Options -> DroidSign -> Create group).</summary>
    internal sealed class GroupNameForm : HeaderedForm
    {
        private readonly PwDatabase _database;
        private readonly TextBox _name = new TextBox();
        private readonly Label _error = new Label { AutoSize = true, ForeColor = Color.Firebrick };
        private readonly Button _ok = FormLayout.CreateButton(Strings.ButtonCreate);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        public GroupNameForm(PwDatabase database)
            : base(Strings.CreateGroupTitle, 440, 230)
        {
            _database = database;
            SetHeader(Strings.CreateGroupTitle, Strings.CreateGroupInfo);

            _name.Width = Px(260);
            _error.MaximumSize = new Size(TextWidth, 0);
            TableLayoutPanel grid = Grid();
            FormLayout.AddRow(grid, Strings.LabelGroupName, _name);
            FormLayout.AddRow(grid, string.Empty, _error);
            Content.Controls.Add(grid);

            _ok.Click += OnOk;
            _cancel.DialogResult = DialogResult.Cancel;
            LayOut(_cancel, _ok);
            AcceptButton = _ok;
            CancelButton = _cancel;
        }

        /// <summary>The trimmed name, set when the dialog closes with OK.</summary>
        public string GroupName { get; private set; }

        private void OnOk(object sender, EventArgs e)
        {
            string name = _name.Text.Trim();
            if (!RootGroups.IsValidName(name))
            {
                _error.Text = Strings.ErrorGroupName;
                return;
            }
            if (RootGroups.Exists(_database, name))
            {
                _error.Text = string.Format(Strings.ErrorGroupExists, name);
                return;
            }
            GroupName = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
