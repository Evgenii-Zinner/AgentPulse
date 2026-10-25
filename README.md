# AgentPulse

AgentPulse is a Windows 11 tool that tracks your remaining Google Antigravity quotas and cooldowns.

It surfaces your remaining limits across:
- **Lock Screen**: Live quota stats shown on the Windows lock screen.
- **Windows Widgets Board**: A 3-gauge circular widget (Gemini 5h & weekly, Claude / GPT, and Gemini 4 Argon) accessible via <kbd>Win</kbd> + <kbd>W</kbd>.
- **System Tray**: Displays the remaining quota percentage directly on the tray icon.
- **Flyout Window**: Window above the taskbar showing detailed model quotas and cooldowns.

Under the hood, it communicates with the local `agy` CLI using persistent streaming with no background polling loops.

## Requirements

- Windows 11
- [.NET SDK](https://dotnet.microsoft.com/)
- [Google Antigravity CLI](https://antigravity.google/) (`agy`) installed and logged in.

## Build & Run

```powershell
# 1. Clone the repository
git clone https://github.com/Evgenii-Zinner/AgentPulse.git
cd AgentPulse

# 2. Build the application
dotnet build src/AgentPulse/AgentPulse.csproj -c Debug

# 3. Register the package and Windows 11 Widget Provider
Add-AppxPackage -Register src/AgentPulse/bin/Debug/net11.0-windows10.0.26100.0/win-x64/AppxManifest.xml

# 4. Launch AgentPulse
Start-Process "shell:AppsFolder\AgentPulse.App_x8cztb2e9ee42!App"
```

## Tech Stack

- **UI Framework**: WinUI 3 / Windows App SDK (C# / .NET 11)
- **Architecture**: MVVM with `CommunityToolkit.Mvvm`
- **Windows Integration**:
  - Windows 11 Widgets Board (`IWidgetProvider` COM server)
  - Lock Screen badge and tile notifications
  - Dynamic system tray icon rendered via Win32 GDI+
- **Integration Layer**: Headless streaming JSON RPC over `agy --input-format stream-json --output-format stream-json`

## License

[MIT](LICENSE)
