# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset and save crypto. **Method 22** asset-ZIP
decryption (single **and** multi-chunk, with re-encrypt), **GameDB** crypto, and the full
**profile / save-swap** pipeline are all live. .NET 8 WinForms, single-file self-contained
EXE — no .NET install required on the target machine.

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
    (XML, etc.). Both single-chunk and multi-chunk entries decrypt (see **Method 22 notes**).
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
| **Method 22 asset decryption** (single + multi-chunk) | ✅ Working (AES-256-CBC) |
| **Method 22 re-encryption** (header MAC solved) | ✅ Working (server-side) |
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

Method 22 is the FH6 encrypted-asset container (AES-256-CBC). Entries come in two shapes:

- **Single-chunk entries** decrypt fully **offline** — the IV is in the entry header. No
  capture, no IV table needed. Verified byte-for-byte against the game's own decrypted output.
  (Roughly half of all entries; e.g. `Rules.zip` is ~73% single-chunk.)
- **Multi-chunk (large) entries** also decrypt **and re-encrypt** now. They use per-page IVs
  the game generates internally and does **not** store in the file — but those IVs are
  **deterministic per asset**, so they only need to be captured **once, ever**, and then work
  forever and offline. The tool ships a bundled **IV table** (`m22_iv_table.json`) covering
  many entries; entries not yet in the table are surfaced as `unsupported` rather than emitted
  as corrupt data.
  - The header MAC (the one load-enforced field) is solved, so an edited entry can be
    **re-encrypted into a loadable asset** — decrypt → edit the XML → re-encrypt.
  - To expand multi-chunk coverage, drop an updated/expanded `m22_iv_table.json` next to the
    EXE (built by the **FH6 M22 IV Tool** capture utility); the tool prefers a loose file over
    its embedded copy.

- **If you want a method 22 file that the tool cannot decrypt yet, ask me to grab it for you**
  (it just needs a one-time IV capture for that asset).

## Configuration

The decrypt/encrypt operations are performed by a backend service. Point the app at it:

- **Backend endpoint** resolves from `FCT_BACKEND_URL` → a DPAPI-encrypted user file
  (`endpoint.bin`, machine-bound) → an obfuscated compiled default. It is masked in
  release builds and never logged. **DO NOT CHANGE WHAT'S IN IT.**
- **App key** resolves from `FCT_API_KEY` → DPAPI-encrypted user file.

Without a reachable backend + valid key, the client loads and detects files but cannot
decrypt/encrypt. The **crypto keys stay server-side**; the Method 22 IV table is **not**
secret (it's useless without the key) and is supplied with the request.

## Security & safety

- Crypto keys live only on the backend (env-only); they are never hardcoded, returned,
  or logged. **Crypto keys are not public — do not ask for them.**
- Save Swap and profile re-encrypt always **back up your active save first** and restore it
  automatically if a write fails. See `docs/FILE_SAFETY.md`.
- Hidden runtime logs (auto-created, redacted, retained) go to
  `%LOCALAPPDATA%\ForzaCryptoTool\logs`. See `docs/LOGGING.md`.

## Status / known limitations

- **Method 22 multi-chunk coverage** depends on the bundled IV table. Single-chunk entries
  always decrypt offline; multi-chunk entries decrypt where the table has them, and the table
  grows as more assets are captured (one capture per asset, permanently).
- **Method 22 is decrypt + re-encrypt**, but re-encrypting an entry needs that entry's IVs
  (from the table) — same one-time-capture rule as above.
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

DVS — Method 22 decryption + re-encryption, profile / save-swap pipeline, and tool development

xxd20xxx — GameDB and SFS decryption

## Notes

- This tool is far from finished or perfect — there will be bugs and issues. Please be
  patient and report issues via the GitHub **Issues** tab.

