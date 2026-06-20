using System;
using System.IO;
using Playnite.SDK.Data;

namespace ControlUp.Common
{
    /// <summary>
    /// UI-free helper that serializes ControlUp settings to a versioned JSON
    /// wrapper and parses/validates that wrapper back into settings.
    /// </summary>
    public static class SettingsBackupService
    {
        public const string AppName = "ControlUp";

        private class BackupFile
        {
            public string App { get; set; }
            public string Version { get; set; }
            public ControlUpSettings Settings { get; set; }
        }

        /// <summary>Serializes settings into a versioned backup JSON string.</summary>
        public static string Export(ControlUpSettings settings, string version)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var backup = new BackupFile
            {
                App = AppName,
                Version = version ?? string.Empty,
                Settings = settings
            };
            return Serialization.ToJson(backup, true);
        }

        /// <summary>
        /// Parses and validates a backup JSON string. Throws InvalidDataException
        /// if it is not a valid ControlUp backup.
        /// </summary>
        public static ControlUpSettings Import(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("Backup file is empty.");

            BackupFile backup;
            try
            {
                backup = Serialization.FromJson<BackupFile>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Backup file is not valid JSON.", ex);
            }

            if (backup == null || !string.Equals(backup.App, AppName, StringComparison.Ordinal))
                throw new InvalidDataException("Not a ControlUp backup file.");

            if (backup.Settings == null)
                throw new InvalidDataException("Backup file contains no settings.");

            return backup.Settings;
        }
    }
}
