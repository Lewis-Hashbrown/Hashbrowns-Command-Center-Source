# HashBrowns Command Center (Source)

This repository contains the full source code for **HashBrowns Command Center**.

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

