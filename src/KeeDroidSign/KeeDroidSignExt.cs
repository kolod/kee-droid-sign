using System;
using System.Windows.Forms;
using KeeDroidSign.Properties;
using KeeDroidSign.Services;
using KeeDroidSign.Settings;
using KeeDroidSign.Storage;
using KeeDroidSign.UI;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.UI;
using KeePassLib;
using KeePassLib.Collections;
using KeePassLib.Utility;

namespace KeeDroidSign
{
    /// <summary>KeePass plugin entry point (KeePass instantiates <c>KeeDroidSign.KeeDroidSignExt</c>).</summary>
    public sealed class KeeDroidSignExt : Plugin
    {
        private IPluginHost _host;
        private DialogTabInjector _tabs;

        public override bool Initialize(IPluginHost host)
        {
            if (host == null) return false;
            _host = host;
            _tabs = new DialogTabInjector();
            _tabs.Register<PwEntryForm>(Strings.TabTitle, CreateEntryTab);
            _tabs.Register<OptionsForm>(Strings.TabTitle, CreateOptionsTab);
            return true;
        }

        public override void Terminate()
        {
            if (_tabs != null) _tabs.Dispose();
            _tabs = null;
            _host = null;
        }

        public override ToolStripMenuItem GetMenuItem(PluginMenuType t)
        {
            if (t != PluginMenuType.Main) return null;

            var root = new ToolStripMenuItem(Strings.MenuRoot);
            var newKey = new ToolStripMenuItem(Strings.MenuNewKey);
            var addKey = new ToolStripMenuItem(Strings.MenuAddKey);
            newKey.Click += (s, e) => ShowNewKey();
            addKey.Click += (s, e) => ShowAddKey(null);
            root.DropDownItems.Add(newKey);
            root.DropDownItems.Add(addKey);

            root.DropDownOpening += (s, e) =>
            {
                PwDatabase db = ActiveDatabase;
                newKey.Enabled = db != null;
                addKey.Enabled = db != null && new DroidSignStore(db, LoadSettings()).ListApps().Count > 0;
            };
            return root;
        }

        internal PluginSettings LoadSettings()
        {
            return PluginSettings.Load(new CustomConfigSettingsStore(_host.CustomConfig));
        }

        private PwDatabase ActiveDatabase
        {
            get
            {
                PwDatabase db = _host != null && _host.MainWindow != null ? _host.MainWindow.ActiveDatabase : null;
                return db != null && db.IsOpen ? db : null;
            }
        }

        private void ShowNewKey()
        {
            PwDatabase db = ActiveDatabase;
            if (db == null) { MessageService.ShowWarning(Strings.ErrorNoDatabase); return; }

            var form = new NewKeyForm(new KeyService(db, LoadSettings), LoadSettings());
            DialogResult result = form.ShowDialog(_host.MainWindow);
            KeyEntryInfo created = form.CreatedKey;
            string addKeyTo = form.AddKeyToPackageId;
            UIUtil.DestroyForm(form);

            if (result == DialogResult.OK && created != null)
                ShowInDatabase(db, created.Entry);
            else if (result == DialogResult.Retry && addKeyTo != null)
                ShowAddKey(addKeyTo);
        }

        private void ShowAddKey(string preselectPackageId)
        {
            PwDatabase db = ActiveDatabase;
            if (db == null) { MessageService.ShowWarning(Strings.ErrorNoDatabase); return; }

            var apps = new DroidSignStore(db, LoadSettings()).ListApps();
            if (apps.Count == 0) { MessageService.ShowInfo(Strings.NoAppsYet); return; }

            var form = new AddKeyForm(new KeyService(db, LoadSettings), apps, preselectPackageId);
            DialogResult result = form.ShowDialog(_host.MainWindow);
            KeyEntryInfo created = form.CreatedKey;
            UIUtil.DestroyForm(form);

            if (result == DialogResult.OK && created != null)
                ShowInDatabase(db, created.Entry);
        }

        private Control CreateEntryTab(PwEntryForm form)
        {
            PwEntry entry = form.EntryRef;
            if (!DroidSignStore.IsKeyEntry(entry)) return null;

            PwDatabase db = _host.MainWindow.DocumentManager.FindContainerOf(entry) ?? ActiveDatabase;
            if (db == null) return null;
            return new EntryTabControl(entry, db, LoadSettings, new ExportService(LoadSettings));
        }

        private Control CreateOptionsTab(OptionsForm form)
        {
            var tab = new OptionsTabControl(LoadSettings(), ActiveDatabase,
                group => _host.MainWindow.UpdateUI(false, null, true, group, false, null, true));
            form.FormClosing += (s, e) =>
            {
                if (form.DialogResult != DialogResult.OK) return;

                string error;
                PluginSettings updated = tab.TryBuild(out error);
                if (updated == null)
                    MessageService.ShowWarning(string.Format(Strings.OptionsNotSaved, error));
                else
                    updated.Save(new CustomConfigSettingsStore(_host.CustomConfig));
            };
            return tab;
        }

        /// <summary>Shows the new key entry and, if enabled in the settings, saves the database.</summary>
        private void ShowInDatabase(PwDatabase db, PwEntry entry)
        {
            _host.MainWindow.UpdateUI(false, null, true, entry.ParentGroup, true, null, true);
            var list = new PwObjectList<PwEntry>();
            list.Add(entry);
            _host.MainWindow.SelectEntries(list, true, true);
            _host.MainWindow.EnsureVisibleEntry(entry.Uuid);

            // KeePass's own save (public for plugins): synchronization, triggers and error messages
            // behave exactly like File > Save.
            if (LoadSettings().SaveAfterKeyChange)
                _host.MainWindow.SaveDatabase(db, null);
        }
    }
}
