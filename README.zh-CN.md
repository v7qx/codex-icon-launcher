# Codex Icon Launcher for Windows

[English](README.md) | 简体中文

用于替换 Windows 版 ChatGPT/Codex 打包应用图标的轻量启动器。

<img src="assets/Codex.svg" alt="Codex Launcher 图标" width="128" />

## 功能

启动应用，为快捷方式、任务栏和托盘使用 Codex 图标。设置完成后退出，无常驻进程，不修改官方安装文件。

## 构建

运行 `build.cmd`，生成 `dist\Codex.exe`。

## 使用

1. 将 `Codex.exe` 放在固定目录中。
2. 运行程序，打开应用并替换图标。
3. 如需快捷方式，使用下面的命令创建，也可以将其固定到任务栏。

```powershell
.\Codex.exe --create-shortcut desktop
.\Codex.exe --create-shortcut start-menu
```

如需自定义图标，替换 `assets/Codex.ico` 后重新构建。`assets/Codex.svg` 为可编辑源素材，构建时仅使用 ICO 文件。

## 已知限制

- Alt+Tab、Win+Tab（任务视图）、任务栏预览和贴靠组的窗口图标保持原样。
- 仅在启动时执行一次替换。切换主题、重启应用或资源管理器后若恢复官方图标，重新运行启动器即可。

## 许可证

图标资源衍生自采用 MIT 许可证的 [Lobe Icons](https://github.com/lobehub/lobe-icons) 项目。Codex、ChatGPT、OpenAI 及相关标志是 OpenAI 的商标。本项目为非官方社区项目。
