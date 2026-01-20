namespace ControlUp.Common
{
    public static class Constants
    {
        // File names
        public const string LogFileName = "ControlUp.log";

        // Directory names
        public const string PlayniteFolderName = "Playnite";
        public const string PlayniteExtensionsFolderName = "Extensions";
        public const string ExtensionFolderName = "ControlUp";

        // Controller check intervals (ms)
        public const int DefaultControllerCheckInterval = 1000;
        public const int MinControllerCheckInterval = 100;
        public const int MaxControllerCheckInterval = 10000;
    }
}
