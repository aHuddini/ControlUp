using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace ControlUp.Common
{
    /// <summary>
    /// Asks UniPlaySong to play a sound for a ControlUp event.
    ///
    /// UniPlaySong owns everything about the sound itself — which file, the volume, the debounce,
    /// and its own per-event enable switch. ControlUp only decides *when* to ask, and for *what*.
    ///
    /// Two transports, tried in order:
    ///
    ///   1. TriggerExternalEvent(source, event) invoked by reflection — in-process, no shell hop.
    ///   2. playnite://uniplaysong/{source}/{event} — the URI, when the direct call isn't available
    ///      or doesn't recognise the event.
    ///
    /// The URI is kept as a fallback rather than deleted because the two transports fail in
    /// different ways: the direct call needs a UniPlaySong build that exposes the method AND knows
    /// the event, while the URI works against any build that registered the scheme. As ControlUp
    /// adds events (disconnect, battery-low), a newer ControlUp will inevitably run against an
    /// older UniPlaySong that returns false for the new event — the URI covers exactly that gap.
    ///
    /// Reflection rather than an assembly reference so the two plugins version independently:
    /// the whole contract is a GUID, a method name, and two strings.
    /// </summary>
    public class SoundTriggerService
    {
        private readonly IPlayniteAPI _api;
        private readonly FileLogger _logger;

        // Resolved once. Plugins can't be installed mid-session, so a miss stays a miss until
        // Playnite restarts, and GetMethod is a metadata scan we don't want on controller events.
        private bool _lookupDone;
        private Plugin _plugin;
        private MethodInfo _trigger;

        public SoundTriggerService(IPlayniteAPI api, FileLogger logger = null)
        {
            _api = api;
            _logger = logger;
        }

        /// <summary>Plays the controller-detection sound. Never throws.</summary>
        public void PlayDetectSound() => Play(Constants.DetectSoundEvent);

        /// <summary>
        /// Asks UniPlaySong to play the sound for one ControlUp event, e.g. "detecttrigger".
        /// Silently does nothing when UniPlaySong isn't installed — the sound is an optional
        /// integration, never a hard dependency.
        /// </summary>
        public void Play(string eventName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            if (TryDirectCall(eventName))
                return;

            FireUri($"playnite://uniplaysong/{Constants.DetectSoundSource}/{eventName}");
        }

        /// <returns>True if UniPlaySong handled it; false to fall back to the URI.</returns>
        private bool TryDirectCall(string eventName)
        {
            try
            {
                if (!_lookupDone)
                {
                    _lookupDone = true;
                    var id = Guid.Parse(Constants.UniPlaySongPluginId);
                    _plugin = _api?.Addons?.Plugins?.FirstOrDefault(p => p.Id == id);
                    _trigger = _plugin?.GetType().GetMethod(
                        Constants.TriggerExternalEventMethod,
                        new[] { typeof(string), typeof(string) });

                    _logger?.Info(_trigger != null
                        ? "UniPlaySong found - using direct in-process sound trigger"
                        : "UniPlaySong direct trigger unavailable - falling back to URI");
                }

                if (_plugin == null || _trigger == null)
                    return false;

                // True means recognised and routed — including when the user has the sound switched
                // off on UniPlaySong's side. False means this build doesn't know the event, so the
                // URI is worth trying instead.
                var handled = _trigger.Invoke(_plugin,
                    new object[] { Constants.DetectSoundSource, eventName });

                return handled is bool b && b;
            }
            catch (Exception ex)
            {
                // Another plugin's failure must never break ControlUp's trigger path.
                _logger?.Warn($"Direct UniPlaySong trigger failed, using URI: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Fallback transport. Fire-and-forget: nothing is read back, and a missing handler
        /// (UniPlaySong not installed) is not an error.
        /// </summary>
        private void FireUri(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            // Only ever built from playnite:// constants today. The guard stays so that routing a
            // user-supplied or imported string through here later can't turn Process.Start into
            // arbitrary program execution.
            if (!uri.TrimStart().StartsWith("playnite://", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Warn($"Ignoring URI (only playnite:// is allowed): {uri}");
                return;
            }

            // Off the UI thread: Process.Start blocks while Windows resolves the playnite:// handler,
            // and callers are on the dispatcher during controller events.
            var target = uri.Trim();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Diagnostics.Process.Start(target);
                    _logger?.Info($"Fired URI: {target}");
                }
                catch (Exception ex)
                {
                    _logger?.Error($"URI failed: {ex.Message}");
                }
            });
        }
    }
}
