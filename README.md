# ControlUp

![Version](https://img.shields.io/badge/version-1.0.1-blue.svg) ![License](https://img.shields.io/badge/license-MIT-green.svg)

<p align="center">
  <img src="icon.png" alt="ControlUp Icon" width="128" height="128">
</p>

A Playnite extension that detects game controller connections and gamepad hotkeys, displaying a customizable popup prompting users to switch to fullscreen mode.

Includes the option to skip the pop-up to let the user directly switch to fullscreen.

<img width="548" height="440" alt="image" src="https://github.com/user-attachments/assets/09a615c7-a52d-49c2-a746-95148ff7b0dc" />

*Currently Tested on an Xbox Series X USB/Wireless Controller*

## Features

- **Controller Detection Popup**: When a controller is connected, a customizable popup appears asking if you want to switch to fullscreen mode
- **Controller Navigation Support**: Navigate the popup using your controller - D-pad, thumbsticks, A to confirm, B to cancel
- **Controller Hotkey Support**: Trigger request to go to fullscreen mode directly with controller button combinations (Start+RB, Start+LB, etc.)
- **Multiple Detection Modes**:
  - USB/Wired Xbox controllers (XInput)
  - Bluetooth wireless controllers (Windows.Gaming.Input)
  - Any controller type
  - On-startup detection options
- **Fully Customizable Notification**:
  - Position (7 screen positions)
  - Size and timing
  - Acrylic blur effect with tint color
  - Background color and opacity
  - Border styling and corner radius
- **Live Preview**: Test your notification UI settings before saving

## Installation

1. Download the latest `.pext` file from the [Releases](https://github.com/yourusername/ControlUp/releases) page
2. Double-click the `.pext` file to install, or drag it into Playnite
3. Restart Playnite when prompted
4. Configure the extension in Settings > Extensions > ControlUp

## Configuration

### General Settings

- **Controller Type to Detect**: Choose which controller type triggers the popup
  - Disabled - No controller detection
  - USB Controller - Detect USB/wired Xbox controllers
  - Bluetooth Controller - Detect Bluetooth wireless controllers
  - Any Controller - Detect any controller type
  - USB/Bluetooth On Startup - Check only when Playnite starts

- **Enable Logging**: Turn on detailed logging for troubleshooting
- **Detect Controllers**: Test button to see which controllers are currently connected

### Hotkey Settings

- **Enable Controller Hotkey**: Enable/disable controller hotkey to trigger fullscreen
- **Hotkey Combination**: Choose button combination (Start+RB, Start+LB, Back+Start, Back+RB, Back+LB)
- **Skip Popup on Hotkey**: Go directly to fullscreen without showing popup when hotkey is pressed
- **Polling Interval**: How often to check for hotkey press (50-100ms recommended for responsiveness)

### Notification Settings

- **Position**: Choose where the notification appears (Top Left/Center/Right, Center, Bottom Left/Center/Right)
- **Edge Margin**: Distance from screen edge
- **Size**: Width and height of the notification
- **Timing**: Auto-close duration (5-30 seconds)
- **Blur Effect**: Enable/disable acrylic blur, adjust opacity and tint color
- **Visual Style**: Background color/opacity, border color/thickness, corner radius

## Requirements

- Playnite 10 or later
- Windows 10 or later (for acrylic blur effect)
- Xbox controller or compatible gamepad


## Development

### Building

1. Clone the repository
2. Open `ControlUp.sln` in Visual Studio
3. Restore NuGet packages
4. Build in Release configuration

### Packaging

Run the packaging script:
```powershell
.\package_extension.ps1
```

The `.pext` file will be created in the `pext` folder.

## License

MIT License - see [LICENSE](LICENSE) file for details

## Support

If you encounter issues or have suggestions:

1. Enable logging in the extension settings
2. Check the log file in the extension folder
3. Create an issue with detailed information about your setup

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history.
