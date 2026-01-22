# ControlUp Architecture

## Overview

ControlUp is a Playnite extension that switches from Desktop mode to Fullscreen mode when a game controller is detected. Version 2.x leverages an **experimental test build** of the Playnite SDK with SDL-based controller input for Desktop mode.

> **Important**: This extension uses an experimental SDK feature (`OnDesktopControllerButtonStateChanged`) that is not part of official Playnite releases. It requires a special test build of Playnite with Desktop mode controller support enabled.

## Key Innovation: SDK-Based Input

### The Problem (v1.x)

Previous versions used custom polling threads with XInput, DirectInput, and Windows.Gaming.Input APIs:
- High CPU usage from constant polling
- Complex multi-API management
- Race conditions and thread synchronization issues
- Limited controller compatibility

### The Solution (v2.x)

An experimental Playnite test build added SDL controller support with a new SDK callback:

```csharp
public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    // args.Button: ControllerInput enum (A, B, Start, DPadLeft, etc.)
    // args.State: ControllerInputState (Pressed, Released)
}
```

This callback is invoked by Playnite whenever a controller button state changes in Desktop mode, eliminating the need for custom polling.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     Playnite Desktop Mode                        │
│                                                                  │
│  ┌──────────────┐    SDL Controller    ┌──────────────────────┐ │
│  │  Controller  │ ──────────────────►  │  Playnite SDL Layer  │ │
│  │  (Any Type)  │                      │  (Built-in)          │ │
│  └──────────────┘                      └──────────┬───────────┘ │
│                                                   │              │
│                                                   ▼              │
│                              OnDesktopControllerButtonStateChanged
│                                                   │              │
│                                                   ▼              │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │                    ControlUpPlugin                          │ │
│  │                                                             │ │
│  │  ┌─────────────────┐    ┌─────────────────────────────┐   │ │
│  │  │ Button Tracking │    │     Hotkey Detection        │   │ │
│  │  │ HashSet<Input>  │───►│ IsHotkeyComboPressed()      │   │ │
│  │  └─────────────────┘    └──────────────┬──────────────┘   │ │
│  │                                        │                   │ │
│  │                                        ▼                   │ │
│  │                         ┌──────────────────────────┐       │ │
│  │                         │ TriggerFullscreenSwitch()│       │ │
│  │                         └──────────────┬───────────┘       │ │
│  │                                        │                   │ │
│  └────────────────────────────────────────┼───────────────────┘ │
│                                           │                      │
└───────────────────────────────────────────┼──────────────────────┘
                                            │
                                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                  ControllerDetectedDialog                        │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  HandleControllerInput(button, state)                     │   │
│  │  - D-pad: Navigate between Yes/Cancel                     │   │
│  │  - A: Confirm selection                                   │   │
│  │  - B: Cancel                                              │   │
│  └──────────────────────────────────────────────────────────┘   │
│                            │                                     │
│                            ▼                                     │
│                    User selects "Yes"                            │
│                            │                                     │
└────────────────────────────┼─────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                     SwitchToFullscreen()                         │
│                                                                  │
│  1. Start Playnite.FullscreenApp.exe                            │
│     --nolibupdate --startfullscreen --masterinstance            │
│                                                                  │
│  2. Application.Current.Shutdown()                               │
│     (Releases database lock)                                     │
│                                                                  │
│  3. Fullscreen app takes over as master instance                 │
└─────────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. ControlUpPlugin (ControlUp.cs)

Main plugin class that:
- Overrides `OnDesktopControllerButtonStateChanged` to receive controller events
- Tracks pressed buttons in a `HashSet<ControllerInput>` for combo detection
- Manages connection monitoring via `DispatcherTimer` for HID detection
- Handles fullscreen switching via `SwitchToFullscreen()`

**Key Method: Hotkey Detection**
```csharp
private HashSet<ControllerInput> _pressedButtons = new HashSet<ControllerInput>();

public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    if (args.State == ControllerInputState.Pressed)
        _pressedButtons.Add(args.Button);
    else
        _pressedButtons.Remove(args.Button);

    if (IsHotkeyComboPressed())
        TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey);
}

private bool IsHotkeyComboPressed()
{
    // Example: Start + RB combo
    return _pressedButtons.Contains(ControllerInput.Start) &&
           _pressedButtons.Contains(ControllerInput.RightShoulder);
}
```

