using System.Linq;
using System.Windows.Forms;
using KeeDroidSign.UI;
using KeePass.Plugins;
using Xunit;

namespace KeeDroidSign.Tests.UI
{
    /// <summary>Automated checks of the plugin's KeePass integration points that need no running KeePass.</summary>
    public class PluginSmokeTests
    {
        [Fact]
        public void PluginClass_FollowsKeePassNamingConvention()
        {
            // KeePass instantiates "<FileName>.<FileName>Ext" from KeeDroidSign.dll.
            var type = typeof(KeeDroidSignExt);
            Assert.Equal("KeeDroidSign.KeeDroidSignExt", type.FullName);
            Assert.Equal("KeeDroidSign", type.Assembly.GetName().Name);
            Assert.True(typeof(Plugin).IsAssignableFrom(type));
        }

        [Fact]
        public void Assembly_IsMarkedAsKeePassPlugin()
        {
            // KeePass silently skips plugin assemblies without this product name (DLL and PLGX alike).
            var product = (System.Reflection.AssemblyProductAttribute)System.Attribute.GetCustomAttribute(
                typeof(KeeDroidSignExt).Assembly, typeof(System.Reflection.AssemblyProductAttribute));
            Assert.Equal("KeePass Plugin", product.Product);
        }

        [Fact]
        public void PluginVersion_MatchesCoreVersion()
        {
            // AssemblyInfo.cs (plugin, kept in source for the PLGX) and Directory.Build.props (core)
            // must be raised together, so local builds and releases report the same version.
            var plugin = typeof(KeeDroidSignExt).Assembly.GetName().Version;
            var core = typeof(KeeDroidSign.Core.Keystore.KeystoreGenerator).Assembly.GetName().Version;
            Assert.Equal(core, plugin);
        }

        [Fact]
        public void Initialize_WithoutHost_ReturnsFalse()
        {
            Assert.False(new KeeDroidSignExt().Initialize(null));
        }

        [Fact]
        public void MainMenu_HasNewKeyAndAddKey()
        {
            ToolStripMenuItem root = new KeeDroidSignExt().GetMenuItem(PluginMenuType.Main);

            Assert.Equal("DroidSign", root.Text);
            Assert.Equal(new[] { "New signing key...", "Add key to existing app..." },
                root.DropDownItems.Cast<ToolStripItem>().Select(i => i.Text));
            Assert.Null(new KeeDroidSignExt().GetMenuItem(PluginMenuType.Entry));
        }

        private sealed class FakeDialog : Form
        {
            public FakeDialog(bool withTabControl)
            {
                if (withTabControl)
                {
                    var tabs = new TabControl { Name = "m_tabMain" };
                    tabs.TabPages.Add(new TabPage("General"));
                    var panel = new Panel();
                    panel.Controls.Add(tabs);
                    Controls.Add(panel);
                }
            }

            public TabControl Tabs => Controls.Find("m_tabMain", true).OfType<TabControl>().FirstOrDefault();
        }

        [Fact]
        public void Injector_AddsTabToMatchingDialog()
        {
            using (var injector = new DialogTabInjector())
            using (var form = new FakeDialog(true))
            {
                injector.Register<FakeDialog>("DroidSign", f => new Label { Text = "content" });

                injector.Inject(form);

                Assert.Equal(new[] { "General", "DroidSign" }, form.Tabs.TabPages.Cast<TabPage>().Select(p => p.Text));
                Assert.Equal("content", form.Tabs.TabPages[1].Controls[0].Text);
            }
        }

        [Fact]
        public void Injector_SkipsWhenFactoryReturnsNull()
        {
            using (var injector = new DialogTabInjector())
            using (var form = new FakeDialog(true))
            {
                injector.Register<FakeDialog>("DroidSign", f => null);

                injector.Inject(form);

                Assert.Single(form.Tabs.TabPages);
            }
        }

        [Fact]
        public void Injector_IgnoresDialogsWithoutTabControlOrOfOtherTypes()
        {
            using (var injector = new DialogTabInjector())
            using (var noTabs = new FakeDialog(false))
            using (var other = new Form())
            {
                bool called = false;
                injector.Register<FakeDialog>("DroidSign", f => { called = true; return new Label(); });

                injector.Inject(noTabs);
                injector.Inject(other);
                injector.Inject(null);

                Assert.False(called);
            }
        }
    }
}
