using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KeePassLib;

namespace KeeDroidSign.Storage
{
    /// <summary>First-level groups of a database, offered as the plugin's root group.</summary>
    public static class RootGroups
    {
        /// <summary>Names of the first-level groups, sorted, without the recycle bin.</summary>
        public static IReadOnlyList<string> ListNames(PwDatabase database)
        {
            if (database == null || database.RootGroup == null) return new string[0];

            return database.RootGroup.Groups
                .Where(g => !IsRecycleBin(database, g))
                .Select(g => g.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public static bool Exists(PwDatabase database, string name)
        {
            return database != null && database.RootGroup != null && name != null &&
                   database.RootGroup.FindCreateGroup(name.Trim(), false) != null;
        }

        /// <summary>Same rule as the settings: not empty and no '/'.</summary>
        public static bool IsValidName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && !name.Contains("/");
        }

        /// <summary>Creates a first-level group and marks the database modified.</summary>
        public static PwGroup Create(PwDatabase database, string name)
        {
            if (database == null || database.RootGroup == null) throw new ArgumentNullException("database");
            if (!IsValidName(name))
                throw new ArgumentException("The root group name must not be empty or contain '/'.");
            name = name.Trim();
            if (Exists(database, name))
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "The group '{0}' already exists.", name));

            PwGroup group = database.RootGroup.FindCreateGroup(name, true);
            database.Modified = true;
            return group;
        }

        private static bool IsRecycleBin(PwDatabase database, PwGroup group)
        {
            return database.RecycleBinEnabled && group.Uuid.Equals(database.RecycleBinUuid);
        }
    }
}
