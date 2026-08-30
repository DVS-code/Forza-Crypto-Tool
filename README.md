# ForzaCryptoTool public client

Open-source .NET 8 front-end for Forza Horizon 6 asset, GameDB, profile-save crypto, profile editing, and save swapping.

This branch contains the Windows WPF application, the cross-platform command line, and their shared client library. Backend services, cryptographic keys, API credentials, and private infrastructure are not included.

## Features

- Method 22 asset ZIP decryption and re-encryption
- GameDB decryption and re-encryption
- Config-file decryption and re-encryption
- FH6-only Profile Editor for typed properties, XUID, BXML, binary scalars, and embedded SQLite
- Save Swap with Xbox, Microsoft Store, Steam, cracked, and RUNE save discovery
- Optional Xbox gamertag labels for discovered XUIDs
- Windows graphical interface and Windows/Linux command line

## Build

Install the .NET 8 SDK, then run:

```powershell
dotnet build ForzaCryptoTool.sln -c Release
```

Windows single-file application:

```powershell
dotnet publish src/ForzaCryptoTool.Gui/ForzaCryptoTool.Gui.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/win
```

Linux x64 command line:

```powershell
dotnet publish src/ForzaCryptoTool.Cli/ForzaCryptoTool.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/linux
```

## Configuration

Crypto operations require a compatible backend service. This public source contains no default endpoint or app key.

Set both values before starting the client:

```powershell
$env:FCT_BACKEND_URL = "https://your-backend.example.com"
$env:FCT_API_KEY = "your-api-key"
```

Without a reachable compatible backend and valid app key, local detection, profile inspection, and offline editing remain available, but decrypt and re-encrypt operations cannot run.

## Credits

- **DVS** — Method 22 crypto, profile crypto, save swapping, and tool development
- **xxd20xxx** — GameDB and SFS crypto research
- **Ariza** — Save Swap assistance
- **Doliman100** — ForzaTech crypto research
- **draff** — Original profile-editor reference implementation, FH6 format research, and crypto assistance
- **Forza Mods AIO** — Save Swap Xbox account-identification/PeopleHub reference

## License

The public client is licensed under GPLv3. Backend services, cryptographic keys, credentials, and private infrastructure are not included and remain separately licensed and closed source.
