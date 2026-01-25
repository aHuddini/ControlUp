# ControlUp Changelog

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
