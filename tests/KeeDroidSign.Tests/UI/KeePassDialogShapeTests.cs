using System.Linq;
using System.Windows.Forms;
using KeePass.Forms;
using Xunit;

namespace KeeDroidSign.Tests.UI
{
    /// <summary>
    /// Guards the one non-API assumption of the tab injection (research R3): KeePass's entry and
    /// Options dialogs contain a TabControl named "m_tabMain".
    /// </summary>
    public class KeePassDialogShapeTests
    {
        [Fact]
        public void EntryDialog_HasMainTabControl()
        {
            using (var form = new PwEntryForm())
                Assert.Single(form.Controls.Find("m_tabMain", true).OfType<TabControl>());
        }

        [Fact]
        public void OptionsDialog_HasMainTabControl()
        {
            using (var form = new OptionsForm())
                Assert.Single(form.Controls.Find("m_tabMain", true).OfType<TabControl>());
        }
    }
}
