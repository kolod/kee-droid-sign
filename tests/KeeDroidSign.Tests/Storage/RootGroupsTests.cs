using System;
using KeeDroidSign.Storage;
using KeeDroidSign.Tests.Support;
using KeePassLib;
using Xunit;

namespace KeeDroidSign.Tests.Storage
{
    public class RootGroupsTests
    {
        [Fact]
        public void ListNames_ReturnsFirstLevelGroupsSortedWithoutRecycleBin()
        {
            var pd = TestDatabase.Create();
            pd.RootGroup.FindCreateGroup("Work", true).FindCreateGroup("Nested", true);
            pd.RootGroup.FindCreateGroup("android keys", true);
            PwGroup bin = pd.RootGroup.FindCreateGroup("Recycle Bin", true);
            pd.RecycleBinEnabled = true;
            pd.RecycleBinUuid = bin.Uuid;

            Assert.Equal(new[] { "android keys", "Work" }, RootGroups.ListNames(pd));
        }

        [Fact]
        public void ListNames_NoDatabase_IsEmpty()
        {
            Assert.Empty(RootGroups.ListNames(null));
        }

        [Fact]
        public void Create_AddsFirstLevelGroupAndMarksModified()
        {
            var pd = TestDatabase.Create();

            PwGroup group = RootGroups.Create(pd, "  DroidSign  ");

            Assert.Equal("DroidSign", group.Name);
            Assert.Same(pd.RootGroup, group.ParentGroup);
            Assert.True(RootGroups.Exists(pd, "DroidSign"));
            Assert.True(pd.Modified);
        }

        [Fact]
        public void Create_ExistingOrInvalidName_IsRejected()
        {
            var pd = TestDatabase.Create();
            RootGroups.Create(pd, "DroidSign");
            pd.Modified = false;

            Assert.Throws<InvalidOperationException>(() => RootGroups.Create(pd, "DroidSign"));
            Assert.Throws<ArgumentException>(() => RootGroups.Create(pd, " "));
            Assert.Throws<ArgumentException>(() => RootGroups.Create(pd, "a/b"));
            Assert.Single(pd.RootGroup.Groups);
            Assert.False(pd.Modified);
        }
    }
}
