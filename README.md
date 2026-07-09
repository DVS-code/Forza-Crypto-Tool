# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset and save crypto — Method 22 asset-ZIP
decrypt/re-encrypt, GameDB crypto, and the full profile / save-swap pipeline.
.NET 8 WinForms, single-file self-contained EXE.

**This is the open-source front-end client only.** All cryptographic keys and the
backend endpoint live server-side and are **not** included in this source. The client
ships with no backend URL and no API key baked in — you supply them at runtime.

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

The decrypt/encrypt operations run through a backend service. Point the client at one:

* **Backend endpoint** — `FCT_BACKEND_URL` environment variable, or set it once in
  **Settings** (stored DPAPI-encrypted and machine-bound).
* **App key** — `FCT_API_KEY` environment variable, or set it in **Settings**.

```powershell
$env:FCT_BACKEND_URL = "https://your-backend.example.com"
$env:FCT_API_KEY     = "your-api-key"
.\ForzaCryptoTool.exe
```

Without a reachable backend and valid key, the client can load and detect files but
cannot perform decrypt/encrypt operations.

## Credits

* **DVS** — Method 22 decryption + re-encryption, profile / save-swap pipeline, tool development
* **Ariza** — Save swap assistance
* **Draff** — Crypto assistance
* **xxd20xxx** — GameDB and SFS decryption research

## Notes

* I wasn't originally planning to open-source this, but there is no reason to keep
  the front-end source private, so it is available here.
* **The backend will remain closed source. Please do not request access to it.**
* Do not claim this tool or its work as your own. Significant reverse engineering
  and development effort went into this project.

## License

Copyright (C) 2026 DVS-code

ForzaCryptoTool is free software: you can redistribute it and/or modify it under the
terms of the **GNU General Public License as published by the Free Software Foundation,
version 3**.

This project is distributed in the hope that it will be useful, but **WITHOUT ANY
WARRANTY**; without even the implied warranty of **MERCHANTABILITY** or **FITNESS FOR
A PARTICULAR PURPOSE**.

You should have received a copy of the GNU General Public License v3.0 along with this
project. If not, see:

https://www.gnu.org/licenses/gpl-3.0.html

The open-source client code is licensed under GPLv3. The backend service, cryptographic
keys, private infrastructure, and server-side components are **not included** in this
repository and remain separately licensed and closed source.
