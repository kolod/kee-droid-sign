using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using KeeDroidSign.Properties;
using KeeDroidSign.Storage;
using KeePass.UI;
using KeePassLib;

namespace KeeDroidSign.UI
{
    /// <summary>Lets the user pick the entry that holds the GitHub token. Shows titles and groups only.</summary>
    internal sealed class EntryPickerForm : Form
    {
        private readonly PwDatabase _database;
        private readonly TextBox _filter = new TextBox { Dock = DockStyle.Fill };
        private readonly ListView _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            Dock = DockStyle.Fill,
        };
        private readonly Button _ok = FormLayout.CreateButton(Strings.ButtonOk);
        private readonly Button _cancel = FormLayout.CreateButton(Strings.ButtonCancel);

        public EntryPickerForm(PwDatabase database)
        {
            _database = database;

            Text = Strings.PickerTitle;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            Size = new Size(560, 420);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;

            _list.Columns.Add(Strings.ColumnTitle, 220);
            _list.Columns.Add(Strings.ColumnGroup, 300);

            var top = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            top.Controls.Add(new Label { Text = Strings.PickerFilter, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            top.Controls.Add(_filter, 1, 0);

            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
            listHost.Controls.Add(_list);

            _ok.DialogResult = DialogResult.OK;
            _cancel.DialogResult = DialogResult.Cancel;
            _filter.TextChanged += (s, e) => Fill();
            _list.SelectedIndexChanged += (s, e) => _ok.Enabled = _list.SelectedItems.Count == 1;
            _list.DoubleClick += (s, e) => { if (_ok.Enabled) { DialogResult = DialogResult.OK; Close(); } };

            Controls.Add(listHost);
            Controls.Add(top);
            Controls.Add(FormLayout.CreateButtonBar(_cancel, _ok));
            AcceptButton = _ok;
            CancelButton = _cancel;

            Fill();
        }

        public PwUuid SelectedUuid
        {
            get
            {
                PwEntry entry = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as PwEntry : null;
                return entry == null ? null : entry.Uuid;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            GlobalWindowManager.AddWindow(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            GlobalWindowManager.RemoveWindow(this);
            base.OnFormClosed(e);
        }

        private void Fill()
        {
            string filter = _filter.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (PwEntry entry in _database.RootGroup.GetEntries(true)
                         .Where(e => !DroidSignStore.IsKeyEntry(e) && !DroidSignStore.IsKeystoreEntry(e)))
            {
                string title = entry.Strings.ReadSafe(PwDefs.TitleField);
                string group = entry.ParentGroup != null ? entry.ParentGroup.GetFullPath(" / ", false) : string.Empty;
                if (filter.Length > 0 &&
                    title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var item = new ListViewItem(title) { Tag = entry };
                item.SubItems.Add(group);
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _ok.Enabled = false;
        }
    }
}
