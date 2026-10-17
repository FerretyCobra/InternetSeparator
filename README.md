<p align="center">
  <img src="branding/logo-256.png" width="128" alt="Internet Separator logo">
</p>

<h1 align="center">Internet Separator</h1>

<p align="center">Every app on the connection you choose, at full speed.</p>

<p align="center">
  <a href="https://github.com/FerretyCobra/InternetSeparator/releases/latest"><b>⬇ Download for Windows</b></a>
</p>

---

Connect **Wi-Fi, Ethernet and USB tethering at the same time**, then decide which app uses which connection.
Each connection is its own pipe, so a big game download on your phone's data never slows down the browser on your Wi-Fi.

## Features

- **One column per connection.** Every network with internet access shows up with its live download/upload speed and a health check (✓ latency / ⚠ no internet).
- **Move apps between connections.** Tick apps and press ⇄. Open connections are reset so the app reconnects on the new network right away.
- **Pins survive restarts.** Moved apps are pinned (📌) and keep their connection next time.
- **Default connection.** Every app you haven't pinned uses the connection picked at the top.
- **Handles unplugging.** Unplug the phone or lose Wi-Fi and pinned apps wait. They move back when it returns.
- **Add apps before they run.** "Add app…" lists any `.exe` so you can pin it before it connects.
- **Start with Windows.** Starts minimized in the tray at sign-in, so it routes your other apps from their first connection.
- **Runs in the tray.** Closing the window keeps routing running.

## Download and run

1. Download `InternetSeparator-vX.Y.Z-win-x64.zip` from the [latest release](https://github.com/FerretyCobra/InternetSeparator/releases/latest).
2. Extract the whole zip to a folder you'll keep, e.g. `C:\Tools\InternetSeparator`. Keep the `mihomo` folder next to `InternetSeparator.exe`.
3. Run `InternetSeparator.exe` and accept the administrator prompt.

You don't need to install anything else; .NET is included in the exe.

> **"Windows protected your PC"?** The exe isn't code-signed, so SmartScreen may warn on first run. Click **More info → Run anyway**.

### Requirements

- Windows 10 or 11, 64-bit
- Administrator rights (needed to create the virtual network adapter that routes traffic)
- Two or more internet connections to make it useful, e.g. Wi-Fi + USB tethering from your phone

## How to use

1. Connect your networks (Wi-Fi, Ethernet cable, phone via USB tethering). Each one appears as a column.
2. Pick the **Default for other apps** connection at the top.
3. Tick one or more apps and click **⇄** between two columns to move them across.
4. To send an app back to the default, tick it and click **Follow default**.

### Tips

- **⚠ restart to route** means the app opened its connections before Internet Separator started. Restart that app once.
- Turn on **Start with Windows** so routing is in place before your other apps start.
- To quit, right-click the tray icon and choose **Exit**. The window's ✕ only hides it.
- **About → Open data folder** has your saved pins (`settings.json`) and the engine log (`mihomo.log`).

## How it works

Internet Separator runs [mihomo](https://github.com/MetaCubeX/mihomo) in TUN mode. mihomo creates a virtual network adapter, captures all traffic, matches each connection by process name and sends it out through the physical adapter you assigned. Apps you haven't assigned go through the default adapter.

Settings and the generated mihomo config live in the `data` folder next to the exe.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `mihomo.exe` (Windows amd64) from the [mihomo releases](https://github.com/MetaCubeX/mihomo/releases), placed at `mihomo\mihomo.exe`.

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

The output in `publish/` is the same layout as the release zip.

## Credits

- Made by [FerretyCobra](https://github.com/FerretyCobra)
- Traffic routing powered by [mihomo](https://github.com/MetaCubeX/mihomo) by MetaCubeX ([MIT license](https://github.com/MetaCubeX/mihomo/blob/main/LICENSE))
