# Playnite SDK Controller Input Reference

## Overview

Playnite 11+ introduced SDL-based controller support with new SDK callbacks for Desktop mode. This document covers the APIs used by ControlUp.

## SDK Callback: OnDesktopControllerButtonStateChanged

**Available in:** Playnite SDK with SDL support (not yet in official NuGet package)

**Purpose:** Notifies plugins when a controller button is pressed or released in Desktop mode.

### Method Signature

```csharp
public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
```

### OnControllerButtonStateChangedArgs

```csharp
public class OnControllerButtonStateChangedArgs
{
    public ControllerInput Button { get; }
    public ControllerInputState State { get; }
}
```

### ControllerInput Enum

All supported controller inputs:

```csharp
public enum ControllerInput
{
    None,

    // Face buttons
    A,                  // Xbox A / PS Cross / Nintendo B
    B,                  // Xbox B / PS Circle / Nintendo A
    X,                  // Xbox X / PS Square / Nintendo Y
    Y,                  // Xbox Y / PS Triangle / Nintendo X

    // Menu buttons
    Start,              // Start / Options / Plus
    Back,               // Back / View / Share / Minus
    Guide,              // Xbox button / PS button / Home

    // Shoulder buttons
    LeftShoulder,       // LB / L1 / L
    RightShoulder,      // RB / R1 / R

    // Thumbstick clicks
    LeftThumb,          // Left stick click / L3
    RightThumb,         // Right stick click / R3

    // D-pad
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,

    // Left stick (digital events)
    LeftStickUp,
    LeftStickDown,
    LeftStickLeft,
    LeftStickRight,

    // Right stick (digital events)
    RightStickUp,
    RightStickDown,
    RightStickLeft,
    RightStickRight,

    // Triggers (digital events when threshold crossed)
    TriggerLeft,        // LT / L2 / ZL
    TriggerRight        // RT / R2 / ZR
}
```

### ControllerInputState Enum

```csharp
public enum ControllerInputState
{
    Pressed,    // Button was just pressed
    Released    // Button was just released
}
```

## Usage Examples

### Basic Button Detection

```csharp
public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    if (args.Button == ControllerInput.A && args.State == ControllerInputState.Pressed)
    {
        // A button was pressed
    }
}
```

### Combo Detection (Multiple Buttons)

```csharp
private HashSet<ControllerInput> _pressedButtons = new HashSet<ControllerInput>();

public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    // Track button states
    if (args.State == ControllerInputState.Pressed)
        _pressedButtons.Add(args.Button);
    else
        _pressedButtons.Remove(args.Button);

    // Check for Start + RB combo
    if (_pressedButtons.Contains(ControllerInput.Start) &&
        _pressedButtons.Contains(ControllerInput.RightShoulder))
    {
        // Combo detected!
    }
}
```

### Forwarding to UI Components

```csharp
private MyDialog _activeDialog;

public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
{
    if (_activeDialog != null)
    {
        // Forward to dialog on UI thread
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _activeDialog.HandleInput(args.Button, args.State);
        });
        return; // Don't process further
    }

    // Normal processing...
}
```

## Important Notes

### 1. Desktop Mode Only

This callback ONLY fires in Desktop mode. In Fullscreen mode, Playnite handles controller input internally.

### 2. User Must Enable Controller Input

The callback only works if users enable "Controller input" in:
**Playnite Settings > General > Desktop Mode**

### 3. SDL Controller Mapping

SDL automatically maps controllers to Xbox-style inputs:
- PlayStation controllers: Cross=A, Circle=B, etc.
- Nintendo controllers: Layout is mapped to match Xbox positions

### 4. Digital Events for Analog Inputs

Stick movements and triggers generate digital events when crossing thresholds:
- `LeftStickLeft` fires when stick crosses the deadzone to the left
- `TriggerRight` fires when RT is pressed past the threshold

### 5. No Analog Values

The SDK only provides digital button events. If you need analog stick values or trigger pressure, you'll need to use XInput or another API directly.

## SDK Setup

### Using Local SDK DLL

Since the NuGet package may not include SDL support yet:

```xml
<!-- ControlUp.csproj -->
<ItemGroup>
    <Reference Include="Playnite.SDK">
        <HintPath>lib\Playnite.SDK.dll</HintPath>
        <Private>false</Private>
    </Reference>
</ItemGroup>
```

### Getting the SDK

Download a Playnite build with SDL controller support:
- Test builds may be available at: `https://playnite.link/download/`
- Extract `Playnite.SDK.dll` from the installation

## Related SDK Members

### IPlayniteAPI.ApplicationInfo

```csharp
// Check current mode
if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
{
    // Already in fullscreen, don't process Desktop callbacks
}
```

### IPlayniteAPI.Paths

```csharp
// Get Playnite installation directory
string playniteDir = PlayniteApi.Paths.ApplicationPath;
string fullscreenExe = Path.Combine(playniteDir, "Playnite.FullscreenApp.exe");
```

## Comparison with Previous Approaches

| Approach | Pros | Cons |
|----------|------|------|
| **SDK Callback** | Zero polling, no threads, handles all controllers | Requires new SDK, user must enable |
| **XInput Polling** | Direct control, works without user config | Xbox only, CPU usage, threading complexity |
| **Windows.Gaming.Input** | Bluetooth support | Complex API, UWP dependencies |
| **DirectInput** | Legacy support | Outdated, complex button mapping |

## Troubleshooting

### Callback Not Firing

1. Verify "Controller input" is enabled in Playnite Desktop settings
2. Ensure using SDK with SDL support
3. Check that you're in Desktop mode (not Fullscreen)
4. Verify controller is recognized by Windows

### Wrong Button Mapping

SDL handles mapping automatically. If buttons seem wrong:
1. Check if controller needs firmware update
2. Try recalibrating in Windows Game Controllers
3. Some third-party controllers may have non-standard mappings
