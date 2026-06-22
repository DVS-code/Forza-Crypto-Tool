# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset, GameDB, and profile save crypto, plus decryption for older Forza titles.

Built with .NET 8 WinForms and distributed as a self-contained executable.

## Features

### Method 22 Assets

* Decrypt encrypted FH6 asset ZIPs
* Re-encrypt edited assets back into loadable game files
* Supports single-chunk and multi-chunk entries

### GameDB

* Decrypt GameDB files into clean, openable SQLite
* Edit values in any SQL tool

### Profile Saves

* Decrypt and re-encrypt C_ProfileData
* Capture profile IV tables
* Edit 150+ profile properties and XUIDs

### Save Swap

* Swap donor saves to another account — any donor size
* Single-field XUID swap, so full progression transfers
* Automatic backup and recovery
* Works on the Steam, cracked, and Microsoft Store builds

### Older ForzaTech

* Decrypt / re-encrypt files from earlier Forza titles (FM6 Apex, FH3, FM7, FH4, FH5)
* Powered by [Doliman100/ForzaTech-crypto-tool](https://github.com/Doliman100/ForzaTech-crypto-tool), bundled into the backend

## Tabs

### Dashboard

Drop a supported file and the tool will automatically detect it.

**Supported files:**

* Method 22 asset ZIPs
* GameDB files
* SQLite GameDB files
* Profile saves (C_ProfileData)

Outputs are saved to:

<rootfile>\Output



> **Do NOT overwrite your game's base encrypted GameDB with a decrypted/edited DB — it will not load.** Full decrypted GameDB editing and loading into FH6 is in progress.

### Create Save

Captures the IV table required for profile decryption and re-encryption.

The tool automatically verifies the capture and creates the required files for future profile editing or save swapping. Supports the Steam, cracked, and Microsoft Store builds.

### Save Swap

Swap a donor save onto another account. Any donor size works, and full progression transfers.

**Video Guide:** https://youtu.be/7-ChnwC4vTs?si=9shSeTkzTVWxoj0x

### Profile Editor

Edit profile values directly from a decrypted save.

**Supported edits include:**

* XUID
* Credits
* XP
* Skill Points
* Difficulty Settings
* Assists
* And many additional profile properties

### Older ForzaTech

Decrypt / re-encrypt files from earlier Forza titles. Pick the game and key type, then decrypt a file or re-encrypt an edited one.

## Requirements

* Windows
* Administrator privileges for IV capture
* Valid backend configuration

## Status

| Feature | Status |
|---------|--------|
| Method 22 Decrypt | ✅ |
| Method 22 Re-Encrypt | ✅ |
| GameDB Decrypt (to editable SQLite) | ✅ |
| GameDB Re-Encrypt | 🚧 In progress |
| Profile Decrypt/Re-Encrypt | ✅ |
| IV Capture (Steam / Cracked / MS Store) | ✅ |
| Save Swap | ✅ |
| Profile Editor | ✅ |
| Older ForzaTech (FM6 Apex / FH3 / FM7 / FH4 / FH5) | 🧪 Experimental |

## Credits

* **DVS** — Method 22 crypto, profile crypto, save swap system, and tool development — [youtube.com/@dvssquad](https://www.youtube.com/@dvssquad)
* **xxd20xxx** — GameDB and SFS crypto research
* **Ariza** — Save swap assistance
* **Doliman100** — [ForzaTech-crypto-tool](https://github.com/Doliman100/ForzaTech-crypto-tool) (older Forza decryption)

## Disclaimer

This project is still under active development. Bugs and edge cases may exist. Please report issues through GitHub Issues.

This is for **offline, single-player** save editing on builds you own. Save swapping and other uses can result in a **ban** — that's your decision, and I'm not responsible for it.

Please do not redistribute the compiled executable. Share the GitHub repository instead.
