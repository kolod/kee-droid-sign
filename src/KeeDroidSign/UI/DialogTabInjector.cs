using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using KeePass.UI;

namespace KeeDroidSign.UI
{
    /// <summary>
    /// Adds plugin tabs to KeePass dialogs. KeePass has no tab API, so the tab control is located by
    /// its designer name (<c>m_tabMain</c>) when the dialog opens. If it is not found (a future
    /// KeePass change), nothing is added and nothing else is affected.
    /// </summary>
    internal sealed class DialogTabInjector : IDisposable
    {
        private const string TabControlName = "m_tabMain";

        private readonly List<Registration> _registrations = new List<Registration>();

        public DialogTabInjector()
        {
            GlobalWindowManager.WindowAdded += OnWindowAdded;
        }

        /// <summary>
        /// Registers a factory for forms of type <typeparamref name="TForm"/>; it returns the tab
        /// content, or null to add no tab for this particular form.
        /// </summary>
        public void Register<TForm>(string tabText, Func<TForm, Control> createContent) where TForm : Form
        {
            _registrations.Add(new Registration(
                form => form is TForm,
                tabText,
                form => createContent((TForm)form)));
        }

        public void Dispose()
        {
            GlobalWindowManager.WindowAdded -= OnWindowAdded;
            _registrations.Clear();
        }

        private void OnWindowAdded(object sender, GwmWindowEventArgs e)
        {
            Inject(e == null ? null : e.Form);
        }

        /// <summary>Adds the registered tabs to <paramref name="form"/> (separate for testability).</summary>
        internal void Inject(Form form)
        {
            if (form == null) return;

            foreach (Registration registration in _registrations.Where(r => r.Matches(form)))
            {
                TabControl tabControl = form.Controls.Find(TabControlName, true).OfType<TabControl>().FirstOrDefault();
                if (tabControl == null) return;

                Control content = registration.CreateContent(form);
                if (content == null) continue;

                var page = new TabPage(registration.TabText) { UseVisualStyleBackColor = true };
                content.Dock = DockStyle.Fill;
                page.Controls.Add(content);
                tabControl.TabPages.Add(page);
            }
        }

        private sealed class Registration
        {
            private readonly Func<Form, bool> _matches;
            private readonly string _tabText;
            private readonly Func<Form, Control> _createContent;

            public Registration(Func<Form, bool> matches, string tabText, Func<Form, Control> createContent)
            {
                _matches = matches;
                _tabText = tabText;
                _createContent = createContent;
            }

            public string TabText { get { return _tabText; } }

            public bool Matches(Form form)
            {
                return _matches(form);
            }

            public Control CreateContent(Form form)
            {
                return _createContent(form);
            }
        }
    }
}