### 2. Controller Detection (HidControllerDetector.cs)

Lightweight HID enumeration for connection detection:
- Uses Windows SetupAPI and HID APIs
- Filters by HID Usage Page (Generic Desktop) and Usage (Gamepad/Joystick)
- Returns controller info: Name, Manufacturer, VID/PID, Type

**Why HID instead of XInput?**
- XInput only supports Xbox controllers
- HID detects PlayStation, Nintendo, and third-party controllers
- No dependency on specific driver software

### 3. Mode Switching (SwitchToFullscreen)

Simply launches the Fullscreen app and lets Playnite handle the mode switching internally:

```csharp
private void SwitchToFullscreen()
{
    string fullscreenExe = Path.Combine(
        PlayniteApi.Paths.ApplicationPath,
        "Playnite.FullscreenApp.exe");

    if (File.Exists(fullscreenExe))
    {
        Process.Start(fullscreenExe);
    }
}
```

Playnite handles the coordination between Desktop and Fullscreen apps internally.

### 4. Dialog Controller Navigation

The popup dialog receives forwarded controller events:

```csharp
// In ControlUpPlugin
if (_activeDialog != null && _popupShowing)
{
    _activeDialog.HandleControllerInput(args.Button, args.State);
    return; // Don't process hotkeys while dialog is active
}

// In ControllerDetectedDialog
public void HandleControllerInput(ControllerInput button, ControllerInputState state)
{
    switch (button)
    {
        case ControllerInput.DPadLeft:
        case ControllerInput.DPadRight:
            // Toggle between Yes/Cancel
            _selectedIndex = _selectedIndex == 0 ? 1 : 0;
            break;
        case ControllerInput.A:
            // Confirm current selection
            break;
        case ControllerInput.B:
            // Cancel
            break;
    }
}
```

## SDK Event Reference

### ControllerInput Enum (Playnite.SDK.Events)

```csharp
public enum ControllerInput
{
    None,
    A, B, X, Y,
    Start, Back, Guide,
    LeftShoulder, RightShoulder,
    LeftThumb, RightThumb,
    DPadUp, DPadDown, DPadLeft, DPadRight,
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
    TriggerLeft, TriggerRight
}
```

### ControllerInputState Enum

```csharp
public enum ControllerInputState
{
    Pressed,
    Released
}
```

## Trigger Mode Logic

| Mode | On Startup | Runtime Monitoring |
|------|------------|-------------------|
| NewConnectionOnly | No | Yes (new connections only) |
| AnyControllerConnectedAnytime | Yes (if connected) | Yes |
| AnyControllerOnStartupOnly | Yes (if connected) | No |
| Disabled | No | No |

## File Structure

```
ControlUp/
├── ControlUp.cs                    # Main plugin, SDK callbacks
├── ControlUpSettings.cs            # Settings model, enums
├── Common/
│   ├── HidControllerDetector.cs    # HID enumeration (connection detection)
│   ├── XInputWrapper.cs            # XInput check (supplementary)
│   ├── FileLogger.cs               # Debug logging
│   ├── Constants.cs                # Shared constants
│   └── EnumDescriptionConverter.cs # WPF enum display helper
├── Views/
│   ├── ControllerDetectedDialog.xaml(.cs)  # Popup window
│   └── ControlUpSettingsView.xaml(.cs)     # Settings panel
├── ViewModels/
│   └── ControlUpSettingsViewModel.cs       # Settings logic
├── Controls/
│   └── ColorPickerButton.xaml(.cs)         # Color picker control
└── lib/
    └── Playnite.SDK.dll                    # SDK with new callbacks
```

## Prerequisites for Development

1. **Experimental Playnite SDK**: The `OnDesktopControllerButtonStateChanged` callback requires a **test build** of Playnite with experimental Desktop mode controller support. This is not available in official releases.

2. **User Configuration**: Users must enable "Controller input" in Playnite's Desktop mode settings for the SDK callback to fire.

3. **Reference the test SDK**: Use the local SDK DLL from the test build (in `lib/` folder), not the official NuGet package.

## Performance Considerations

- **No polling threads**: All input comes via SDK callbacks
- **Minimal timer usage**: Only one 500ms timer for HID connection monitoring
- **Efficient button tracking**: HashSet for O(1) combo detection
- **Event-driven architecture**: No busy-waiting or sleep loops
