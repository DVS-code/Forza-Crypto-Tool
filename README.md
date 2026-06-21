# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset and save crypto. **Method 22** asset-ZIP
decryption, **GameDB** crypto, and the full **profile / save-swap** pipeline are all live.
.NET 8 WinForms, single-file self-contained EXE — no .NET install required on the target
machine.

Version **2.2.0**.

## Run

```text
release\ForzaCryptoTool.exe
```

You can also pass a file to load on startup:

```text
release\ForzaCryptoTool.exe "C:\Forza Horizon 6\Forza Horizon 6\media\GameTunableSettings.zip"
```

The decrypt/encrypt work runs **server-side** via a backend the app talks to, so a
configured backend endpoint + app key are required for those operations (see
**Configuration** below). The app itself is a thin, safe client.

## Tabs

- **Dashboard** — drop a file; it auto-detects the type and offers the right action.
  Supported inputs:
  - **Method 22 asset ZIP** (FH6 `media\*.zip` like `GameTunableSettings.zip`,
    `Camera.zip`, `Rules.zip`) → **decrypt** to a plain ZIP of the underlying assets
    (XML, etc.). Decrypt-only.
  - **GameDB** (`gamedbRC.slt`) → decrypt / re-encrypt.
  - **Decrypted SQLite** (already-decrypted GameDB) → re-encrypt.
  - **Profile save** (`C_ProfileData`) → **decrypt / re-encrypt** using a captured IV
    table. Drop the encrypted `C_ProfileData`, pick the matching `profile_ivs.json`
    (from **Create Save → Capture IVs**), and it decrypts to the editable plaintext. Drop
    a decrypted profile back in to re-encrypt it.

  Outputs land in `Documents\ForzaCryptoTool\Output`.

- **Create Save** — captures the per-chunk **IV table** for your live profile, which is
  what makes profile decrypt/re-encrypt and save swapping possible. Requires Administrator
  (it relaunches elevated if needed). Flow: click **Capture IVs** → launch FH6 and press
  Enter on the first screen → the tool captures the IVs during profile load, self-verifies
  every chunk, then **automatically closes FH6** so it can't autosave over the captured
  version. Output (`profile_ivs.json` + a matching snapshot of the profile) lands in an
  **IV Captures** folder next to the app.

- **Save Swap** — swap a **donor** `C_ProfileData` onto your **active** account. You
  provide the donor save + its IV table, your active save + its IV table (from Create
  Save), and the target XUID (grab it from the Xbox App). The tool decrypts the donor,
  rewrites the embedded XUID to your account, re-encrypts under your active account's IV
  table, backs up your current save, and writes the swap — restoring the backup
  automatically if anything fails.

- **Profile Editor** — decrypt a `C_ProfileData` to its plaintext (with its IV table),
  edit the embedded XUID, and re-encrypt it back into a loadable save.

## What's actually working

| Feature | Status |
|---|---|
| **Method 22 asset decryption** | ✅ Working (AES-256-CBC, single-chunk entries) |
| GameDB decrypt / re-encrypt | ✅ Working (server-side) |
| Profile (`C_ProfileData`) decrypt / re-encrypt | ✅ Working (IV-based) |
| Profile IV capture (Create Save) | ✅ Working |
| Save Swap | ✅ Working |
| Profile Editor | ✅ Working |

### Profile / Save Swap notes — read this

FH6 profile saves use **per-chunk IVs that the game generates at save time and does not
store in the file**. They can only be recovered by capturing them from the running game
(the **Create Save** tab does this). Two important consequences:

- **IVs are only valid for one exact version of the save.** Any in-game autosave generates
  brand-new IVs and invalidates a previous capture. **Workflow: capture IVs → do your
  edit/swap → make sure FH6 is closed → copy the file in → launch.** Don't let FH6 autosave
  between capturing and using a save. (The tool snapshots the profile *before* launching the
  game to survive an autosave during the capture window, and it auto-closes FH6 on a
  successful capture — but don't relaunch and play before you've used the capture.)
- For a **Save Swap**, the active IV table must be captured from a save of comparable size
  to the edited result. If the edited profile needs more chunks than you captured, re-capture
  against the current save and try again.
- IV capture supports the current Steam build (`6.379.939.0`) and the earlier pre-release
  build (`6.353.145.0`), auto-detecting which one is running. A later game update may need
  the IV tool refreshed.

### Method 22 notes

- **Single-chunk entries** decrypt fully and produce valid, inflated asset files
  (verified byte-for-byte against the game's own decrypted output).
- **Multi-chunk (large) entries** are detected and **safely skipped** — they are
  excluded from the output ZIP and surfaced in the job summary/warnings rather than
  emitted as corrupt data. Multi-chunk page de-interleaving is in progress.
- **If you want a method 22 file that the tool cannot decrypt please ask me to grab it for you.**

## Configuration

The decrypt/encrypt operations are performed by a backend service. Point the app at it:

- **Backend endpoint** resolves from `FCT_BACKEND_URL` → a DPAPI-encrypted user file
  (`endpoint.bin`, machine-bound) → an obfuscated compiled default. It is masked in
  release builds and never logged. **DO NOT CHANGE WHAT'S IN IT.**
- **App key** resolves from `FCT_API_KEY` → DPAPI-encrypted user file.

Without a reachable backend + valid key, the client loads and detects files but cannot
decrypt/encrypt.

## Security & safety

- Crypto keys live only on the backend (env-only); they are never hardcoded, returned,
  or logged. **Crypto keys are not public — do not ask for them.**
- Save Swap and profile re-encrypt always **back up your active save first** and restore it
  automatically if a write fails. See `docs/FILE_SAFETY.md`.
- Hidden runtime logs (auto-created, redacted, retained) go to
  `%LOCALAPPDATA%\ForzaCryptoTool\logs`. See `docs/LOGGING.md`.

## Status / known limitations

- **Method 22 multi-chunk entries** are not yet decrypted offline (per-chunk page
  de-interleaving in progress); they are safely skipped, never corrupted.
- Profile crypto depends on a fresh IV capture per save version — see the Profile / Save
  Swap notes above.

## Build

```powershell
# Development
dotnet build ForzaCryptoTool.csproj -c Debug

# Release (single-file, self-contained, compressed) — produces release\ForzaCryptoTool.exe
dotnet publish ForzaCryptoTool.csproj -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o release
```

The release EXE is ~70 MB (the .NET 8 + WinForms runtime is bundled and compressed),
runs on a clean Windows machine with no .NET install, and is a single file.

## Credits

DVS — Method 22 decryption, profile / save-swap pipeline, and tool development

xxd20xxx — GameDB and SFS decryption

## Notes

- This tool is far from finished or perfect — there will be bugs and issues. Please be
  patient and report issues via the GitHub **Issues** tab.
