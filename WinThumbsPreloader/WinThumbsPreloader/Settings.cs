using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace WinThumbsPreloader.Properties {
    
    
    // This class allows you to handle specific events on the settings class:
    //  The SettingChanging event is raised before a setting's value is changed.
    //  The PropertyChanged event is raised after a setting's value is changed.
    //  The SettingsLoaded event is raised after the setting values are loaded.
    //  The SettingsSaving event is raised before the setting values are saved.
    internal sealed partial class Settings {
        
        private bool _isUpgrading;

        public Settings() {
            // // To add event handlers for saving and changing settings, uncomment the lines below:
            //
            // this.SettingChanging += this.SettingChangingEventHandler;
            //
            // this.SettingsSaving += this.SettingsSavingEventHandler;
            //
            EnsureSettingsUpgraded();
        }

        internal void EnsureSettingsUpgraded()
        {
            if (_isUpgrading)
                return;

            _isUpgrading = true;
            try
            {
                Version currentAssemblyVersion = typeof(Settings).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
                string currentVersionString = currentAssemblyVersion.ToString();

                if (string.Equals(this.LastUpgradedVersion, currentVersionString, StringComparison.OrdinalIgnoreCase))
                    return;

                try
                {
                    this.Upgrade();
                }
                catch
                {
                    // Ignore standard provider upgrade errors and fall back to XML migration below
                }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(localAppData))
                {
                    string appConfigRoot = Path.Combine(localAppData, "WinThumbsPreloader");
                    MigrateFromPreviousUserConfigs(appConfigRoot, currentAssemblyVersion);
                }

                this.LastUpgradedVersion = currentVersionString;
                this.Save();
            }
            catch
            {
                // Never allow settings migration to crash application startup
            }
            finally
            {
                _isUpgrading = false;
            }
        }

        internal int MigrateFromPreviousUserConfigs(string appConfigRoot, Version currentAssemblyVersion)
        {
            if (string.IsNullOrWhiteSpace(appConfigRoot) || !Directory.Exists(appConfigRoot))
                return 0;

            var candidateConfigs = new List<(Version Version, DateTime LastWriteUtc, string FilePath)>();

            try
            {
                foreach (string hashDir in Directory.EnumerateDirectories(appConfigRoot, "WinThumbsPreloader*"))
                {
                    foreach (string versionDir in Directory.EnumerateDirectories(hashDir))
                    {
                        string folderName = Path.GetFileName(versionDir);
                        if (!Version.TryParse(folderName, out Version parsedVersion))
                            continue;

                        if (currentAssemblyVersion != null && parsedVersion >= currentAssemblyVersion)
                            continue;

                        string userConfigPath = Path.Combine(versionDir, "user.config");
                        if (File.Exists(userConfigPath))
                        {
                            DateTime lastWriteUtc = File.GetLastWriteTimeUtc(userConfigPath);
                            candidateConfigs.Add((parsedVersion, lastWriteUtc, userConfigPath));
                        }
                    }
                }
            }
            catch
            {
                return 0;
            }

            if (candidateConfigs.Count == 0)
                return 0;

            int appliedCount = 0;
            foreach (var candidate in candidateConfigs
                .OrderBy(c => c.Version)
                .ThenBy(c => c.LastWriteUtc))
            {
                appliedCount += ApplyUserConfigXmlFile(candidate.FilePath);
            }

            return appliedCount;
        }

        internal int ApplyUserConfigXmlFile(string userConfigPath)
        {
            if (string.IsNullOrWhiteSpace(userConfigPath) || !File.Exists(userConfigPath))
                return 0;

            try
            {
                XDocument doc = XDocument.Load(userConfigPath);
                var settingElements = doc.Descendants("setting");
                int applied = 0;

                foreach (XElement settingElement in settingElements)
                {
                    string name = settingElement.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(name) ||
                        string.Equals(name, nameof(LastUpgradedVersion), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    SettingsProperty property = this.Properties[name];
                    if (property == null)
                        continue;

                    XElement valueElement = settingElement.Element("value");
                    if (valueElement == null)
                        continue;

                    string rawValue = valueElement.Value ?? string.Empty;
                    if (TryConvertSettingValue(property.PropertyType, rawValue, out object convertedValue))
                    {
                        this[name] = convertedValue;
                        applied++;
                    }
                }

                return applied;
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryConvertSettingValue(Type targetType, string rawValue, out object result)
        {
            result = null;
            if (targetType == typeof(string))
            {
                result = rawValue ?? string.Empty;
                return true;
            }

            if (targetType == typeof(bool))
            {
                if (bool.TryParse(rawValue?.Trim(), out bool boolVal))
                {
                    result = boolVal;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(int))
            {
                if (int.TryParse(rawValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
                {
                    result = intVal;
                    return true;
                }
                return false;
            }

            try
            {
                TypeConverter converter = TypeDescriptor.GetConverter(targetType);
                if (converter != null && converter.CanConvertFrom(typeof(string)))
                {
                    result = converter.ConvertFromInvariantString(rawValue);
                    return true;
                }
            }
            catch
            {
                // Ignore conversion failure
            }

            return false;
        }
        
        private void SettingChangingEventHandler(object sender, System.Configuration.SettingChangingEventArgs e) {
            // Add code to handle the SettingChangingEvent event here.
        }
        
        private void SettingsSavingEventHandler(object sender, System.ComponentModel.CancelEventArgs e) {
            // Add code to handle the SettingsSaving event here.
        }
    }
}
