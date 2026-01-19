using System;

namespace ControlUp.Common
{
    /// <summary>
    /// Centralized constants for ControlUp extension
    /// </summary>
    public static class Constants
    {
        #region File Names

        /// <summary>
        /// Log file name
        /// </summary>
        public const string LogFileName = "ControlUp.log";

        #endregion

        #region Directory Names

        /// <summary>
        /// Playnite application folder name
        /// </summary>
        public const string PlayniteFolderName = "Playnite";

        /// <summary>
        /// Playnite extensions folder name
        /// </summary>
        public const string PlayniteExtensionsFolderName = "Extensions";

        /// <summary>
        /// Extension folder name
        /// </summary>
        public const string ExtensionFolderName = "ControlUp";

        #endregion

        #region Controller Settings

        /// <summary>
        /// Default controller check interval in milliseconds
        /// </summary>
        public const int DefaultControllerCheckInterval = 1000;

        /// <summary>
        /// Minimum controller check interval in milliseconds
        /// </summary>
        public const int MinControllerCheckInterval = 100;

        /// <summary>
        /// Maximum controller check interval in milliseconds
        /// </summary>
        public const int MaxControllerCheckInterval = 10000;

        #endregion
    }
}