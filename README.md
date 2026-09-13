# Codex Launcher for Windows

A tiny launcher for the packaged ChatGPT/Codex Windows app, with a custom shortcut icon.

<img src="assets/Codex.svg" alt="Codex Launcher icon" width="128" />

## Features

- Automatically detects the installed app and opens it.
- Applies the custom taskbar icon to the app's current windows after launch.
- Creates desktop or Start menu shortcuts on request, with a custom icon and matching taskbar identity.
- Embeds the icon in a single executable.
- Replaces the existing production app's tray image with the same Codex icon, preserving the official menu and click actions.
- Records startup diagnostics and warns when taskbar icon properties cannot be verified.

## Build

Run `build.cmd` to generate `dist\Codex.exe`.

## Use

1. Place `Codex.exe` in a permanent folder.
2. Run it to open the app.
3. Optionally create a shortcut using one of the commands below. You can pin it to the taskbar.

```powershell
.\Codex.exe --create-shortcut desktop
.\Codex.exe --create-shortcut start-menu
```

To customize the icon, replace `assets/Codex.ico` and run `build.cmd` again. `assets/Codex.svg` is provided as editable artwork; after editing it, export an ICO to replace `assets/Codex.ico`. The build uses only the ICO file.

The launcher waits up to 4 seconds for a visible app window, applies taskbar properties immediately, verifies them, and exits. Startup logs are saved in `%LOCALAPPDATA%\CodexLauncher\logs` (normally the latest 20 runs). If the icon looks wrong, run `Codex.exe --diagnose` before relaunching to save a read-only window snapshot to `%LOCALAPPDATA%\CodexLauncher\diagnostics.txt`. Property verification does not verify the icon actually rendered by Windows.

Taskbar and tray use the same embedded `assets/Codex.ico` regardless of theme. On each launch, the launcher waits up to 4 seconds for the existing official tray item and replaces its image once using `Shell_NotifyIcon(NIM_MODIFY)`. It then completes taskbar setup and exits. There is no helper process, theme listener, or resident background component. A stuck tray API call is bounded by a 6-second worker timeout; failure only records a warning and does not prevent the app from opening.

This changes the current tray image, not the official app's light/dark icon resources. A later theme change, Explorer restart, or official icon refresh may restore the official image. Run the launcher again to reapply Codex.ico. Starting the official app directly does not apply the override.

Tray detection currently supports the production GUID `e5768d8b-6936-4f45-b1ad-4c5fb414cb35` and `OwlElectron_NotifyIconHostWindow`, observed in package `26.908.4834.0`. The host's packaged application identity is checked before modification. Future versions may change these details; an unavailable tray is skipped. Only the image is modified, preserving the official item's menu and callbacks without editing the installation. Diagnostics report host PID/HWND and the GUID's Shell rectangle lookup; API success is not visual verification.

## License

Icon assets are derived from [Lobe Icons](https://github.com/lobehub/lobe-icons), licensed under MIT. Codex, ChatGPT, OpenAI, and their logos are trademarks of OpenAI. This is an unofficial community project.
