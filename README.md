# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset and save crypto — Method 22 asset-ZIP
decrypt/re-encrypt, GameDB crypto, and the full profile / save-swap pipeline.
.NET 8 WinForms, single-file self-contained EXE.

**This is the open-source front-end client only.** All cryptographic keys and the
backend endpoint live server-side and are **not** in this source. The client ships with
no backend URL and no API key baked in — you supply them at runtime.

## Build

```powershell
# Development
dotnet build ForzaCryptoTool.csproj -c Debug

# Release (single-file, self-contained)
dotnet publish ForzaCryptoTool.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o release
```

## Configuration

The decrypt/encrypt operations run on a backend service. Point the client at one:

- **Backend endpoint** — `FCT_BACKEND_URL` environment variable, or set it once in
  **Settings** (stored DPAPI-encrypted, machine-bound).
- **App key** — `FCT_API_KEY` environment variable, or set it in **Settings**.

```powershell
$env:FCT_BACKEND_URL = "https://your-backend.example.com"
$env:FCT_API_KEY     = "your-api-key"
.\ForzaCryptoTool.exe
```

Without a reachable backend + valid key, the client loads and detects files but cannot
decrypt/encrypt.

## Credits

- **DVS** — Method 22 decryption + re-encryption, profile / save-swap pipeline, tool development
- **Ariza** — save swap help
- **Draff** — crypto help
- **xxd20xxx** — GameDB and SFS decryption

## Notes

- I wasn't going to open-source this, but there's no reason to keep the front-end source
  secret, so here it is.
- **The backend will never be open source — do not ask for it.**
- Don't claim this tool as your own. Months of RE work went into it. Have some dignity.
