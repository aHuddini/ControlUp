# ControlUp Changelog

## Version 2.0 (January 30, 2026)

### Major Release — Overhauled for Playnite SDK 6.15 & Controller API

Completely overhauled plugin to take advantage of the Playnite SDK 6.15 update, specifically the new Controller API commands and Playnite's SDL support for Desktop Mode. The plugin should work as expected with little stability issues now. Performance is equally fast between DirectInput and XInput gamepads. Controllers should be more reliably detected so long as SDL supports them.

### What Changed

- **Playnite SDK 6.15**: Rebuilt against the latest SDK, leveraging event-based controller triggers instead of manual polling.
- **Polling removed**: Thanks to performance benefits from event-based triggers in the SDK, various polling features (including power saving / idle mode) are no longer needed and have been removed.
- **Long press reworked**: Now performed by starting a timer after the user first presses and holds a button, working around event-based presses with the SDK update. Maximum duration capped at 2 seconds.
- **Troubleshooting tab updated**: Added explanation for the "XInput Controller #1" display name issue (SDL-related, not a bug in the extension or Playnite).
- **Standardized style presets**: Both toast and pop-up notification presets now use consistent values across all themes.
- **Vignette effect (experimental)**: New optional edge-darkening effect for the pop-up notification.
- **Blur padding**: New visual parameter that extends the blur window beyond the border, giving a pleasant bleed effect. Set to 1 and combine with border opacity for best results.
- **Border opacity**: Main window border now has customizable opacity; lower values let the blur beautifully bleed through.
- **Toast notifications**: Non-blocking toasts with acrylic blur, defaulting to "Ocean Teal" to showcase the notification potential to new users.

## Version 1.0.5 (January 26, 2026)

### Features
- **SDL Hotkey Fallback for All Controllers**: Non-XInput controllers (8BitDo, PlayStation, etc.) now work with hotkeys
- **Style Presets**: Quick color scheme presets for notification appearance (8 themes)
- **Troubleshooting Tab**: In-app help for common issues (SmartScreen, controller modes, hotkeys)

### Fixes
- Fixed popup crash caused by remaining SDL_Quit() calls corrupting COM state
- Improved third-party controller support via SDL
- Fixed multi-controller button state bug causing false hotkey triggers
- Added SDL lock timeout to prevent potential deadlocks

## Version 1.0.4 (January 25, 2026)

### Stability Fixes
- **Fixed popup crash when opened multiple times** — `SDL_Quit()` was corrupting COM apartment state, breaking WPF's Text Services Framework. SDL now stays initialized for the plugin's lifetime.

### Performance
- **Idle Mode**: Reduced CPU usage when no controller is connected (configurable timeout and interval)
- **Lazy HID Initialization**: HID/DirectInput enumeration deferred until a PlayStation controller is first detected

## Version 1.0.3 (January 24, 2026)

### Features
- **Long Press Hotkey Support**: Option to require holding the hotkey (300-2000ms, default 500ms)
- **Expanded Hotkey Options**: Guide/PS combos, shoulder button combos, single button hotkeys
- **PlayStation Controller Support**: Improved DualSense and DualShock detection via SDL HIDAPI

### Fixes
- Fixed fullscreen mode switching reliability (removed race condition)
- Proper SDL and DirectInput/HID resource cleanup

## Version 1.0.1 (January 20, 2026)

### Features
- **Controller Hotkey Support**: Trigger fullscreen with button combinations (Start+RB, Start+LB, etc.)
- Background thread polling for responsive detection
- Option to skip popup and go directly to fullscreen

## Version 1.0.0 (January 19, 2026)

### Initial Release
- Controller detection popup with full gamepad navigation
- Multiple detection modes (XInput, Bluetooth, startup)
- Customizable notification appearance (position, size, blur, colors, borders)
- Live preview and color picker
- Windows Composition API acrylic blur effects
