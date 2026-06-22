# ForzaCryptoTool

Windows toolkit for Forza Horizon 6 asset, GameDB, and profile save crypto.

Built with .NET 8 WinForms and distributed as a self-contained executable.

## Features

### Method 22 Assets

* Decrypt encrypted FH6 asset ZIPs
* Re-encrypt edited assets back into loadable game files
* Supports single-chunk and multi-chunk entries

### GameDB

* Decrypt GameDB files
* Re-encrypt modified databases

### Profile Saves

* Decrypt and re-encrypt C_ProfileData
* Capture profile IV tables
* Edit profile properties and XUIDs

### Save Swap

* Swap donor saves to another account
* Automatic backup and recovery
* Supports different save sizes

## Tabs

### Dashboard

Drop a supported file and the tool will automatically detect it.

**Supported files:**

* Method 22 asset ZIPs
* GameDB files
* SQLite GameDB files
* Profile saves (C_ProfileData)

Outputs are saved to:

```
<rootfile>\Output
```

### Create Save

Captures the IV table required for profile decryption and re-encryption.

The tool automatically verifies the capture and creates the required files for future profile editing or save swapping.

### Save Swap

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

## How It Works

1. Capture IVs from your profile.
2. Decrypt the target file.
3. Make your edits.
4. Re-encrypt and load in-game.

## Requirements

* Windows
* Administrator privileges for IV capture
* Valid backend configuration

## Status

| Feature | Status |
|---------|--------|
| Method 22 Decrypt | ✅ |
| Method 22 Re-Encrypt | ✅ |
| GameDB Decrypt/Re-Encrypt | ✅ |
| Profile Decrypt/Re-Encrypt | ✅ |
| IV Capture | ✅ |
| Save Swap | ✅ |
| Profile Editor | ✅ |

## Credits

* **DVS** — Method 22 crypto, profile crypto, save swap system, and tool development
* **xxd20xxx** — GameDB and SFS crypto research
* **Ariza** — Save swap assistance

## Disclaimer

This project is still under active development. Bugs and edge cases may exist. Please report issues through GitHub Issues.

Please do not redistribute the compiled executable. Share the GitHub repository instead.
