# Codex Icon Launcher for Windows

English | [简体中文](README.zh-CN.md)

A lightweight icon-replacement launcher for the packaged ChatGPT/Codex Windows app.

<img src="assets/Codex.svg" alt="Codex Launcher icon" width="128" />

## Features

Opens the app with Codex icons for shortcuts, the taskbar, and the tray. Exits after applying changes, with no resident process or modifications to the official installation.

## Build

Run `build.cmd` to generate `dist\Codex.exe`.

## Use

1. Place `Codex.exe` in a permanent folder.
2. Run it to open the app and replace its icons.
3. Optionally create a shortcut using one of the commands below. You can pin it to the taskbar.

```powershell
.\Codex.exe --create-shortcut desktop
.\Codex.exe --create-shortcut start-menu
```

To customize the icon, replace `assets/Codex.ico` and rebuild. `assets/Codex.svg` is the editable artwork; the build uses only the ICO file.

## Known limitations

- Alt+Tab, Win+Tab (Task View), taskbar preview, and Snap Group window icons remain unchanged.
- Changes apply once at launch. If official icons return after a theme change or app/Explorer restart, run the launcher again.

## License

Icon assets are derived from [Lobe Icons](https://github.com/lobehub/lobe-icons), licensed under MIT. Codex, ChatGPT, OpenAI, and their logos are trademarks of OpenAI. This is an unofficial community project.
