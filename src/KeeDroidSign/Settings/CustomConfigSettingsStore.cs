using System;
using KeePass.App.Configuration;

namespace KeeDroidSign.Settings
{
    /// <summary>Stores settings in KeePass's configuration file under the "KeeDroidSign." prefix.</summary>
    internal sealed class CustomConfigSettingsStore : ISettingsStore
    {
        private const string Prefix = "KeeDroidSign.";
        private readonly AceCustomConfig _config;

        public CustomConfigSettingsStore(AceCustomConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            _config = config;
        }

        public string Get(string key)
        {
            return _config.GetString(Prefix + key);
        }

        public void Set(string key, string value)
        {
            _config.SetString(Prefix + key, value);
        }
    }
}
