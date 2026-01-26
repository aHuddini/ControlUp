# ControlUp Changelog

## Version 1.0.5 (January 26, 2026)

### Features
- **SDL Hotkey Fallback for All Controllers**: Non-XInput controllers (8BitDo, PlayStation, etc.) now work with hotkeys
  - Uses SDL's comprehensive controller database (gamecontrollerdb.txt)
  - Automatically falls back to SDL when no XInput controller is detected
- **Style Presets**: Quick color scheme presets for notification appearance
  - 8 presets: Midnight Blue, Deep Purple, Forest Green, Crimson Red, Sunset Orange, Ocean Teal, Charcoal Gray, Rose Pink
- **Troubleshooting Tab**: New settings tab with common issues and solutions
  - SmartScreen blocking fullscreen launch
  - XInput vs DirectInput mode recommendations
  - PlayStation controller limitations (DirectInput only, slight hotkey delay)
  - Hotkey troubleshooting checklist

### Fixes
- **Fixed popup crash (again)**: Removed remaining SDL_Quit() calls that were still causing COM corruption
- **Controller Detection**: Improved support for third-party controllers via SDL

### Stability & Maintenance
- Fixed multi-controller button state bug that could cause false hotkey triggers
- Added SDL lock timeout to prevent potential deadlocks
- Fixed settings change detection (all properties now compared)
- Removed dead code (~50 lines)

## Version 1.0.4 (January 25, 2026)

### Stability Fixes
- **Critical: Fixed popup crash when opened multiple times** - Resolved `InvalidCastException` on `ITfThreadMgr` that occurred when opening the popup repeatedly (e.g., via notification preview or hotkey)
  - Root cause: `SDL_Quit()` was corrupting COM apartment state, breaking WPF's Text Services Framework
  - SDL now stays initialized for the plugin's lifetime, only shutting down on application exit

### Performance Improvements
- **Idle Mode**: Reduces CPU usage when no controller is connected
  - After 30 seconds without a controller, polling interval increases from 70ms to 1000ms
  - Immediately returns to fast polling when a controller is detected
  - Configurable timeout (10-120s) and idle interval (500-5000ms) in settings
- **Lazy HID Initialization**: Reduces resource usage for users without PlayStation controllers
  - HID/DirectInput enumeration only runs every ~3.5 seconds until a PlayStation controller is first detected
  - Once detected, switches to full-speed polling for responsive hotkey detection

### Settings
- Added "Power Saving (Idle Mode)" section in settings UI
  - Enable/disable idle mode
  - Configure timeout before entering idle mode
  - Configure polling interval while in idle mode

## Version 1.0.3 (January 24, 2026)

### Features
- **Long Press Hotkey Support**: Option to require holding the hotkey instead of instant tap
  - Configurable duration (300-2000ms, default 500ms)
  - Recommended for single-button hotkeys like Guide/PS button
- **Expanded Hotkey Options**: Added many more controller button combinations
  - Guide/PS button combos (Guide+Start, Guide+Back, Guide+RB, Guide+LB)
  - Shoulder button combos (LB+RB, LB+RB+Start, LB+RB+Back)
  - Single button hotkeys (Guide, Back, Start) - best with long press enabled
- **PlayStation Controller Support**: Improved detection for DualSense and DualShock controllers via SDL HIDAPI

### Fixes
- **Fullscreen Mode Switching**: Fixed reliability issues with switching to fullscreen mode
  - Added 50ms delay after dialog closes before launching fullscreen app
  - Removed Application.Shutdown() call that caused race conditions
  - Let Playnite's internal pipe system handle mode coordination
- **Resource Cleanup**: Properly release SDL and DirectInput/HID resources when stopping monitoring

### Documentation
- Added Troubleshooting section for Windows SmartScreen blocking issues
- Updated Support section with additional log file locations

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
