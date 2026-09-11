# Codex Launcher for Windows

A tiny launcher for the packaged ChatGPT/Codex Windows app, with a custom shortcut icon.

<img src="assets/Codex.svg" alt="Codex Launcher icon" width="128" />

## Features

- Automatically detects the installed app and opens it.
- Applies the custom taskbar icon to the app's current windows after launch.
- Creates desktop or Start menu shortcuts on request, with a custom icon and matching taskbar identity.
- Embeds the icon in a single executable.

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

## License

Icon assets are derived from [Lobe Icons](https://github.com/lobehub/lobe-icons), licensed under MIT. Codex, ChatGPT, OpenAI, and their logos are trademarks of OpenAI. This is an unofficial community project.
