# ControlUp Changelog

## Version 2.2.0 (January 22, 2026)

### Major Changes
- **SDK Connection Callbacks**: Using new `OnControllerConnected` and `OnControllerDisconnected` callbacks
  - No more timer-based polling for connection detection
  - Instant response to controller plug/unplug events
- **Controller Info in Events**: Button events now include controller name and ID
- **GetConnectedControllers API**: Using `PlayniteApi.GetConnectedControllers()` for listing controllers

### Removed
- `HidControllerDetector.cs` - Replaced by SDK's `GetConnectedControllers()`
- `XInputWrapper.cs` - No longer needed
- `DispatcherTimer` polling - Replaced by `OnControllerConnected` callback

### Technical
- Fully event-driven architecture with zero polling
- Logs now include which controller triggered the action

---

## Version 2.1.0 (January 22, 2026)

### Breaking Changes
- **Requires experimental Playnite test build** with Desktop mode controller support (not official releases)
- Users must enable "Controller input" in Playnite Desktop mode settings

### Major Changes
- **SDK-Based Controller Input**: Complete architectural rewrite using experimental `OnDesktopControllerButtonStateChanged` callback from test SDK
  - Zero polling overhead - events pushed by Playnite's SDL layer
  - Works with all controller types: Xbox, PlayStation, Nintendo, third-party
  - No custom threads or timers for input detection

### New Features
- **New Connection Only Mode** (Now Default): Only triggers popup on newly connected controllers
  - Avoids annoying popup when switching back from Fullscreen to Desktop
  - Recommended for most users
- **Single Button Hotkeys**: Guide, Start, or Back buttons can trigger fullscreen directly

### Improvements
- **Reliable Mode Switching**: Simply launches Fullscreen app, Playnite handles coordination internally
- **Better Enum Display**: Settings dropdowns now show descriptive text instead of enum names
- **Developer Documentation**: Added `docs/` folder with architecture and SDK reference

### Removed
- `GamingInputWrapper.cs` - No longer needed (SDL handles all controllers)
- `DirectInputWrapper.cs` - No longer needed
- `RawInputWrapper.cs` - No longer needed
- Background polling threads - Replaced by SDK callbacks

### Technical
- Experimental Playnite SDK test build with Desktop controller support (local DLL, not NuGet)
- HashSet-based button tracking for O(1) combo detection
- Event-driven architecture with no busy-waiting

---

## Version 1.0.1 (January 20, 2026)

### Features
- **Controller Hotkey Support**: Trigger fullscreen mode directly with controller button combinations
  - Multiple hotkey combinations: Start+RB, Start+LB, Back+Start, Back+RB, Back+LB
  - Background thread polling for responsive detection (50-100ms recommended)
  - Option to skip popup and go directly to fullscreen
  - Configurable polling interval (5-500ms)
- **Enhanced Settings UI**: Added dedicated hotkey configuration section

### Technical
- Background thread implementation for responsive hotkey detection
- XInput integration for button state monitoring
- Improved performance with optimized polling intervals

## Version 1.0.0 (January 19, 2026)

### Features
- **Controller Detection Popup**: Shows a customizable notification when a controller is connected
- **Full Controller Navigation**: Navigate popup with D-pad, thumbsticks (left/right), A to confirm, B to cancel
- **Multiple Detection Modes**:
  - USB/Wired Xbox controllers (XInput API)
  - Bluetooth wireless controllers (Windows.Gaming.Input)
  - Any controller type detection
  - On-startup detection options for both USB and Bluetooth
- **Customizable Notification Appearance**:
  - 7 screen position options (Top/Bottom Left/Center/Right, Center)
  - Adjustable size (width/height) and edge margin
  - Auto-close timer (5-30 seconds)
  - Windows acrylic blur effect with customizable opacity and tint color
  - Background color and opacity
  - Border color, thickness, and corner radius
- **Live Preview**: Test notification appearance from settings before saving
- **Color Picker**: Visual color selection for blur tint, background, and border colors
- **Controller Detection Test**: Button in settings to scan and display all connected controllers
- **Detailed Logging**: Optional logging for troubleshooting

### Technical
- Built with .NET Framework 4.6.2
- Playnite SDK 6.11.0.0
- XInput 1.4 for USB controller detection
- Windows.Gaming.Input for Bluetooth controller detection
- Windows Composition API for acrylic blur effects
- Supports up to 4 simultaneous XInput controllers
