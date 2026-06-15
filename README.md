## ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset and save crypto.

Version **2.1.0**.

## Tabs

- **Dashboard** — drop a file; it auto-detects the type and offers the right action.
  Supported inputs:
  - **Method 22 asset ZIP** (FH6 `media\*.zip` like `GameTunableSettings.zip`,
    `Camera.zip`, `Rules.zip`) → **decrypt** to a plain ZIP of the underlying assets
    (XML, etc.). Decrypt-only.
  - **GameDB** (`gamedbRC.slt`) → decrypt / re-encrypt.
  - **Decrypted SQLite** (already-decrypted GameDB) → re-encrypt.

  Outputs land in `\ForzaCryptoTool\Output`.

- **Save Swap** — **Coming Soon.** 

- **Profile Editor** — **Coming Soon.** 

## What's actually working

| Feature | Status |
|---|---|
| **Method 22 asset decryption** | ✅ Working (AES-256-CBC, single-chunk entries) |
| GameDB decrypt / re-encrypt | ✅ Working (server-side) |
| Profile (`C_ProfileData`)  | 🔒 Coming Soon
| Save Swap | 🔒 Coming Soon
| Profile Editor | 🔒 Coming Soon

### Method 22 notes

- **Single-chunk entries** decrypt fully and produce valid, inflated asset files
  (verified byte-for-byte against the game's own decrypted output).
- **Multi-chunk (large) entries** are detected and **safely skipped** — they are
  excluded from the output ZIP and surfaced in the job summary/warnings rather than
  emitted as corrupt data. Multi-chunk page de-interleaving is in progress.
- **If you want a method 22 file that the tool cannot decrypt please ask me to grab it for you**

## Configuration

- The decrypt/encrypt operations are performed by a backend service. DO NOT CHANGE WHATS IN IT!

- Without a reachable backend + valid key, the client loads and detects files but cannot
decrypt/encrypt.

## Security & safety

- Crypto keys are not public, do not ask for them.
- Hidden runtime logs (auto-created, redacted, retained) go to
  `%LOCALAPPDATA%\ForzaCryptoTool\logs`. See `docs/LOGGING.md`.

## Credits

DVS - Method 22 decryption and Tool Developement

xxd20xxx - GameDB and SFS decryption

## Notes

- This tool is far from finished or perfect there will be bugs and issues please be patient and report issues via the github "Issues" tab.
