# ForzaCryptoTool

Toolkit for Forza Horizon 6 asset, GameDB, profile-save crypto, profile editing, and save swapping.

Built with .NET 8 and WPF, distributed as a self-contained executable. The same executable is also a
full command line, and a separate native binary runs the command line on Linux.

📺 **[Video guide](https://youtu.be/7-ChnwC4vTs?si=9shSeTkzTVWxoj0x)** · 💬 **[Discord support](https://discord.gg/fh6)**

---

## What's new in v3.2

* **Asset Browser.** Browse the game install as a tree and open files directly in the tool, instead
  of decrypting an archive to disk and opening it elsewhere. Archives list from the ZIP central
  directory alone, so expanding one is instant even at several GB, and nothing is decrypted until
  you click a file.
* **Editing.** Text files and archive entries can be edited and saved, either to the output folder
  or over the original behind a second confirmation. Saving an entry rebuilds its archive with every
  other entry copied byte-for-byte — including encrypted ones the tool cannot read — and the file's
  original BOM and line endings are preserved.
* **Honest per-entry status.** Every archive entry is labelled *plain*, *encrypted* or *locked*
  before anything is opened, worked out from its page layout without decrypting. A locked entry
  explains exactly why and never shows guessed bytes.
* **Byte-exact archive rebuilding.** Repacking an archive with no changes reproduces it byte for
  byte, verified across all 8,888 archives under 32 MB in the retail install.
* **Test suite.** Ships with the source and runs against a real game install, skipping cleanly
  without one.

See the [release notes](https://github.com/DVS-code/Forza-Crypto-Tool/releases/tag/V3.2) for the
full list of changes.

---

## Features

### Config .ini files

* Decrypt any config (.ini) file
* Re-encrypt edited configs back into loadable game files

Note that not every `.ini` is encrypted — `ControllerFFB.ini` and `ControllerRec.ini` ship as plain
text, and the tool says so rather than pretending to decrypt them.

### Method 22 assets

* Decrypt encrypted FH6 asset ZIPs
* Re-encrypt edited assets back into loadable game files
* Supports single-chunk and multi-chunk entries

### GameDB

* Decrypt GameDB files into clean, openable SQLite
* Edit values in any SQL tool
* Re-encrypt back into a loadable `.slt`

### Profile saves

* Decrypt and re-encrypt `C_ProfileData`
* Native FH6-only editor for typed properties, XUID, BXML, binary records and embedded SQLite
* Lossless no-edit round trips and full parse/integrity verification before an edited copy is accepted
* Seasonal record and item-catalog drift does not prevent a valid FH6 profile from opening

### Save Swap

* Swap donor saves to another account — any donor size
* Single-field XUID swap, so full progression transfers
* Automatic backup and one-click restore
* Works on Steam, cracked, Microsoft Store and **RUNE** builds
* Optional Xbox gamertag lookup labels discovered XUIDs for multi-account installs

---

## Tabs

### Asset Browser

Point it at the game install and browse it as a tree. Folders and archives expand on click; archive
contents come from the ZIP central directory, so opening a multi-GB archive costs no decryption and
no network call.

Each entry inside an archive carries a status:

| Badge | Meaning |
|---|---|
| `plain` | Not encrypted (stored or deflate). Opens offline, no backend involved. |
| `encrypted` | Method 22, decryptable — sent to the service one entry at a time when you open it. |
| `locked` | Method 22 whose per-page IVs aren't available. Cannot be decrypted; the tooltip says why. |

*Only openable* is ticked by default; untick it to see locked entries as raw bytes. Text files open
in an editor, anything else in a hex view, so no file is a dead end.

Editing a file enables **Save**, which offers the output folder or overwriting the original. Saving
an entry rewrites the whole archive with every other entry byte-copied, so untouched files — locked
ones included — pass through unchanged. Nothing is written that fails its own read-back.

### Dashboard

Drop a supported file and the tool detects it automatically, then offers whichever of Decrypt /
Re-encrypt actually applies.

**Supported files:**

* Method 22 asset ZIPs
* GameDB files (`.slt`)
* SQLite GameDB files
* Config files (`.ini`)
* Profile saves (`C_ProfileData`)

Outputs default to `Documents\ForzaCryptoTool\Output`, configurable in Settings.

Re-encrypting a config or a Method 22 archive needs the **original encrypted file** for its framing and
IVs — the app asks for it, or pass `--original` on the command line.

> **Do NOT overwrite your game's base encrypted GameDB with a decrypted/edited DB — it will not load.**
> Full decrypted GameDB editing and loading into FH6 is in progress.

### Save Swap

Swap a donor save onto another account. Any donor size works, and full progression transfers.

Saves are detected automatically for:

* **Xbox / retail (PGS)** — `C:\XboxGames\GameSave\pgs\u_<xuid>_16D460\<version>\ContainersRoot\User_<hex>\`
* **RUNE** — `C:\Users\Public\Documents\MicrosoftStore\RUNE\Forza Horizon 6 [<id>]\<xuid>\SaveGames\ContainersRoot\User_<hex>\`

RUNE uses one fixed account on every install, so the XUID is filled in for you there.

Use **Identify accounts** to label discovered Xbox save accounts as `Gamertag · XUID`. The lookup uses
the currently signed-in Xbox App session, keeps its token in memory only, and falls back to the numeric
XUID if an account cannot be resolved.

Every replaced file is backed up with a timestamp next to it, and **Restore original** undoes a swap —
even after restarting the tool. Close the game before swapping, or it will save over the new file.

**Video guide:** https://youtu.be/7-ChnwC4vTs?si=9shSeTkzTVWxoj0x

### Profile Editor

Open or drop an encrypted or decrypted FH6 `C_ProfileData` file. Encrypted input is decrypted into a
private temporary working file; after editing, **Save copy** validates the result, re-encrypts it, and
asks where to write the encrypted profile. The temporary plaintext is removed when the document closes.

* **Properties** searches and edits every typed FH6 property, including variable-length strings.
* **Overview** shows section sizes, SQLite health, and the canonical account XUID.
* **Binary records** lists every registered or seasonal record and offers fixed-width scalar editing
  for known payload offsets without changing record framing.
* **BXML** edits the save-state string table and rebuilds all indexes safely.
* **Database** browses every table/view and runs multi-statement SQLite scripts against a working copy.

The raw printable-string scanner is intentionally not exposed as an editor tab: FH6 profiles contain
binary framing and an embedded SQLite database, so its output is mostly duplicated schema text and
misleading fragments. Saving writes a separate copy by default, checks SQLite integrity, then parses
the generated profile back through the FH6-only reader before reporting success.

### Settings

Output folder, update options, service status with a connection test, and the log folder. Crypto,
profile-editor, save-swap, and restore writes always create collision-safe backups when replacing files.

### About

Version, credits, links, and the command-line reference.

---

## Command line

The same `ForzaCryptoTool.exe` opens the window when you double-click it, and runs headlessly when you
give it a command — printing to the terminal you called it from.

```
ForzaCryptoTool decrypt gamedbRC.slt -o db.sqlite
ForzaCryptoTool encrypt db.sqlite -o gamedbRC.slt --force
ForzaCryptoTool decrypt PhysicsSettings.ini
ForzaCryptoTool encrypt PhysicsSettings_decrypted.ini --original PhysicsSettings.ini
ForzaCryptoTool saveswap donor_C_ProfileData --rune --yes
ForzaCryptoTool saveswap donor_C_ProfileData --xuid 2535437902562438
ForzaCryptoTool detect Camera.zip
ForzaCryptoTool saves
ForzaCryptoTool profile-inspect C_ProfileData_decrypted.bin
ForzaCryptoTool profile-set C_ProfileData_decrypted.bin --property /Main/TotalCredits --value 25000000
ForzaCryptoTool profile-xuid C_ProfileData_decrypted.bin --xuid 2535437902562438
ForzaCryptoTool profile-sql C_ProfileData_decrypted.bin --sql "SELECT * FROM Data_Car LIMIT 10;"
```

| Option | Meaning |
|---|---|
| `-o, --output <path>` | Output file, or a directory to use the default name |
| `--original <file>` | The original **encrypted** file (needed for config / Method 22 re-encrypt) |
| `--target <path>` | Save-swap destination (a `C_ProfileData` path) |
| `--rune` | Target the RUNE save; its XUID is filled in automatically |
| `-x, --xuid <id>` | Target account XUID (decimal or `0x` hex) |
| `-f, --force` | Overwrite an existing output file |
| `-y, --yes` | Skip confirmation prompts |
| `--gui` | Force the window open even with a command present |

Exit codes: `0` ok · `1` failed · `2` bad usage · `3` file not found · `4` backend offline ·
`5` unsupported type.

### Linux

`forzacrypto` is the same command line as a native Linux binary. All crypto runs server-side, so
nothing platform-specific is involved. It finds saves inside Proton and Wine prefixes (Steam
`compatdata`, Lutris, Heroic, bare `~/.wine`).

```bash
chmod +x forzacrypto
./forzacrypto saves
./forzacrypto decrypt gamedbRC.slt -o db.sqlite
```

The published Linux binary embeds its native SQLite dependency. The graphical app is Windows-only —
WPF has no Linux runtime.

---

## Requirements

* Windows 10 or later for the app; any modern x64 Linux for the CLI
* An internet connection — decryption runs server-side
* Nothing to install; both binaries are self-contained

---

## Status

| Feature | Status |
|---|---|
| Method 22 decrypt | ✅ |
| Method 22 re-encrypt | ✅ |
| Config decrypt | ✅ |
| Config re-encrypt | ✅ |
| GameDB decrypt (to editable SQLite) | ✅ |
| GameDB re-encrypt | ✅ |
| Profile decrypt / re-encrypt | ✅ |
| Save Swap (incl. RUNE) | ✅ |
| Command line (Windows + Linux) | ✅ |
| FH6 Profile Editor | ✅ |
| Older ForzaTech (FM6 Apex / FH3 / FM7 / FH4 / FH5) | ⏳ not yet in v3 — use v2 |

The v3.1 profile editor intentionally supports FH6 only. FH5 compatibility code and older-title
heuristics from the reference editor were not carried into the new parser.

---

## Verified before release

Every supported file type was tested end-to-end against the live service on real game files:

| Type | Result |
|---|---|
| GameDB (`gamedbRC.slt`, 14.5 MB) | 205 tables, 638 cars, `integrity_check = ok` |
| Profile save (`C_ProfileData`) | 3,087,088 bytes, valid plaintext |
| Current FH6 editor fixture | 2,700,762 bytes; 714 typed nodes; 5,405 BXML nodes; 111 binary records; SQLite `ok`; byte-identical no-edit round trip |
| Method 22 (`Camera.zip`) | 244 entries, all valid, clean XML |
| Config (`PhysicsSettings.ini`) | 66,508 bytes of readable config |

The config **round-trip** was also verified: decrypt → edit → re-encrypt → decrypt returns the edited
file byte-for-byte.

### v3.2 archive handling

| Check | Result |
|---|---|
| Repack with no changes | Byte-identical across **all 8,888 archives ≤32 MB** in the retail install |
| Central-directory parsing | Matches .NET's own ZIP reader on entry names, sizes and CRCs |
| Method-22 page layout | Every method-22 entry in the install classifies to a legal layout |
| Single-entry edit | The edited entry holds the new bytes; every other entry is unchanged |
| Text round-trip | BOM and line endings preserved; an unedited file re-encodes to identical bytes |

The repack check runs over the whole install rather than a sample for a reason: exactly one archive
(`UI.zip`) contains 22 bytes of padding between two entries, and a contiguous rewrite silently
dropped it. A sampled test would have missed it.

---

## Building

Requires the .NET 8 SDK.

```bash
dotnet build ForzaCryptoTool.sln -c Release
```

| Project | Output | Platform |
|---|---|---|
| `src/ForzaCryptoTool.Core` | shared library | any |
| `src/ForzaCryptoTool.Gui` | `ForzaCryptoTool.exe` (window + CLI) | Windows |
| `src/ForzaCryptoTool.Cli` | `forzacrypto` | Windows, Linux, macOS |

Single-file releases:

```bash
dotnet publish src/ForzaCryptoTool.Gui -c Release -r win-x64 \
  -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/win

dotnet publish src/ForzaCryptoTool.Cli -c Release -r linux-x64 \
  -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release/linux
```

`FCT_START_VIEW=SaveSwap` opens the app on a given tab, which is handy while working on a view.

---

## How it works

Decryption runs **server-side**. This client holds no encryption keys — it identifies files, uploads
them, polls the job, and writes the result. That is deliberate: the keys stay off end-user machines.

Configuration resolves in this order, for both the endpoint and the app key:

1. environment variable (`FCT_BACKEND_URL`, `FCT_API_KEY`)
2. protected file in the user config directory
3. obfuscated compiled-in default

The protected file uses DPAPI on Windows, and AES-GCM with a key derived from per-user/per-machine
material elsewhere. The non-Windows path stops casual disclosure — a synced dotfile, a backup tarball —
not someone already running code as your user.

Logs are written automatically with paths, URLs and keys redacted, so they are safe to share when
asking for help. Find them under **Settings → Logs**.

---

## Credits

* **DVS** — Method 22 crypto, profile crypto, save swap system, and tool development — [youtube.com/@dvssquad](https://www.youtube.com/@dvssquad)
* **xxd20xxx** — GameDB and SFS crypto research
* **Ariza** — Save swap assistance
* **Doliman100** — [ForzaTech-crypto-tool](https://github.com/Doliman100/ForzaTech-crypto-tool) (older Forza decryption)
* **draff** — Original profile-editor reference implementation, FH6 format research, and crypto help
* **Forza Mods AIO** — Reference for Save Swap Xbox account identification via PeopleHub ([source](https://github.com/ForzaMods/Forza-Mods-AIO/blob/6adf484ec921d0f02145d37483144c9a31ec6124/Forza-Mods-AIO/TabForms/Saveswapper.cs))

---

## Disclaimer

This project is still under active development. Bugs and edge cases may exist. Please report issues
through GitHub Issues.

This is for **offline, single-player** save editing on builds you own. Save swapping and other uses can
result in a **ban** — that's your decision, and I'm not responsible for it.

Please do not redistribute the compiled executable. Share the GitHub repository instead.
