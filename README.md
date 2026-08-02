# AKAI Disk Catalog

A cross-platform (Windows + macOS) desktop app that scans a folder of AKAI S900/S1000/S3000
sampler disk images (`.hfe` and `.img`) and builds a searchable, browsable catalog of every
disk's volumes, samples, and programs — sample rate, duration, loop points, root key, tuning,
and full program keygroup / velocity-zone maps.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (free, Microsoft)
- Windows 10+ or macOS 12+

## Running it

```bash
cd src/AkaiDiskCatalog.App
dotnet run
```

Or open `AkaiDiskCatalog.sln` in JetBrains Rider / Visual Studio and run the
`AkaiDiskCatalog.App` project.

## Publishing a standalone app

```bash
# macOS (Apple Silicon)
dotnet publish src/AkaiDiskCatalog.App -c Release -r osx-arm64 --self-contained

# macOS (Intel)
dotnet publish src/AkaiDiskCatalog.App -c Release -r osx-x64 --self-contained

# Windows
dotnet publish src/AkaiDiskCatalog.App -c Release -r win-x64 --self-contained
```

The published app is in `src/AkaiDiskCatalog.App/bin/Release/net8.0/<rid>/publish/`.

## How it works

- **AkaiDiskCatalog.Core** — no external dependencies. Decodes `.hfe` bitstream images
  (a from-scratch MFM decoder — no HxC/akaiutil binaries needed at runtime), reads the
  AKAI S900/S1000/S3000 floppy filesystem (FAT + volume directory), and parses S1000/S3000
  sample headers (rate, length, loop points, tuning) and program files (keygroups,
  velocity zones, per-zone tuning/filter/pan). All byte offsets were derived from and
  cross-checked against Klaus Michael Indlekofer's `akaiutil` (GPLv2) — see
  `Filesystem/*.cs` doc comments for structure references.
- **AkaiDiskCatalog.Data** — SQLite-backed catalog (`Microsoft.Data.Sqlite`). Rescans are
  incremental: a disk image is only re-decoded if its file size or modified time changed
  since the last scan. The database lives at:
  - macOS: `~/Library/Application Support/AkaiDiskCatalog/catalog.db`
  - Windows: `%LOCALAPPDATA%\AkaiDiskCatalog\catalog.db`
- **AkaiDiskCatalog.App** — Avalonia 11 MVVM desktop UI (CommunityToolkit.Mvvm).

## Known limitations (v1)

- **S3000 program keygroups are not decoded** — the S3000 keygroup binary layout differs
  from S1000's and wasn't reverse-engineered in this pass. S3000 program *headers* (name,
  MIDI channel, key range) still show; the keygroup/velocity-zone table will show a note
  instead of data. S1000 programs (like the disk this was built against) are fully decoded.
- **S900 sample/program internals aren't deeply parsed** — S900 files are recognized,
  named, and sized correctly, but sample-rate/loop/keygroup details specific to the S900's
  older header format aren't extracted yet.
- Low-density (800KB) floppies are supported in the filesystem/FAT layer but got less
  real-world testing than the 1.6MB high-density path (which was validated byte-for-byte
  against `akaiutil` output on a real disk).
- No audio playback or WAV export in this version (by request — metadata browsing only).

## Extending it

The parsing logic in `AkaiDiskCatalog.Core/Filesystem/` is intentionally offset-based and
heavily commented with the source struct layouts, so adding S3000 keygroup or S900 detail
support later is a matter of adding another parser class alongside `AkaiProgramParser`/
`AkaiSampleParser` — no changes needed to the HFE decoder, filesystem reader, database
schema, or UI.
