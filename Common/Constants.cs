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

        // UniPlaySong sound integration — see SoundTriggerService. Preferred transport is an
        // in-process reflection call to TriggerExternalEvent(source, eventName); the equivalent
        // playnite://uniplaysong/{source}/{event} URI is the fallback. UniPlaySong owns the sound
        // choice, the debounce, and its own enable switch; ControlUp only decides when to ask.
        public const string UniPlaySongPluginId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
        public const string TriggerExternalEventMethod = "TriggerExternalEvent";

        // Event namespace: playnite://uniplaysong/controlup/{event}. Add new events here as they
        // are agreed with UniPlaySong (disconnect, battery-low, ...).
        public const string DetectSoundSource = "controlup";
        public const string DetectSoundEvent = "detecttrigger";

        // Controller check intervals (ms)
        public const int DefaultControllerCheckInterval = 1000;
        public const int MinControllerCheckInterval = 100;
        public const int MaxControllerCheckInterval = 10000;
    }
}
