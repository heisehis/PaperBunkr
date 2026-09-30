# DjVuLibre 3.5.29 (bundled)

Unmodified files from the official DjVuLibre Windows release, copied to the app output's
`Resources\` folder by `Paperbunkr.App.csproj`. The engine runs them as separate processes to open
DjVu files: `djvm.exe` lists pages, `ddjvu.exe` renders one, `c44.exe` encodes one
(`src/Paperbunkr.Engine/IO/Provider/DjVuImage.cs`, `Readers/DjvuComicProvider.cs`).

- **Source:** `DjVuLibre-3.5.29_DjView-4.12_Setup.exe` (2025-07-14) from
  https://sourceforge.net/projects/djvu/files/DjVuLibre_Windows/3.5.29%2B4.12/ , SHA-256
  `92233fbf891c63f3fb7a0b5e1ce108baa4c29a40c89a442d1313f883aae84670`. The installer is an NSIS
  package; the files were unpacked with 7-Zip, not by running it. All are 32-bit (x86).
- **VC++ runtime:** the DjVuLibre binaries link against the Visual C++ 2015-2022 x86 runtime, which
  their installer only provides by running `vcredist_x86.exe`. `vcruntime140.dll` and
  `msvcp140.dll` (14.23.27820.0, Microsoft-signed) come from that same bundled `vcredist_x86.exe`
  and ship app-local here, as Microsoft allows for these redistributable files. The `api-ms-win-crt-*`
  (Universal CRT) DLLs they also import are part of Windows 10 and later.
- **License:** DjVuLibre is GPL 2.0 or later (`DjVuLibre-COPYING.txt`, the release's own
  `COPYING.txt`). Source for this exact version: https://sourceforge.net/projects/djvu/files/DjVuLibre/3.5.29/ .
  Credits for all of these files are in `THIRD-PARTY-NOTICES.md`.

| File | SHA-256 |
| --- | --- |
| `c44.exe` | `a3587fe3a26e2b0a8795a0baa828b6f18398de875e4bb51fd902fa9e593b8814` |
| `ddjvu.exe` | `5a3dba5d3630ea8947ddbc78fc470468cec1fe0b05d9ddd5957ccf00cf1f58bb` |
| `djvm.exe` | `6c94ab835885ac0da0de51217bfe976f5d641837d8ed6635146f0790a258ce80` |
| `libdjvulibre.dll` | `d0c5f982096b9da04d018c7a966e3aa417a0d9e8790cd88a6b5a388a5c1108ea` |
| `libjpeg.dll` | `978b9e30220e06e4be405ea415be5ead9f77cfba547b705b60bc9935f20c2e4e` |
| `libtiff.dll` | `3cd71ed52e14fceaecaebe8582e91a46ca35808077f4470f56b5a569b0aa8f74` |
| `libz.dll` | `7029b0041b505446668b6ed41435255e41ce225fd934639ff6518e4f506100d2` |
| `msvcp140.dll` | `83c0fe5fab090060de7abe9dc85f5651d0f505a4ecc18f1ee8631941d0d665ea` |
| `vcruntime140.dll` | `cf53710630dcb21133ce3b10765fcd1900b4057fe3b2f806674aaf7dde0a8aa0` |
