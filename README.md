# HashBrowns Command Center

![HashBrowns Command Center Banner](assets/title-banner.png)

HashBrowns Command Center is a multi-client DreamBot organizer / manager that lets you run and manage a lot of clients in one clean dashboard without everything turning into a mess.

## What it can do

- Organize lots of clients in one window (20+ supported)
- Scale tiles so they stay usable at high client counts
- Quick client control flow
- Right-click tile actions (Mute/Unmute client, Close client)
- Top-bar controls for Mute All, Tile Click Control, Close All
- Settings menu for FPS/layout/performance

## Main Features

- Scalable grid layout for large multi-client sessions
- Click-to-control workflow with optional prompts
- Per-client and global audio controls
- Startup setup prompt if DreamBot JAR path is not configured
- Full source code included for transparency

## Screenshots

### Dashboard Overview
![Dashboard Overview](assets/screenshots/dashboard-stretch.png)

### Keep Default Ratio Mode
![Keep Default Ratio](assets/screenshots/dashboard-keep-ratio.png)

### High Client Count
![36 Clients](assets/screenshots/dashboard-36-clients.jpg)

### Second Page View
![Second Page](assets/screenshots/dashboard-second-page.jpg)

### Settings
![Settings](assets/screenshots/settings.png)

## Projects

- `ClientDashboard/` - Main WPF app
- `AutoTest/` - Test/diagnostic tooling

## Requirements

- Windows 10/11
- .NET 8 SDK (for building from source)

## Build

```powershell
dotnet build ClientDashboard/ClientDashboard.csproj
```

## Run (dev)

```powershell
dotnet run --project ClientDashboard/ClientDashboard.csproj
```

## Publish

```powershell
dotnet publish ClientDashboard/ClientDashboard.csproj -c Release -o ClientDashboard/publish
```
