# Codex Launcher for Windows

A tiny open-source launcher for the packaged ChatGPT/Codex Windows app.

<img src="assets/Codex.png" alt="Codex Launcher icon" width="128" />

## Features

- Activates `OpenAI.Codex_2p2nqsd0c76g0!App` through Windows' `IApplicationActivationManager` API.
- Avoids launching the packaged app through `explorer.exe shell:AppsFolder`.
- Maintains `Codex.lnk` shortcuts on the desktop and in the Start menu only when their target or icon changes.
- Prefers `Codex.ico` beside the executable and falls back to the icon embedded in `Codex.exe`.
- Exits immediately after activation and does not remain in the background.

## Project layout

```text
codex-launcher/
├── src/CodexLauncher.cs
├── assets/
│   ├── Codex.ico
│   ├── Codex.png
│   ├── Codex.svg
│   └── Codex-original.svg
├── build.cmd
├── LICENSE
└── README.md
```

## Build

Run `build.cmd`. The compiled launcher is written to `dist\Codex.exe`.

The build uses the .NET Framework C# compiler included with Windows and requires no third-party packages.

## Identifiers

- `45BA127D-10A8-46EA-8AB7-56EA9078943C` is the system-wide COM class identifier for `ApplicationActivationManager`.
- `2E941141-7F97-4756-BA1D-9DECDE894A3D` identifies the `IApplicationActivationManager` interface.
- `OpenAI.Codex_2p2nqsd0c76g0!App` is the AppUserModelID of the ChatGPT/Codex package targeted by this launcher.

## License

MIT

The icon assets are derived from the MIT-licensed [Lobe Icons](https://github.com/lobehub/lobe-icons) project. Codex, ChatGPT, OpenAI, and their respective logos are trademarks of OpenAI. This project is unofficial and is not affiliated with or endorsed by OpenAI.
