# Open-Source Notices

**Last updated:** 2026-09-30

Paperbunkr is built on open-source software written by other people. This file lists the
third-party software, fonts and artwork that ship with the app, and the licenses they come under.
Paperbunkr's own license is in [LICENSE](LICENSE). The MIT and Apache 2.0 texts are included at the
end; the other licenses are linked to their official text.

## MIT License

- **Avalonia**, **Avalonia.Controls.DataGrid**, **Avalonia.Themes.Fluent**, **Avalonia.Desktop**,
  **Avalonia.AvaloniaEdit**: copyright © The AvaloniaUI Project.
- **Avalonia.Controls.WebView**: copyright © AvaloniaUI OÜ. Book pages are shown with Microsoft Edge
  WebView2, which Windows provides and Paperbunkr does not ship.
- **FluentAvaloniaUI**, **FluentIcons.Avalonia** (Fluent System Icons), **DialogHost.Avalonia**,
  **Optris.Icons.Avalonia**.
- **CommunityToolkit.Mvvm**: copyright © .NET Foundation and Contributors.
- **Entity Framework Core**, **Microsoft.Data.Sqlite**, the **Microsoft.Extensions** and **System**
  packages, and **Roslyn C# scripting**: copyright © .NET Foundation and Contributors.
- **SkiaSharp** and **HarfBuzzSharp**: copyright © .NET Foundation and Contributors.
- **Svg.Skia**: copyright © Wiesław Šoltés.
- **ScottPlot**: copyright © Scott Harden / Harden Technologies, LLC.
- **NetSparkle** (NetSparkleUpdater): copyright © the NetSparkle contributors.
- **SharpCompress**: copyright © Adam Hathcock.
- **SharpZipLib**: copyright © SharpZipLib Contributors.
- **DynamicExpresso**: copyright © Davide Icardi.
- **ImageHash** (CoenM.ImageSharp.ImageHash): by coenm and contributors.
- **MySqlConnector**: copyright © Bradley Grainger.
- **Makaretu.Dns.Multicast.New** (mDNS): copyright © Richard Schneider, jdomnitz.
- **flag-icons** (country flags): copyright © Panayiotis Lipiridis.
- **ONNX Runtime** (Microsoft.ML.OnnxRuntime): copyright © Microsoft Corporation. Runs the guided-view
  panel detector on your computer; nothing is sent anywhere.

## Apache License 2.0

- **Manga Panel and Text Detector (YOLO26-nano)** (`Models/panel-detector.onnx`, guided-view panel
  detection): model by Leandro Narosky ([leoxs22/manga-panel-detector-yolo26n](https://huggingface.co/leoxs22/manga-panel-detector-yolo26n)),
  built on Ultralytics YOLO, distributed here as the unchanged ONNX export
  [mednasserallah/manga-panel-detector-yolo26n-onnx](https://huggingface.co/mednasserallah/manga-panel-detector-yolo26n-onnx),
  Apache 2.0. Paperbunkr does not retrain or modify its weights. It was trained on the Manga109-s dataset,
  credited under Data below.

- **ImageSharp** 2.1: copyright © Six Labors.
- **IronPython**: copyright © .NET Foundation and Contributors.
- **pdfium-binaries**: copyright © Benoît Blanchon. It packages **PDFium**, copyright © The PDFium
  Authors, under the [BSD 3-Clause License](https://pdfium.googlesource.com/pdfium/+/main/LICENSE)
  with parts under Apache 2.0.
- **Material Design Icons** (through Optris.Icons.Avalonia): copyright © Pictogrammers.

## GNU Lesser General Public License

These are separate library files next to `Paperbunkr.exe`, which you may replace with your own
builds of the same libraries.

- **7-Zip** (`7z.dll`): copyright © Igor Pavlov, [GNU LGPL 2.1 or later](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html),
  with parts under the BSD 3-Clause License and the unRAR restriction (the RAR code may not be used
  to create a RAR-compatible archiver). Source: [7-zip.org](https://www.7-zip.org).
- **libheif** (`libheif.dll`) and **libde265** (`libde265.dll`): copyright © struktur AG,
  [GNU LGPL 3.0](https://www.gnu.org/licenses/lgpl-3.0.html).
- **LibHeifSharp**: copyright © Nicholas Hayes and contributors, GNU LGPL 3.0 or later.

## GNU General Public License

- **x265** (`libx265.dll`, shipped with the libheif native package): copyright © MulticoreWare,
  [GNU GPL 2.0 or later](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html), used here under
  its "or later" terms, which are compatible with Paperbunkr's AGPL 3.0.
- **DjVuLibre** 3.5.29 (`Resources\ddjvu.exe`, `djvm.exe`, `c44.exe` and `libdjvulibre.dll`, used
  to open DjVu files): copyright © Léon Bottou and Yann Le Cun, © AT&T, and © LizardTech Software,
  [GNU GPL 2.0 or later](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html) (the full text ships
  as `Resources\DjVuLibre-COPYING.txt`), used here under its "or later" terms, which are compatible
  with Paperbunkr's AGPL 3.0. These are the unmodified programs from the official Windows release,
  which Paperbunkr runs as separate processes. Source for this version:
  [DjVuLibre 3.5.29](https://sourceforge.net/projects/djvu/files/DjVuLibre/3.5.29/), from
  [djvu.sourceforge.net](https://djvu.sourceforge.net).

## Other licenses

- **PDFiumSharp** (PDFiumSharpV2): [Microsoft Reciprocal License](https://opensource.org/license/ms-rl-html).
- **CSJ2K** (JPEG 2000): copyright © JJ2000 Partners, Jason S. Clary, Anders Gustafsson / Cureos AB,
  [BSD License](https://opensource.org/license/bsd-3-clause).
- **AOM** (`aom.dll`, AV1): copyright © Alliance for Open Media,
  [BSD 2-Clause License](https://aomedia.org/license/software-license/).
- **ANGLE** (`av_libglesv2.dll`, graphics): copyright © The ANGLE Project Authors,
  [BSD 3-Clause License](https://chromium.googlesource.com/angle/angle/+/main/LICENSE).
- **SQLite** (`e_sqlite3.dll`): in the public domain.
- **libjpeg** (`Resources\libjpeg.dll`, shipped with DjVuLibre): this software is based in part on the
  work of the Independent JPEG Group, under the [IJG License](https://www.ijg.org).
- **LibTIFF** (`Resources\libtiff.dll`, shipped with DjVuLibre): copyright © Sam Leffler and
  © Silicon Graphics, Inc., under the [LibTIFF License](https://libtiff.gitlab.io/libtiff/project/license.html).
- **zlib** (`Resources\libz.dll`, shipped with DjVuLibre): copyright © Jean-loup Gailly and Mark Adler,
  under the [zlib License](https://zlib.net/zlib_license.html).
- **Microsoft Visual C++ runtime** (`Resources\vcruntime140.dll`, `msvcp140.dll`, which the DjVuLibre
  tools need): copyright © Microsoft Corporation, redistributed under the Visual Studio
  redistributable terms.
- **VersOne.Epub**: released into the public domain under [The Unlicense](https://unlicense.org).

## Data

- **Manga109-s**: the panel detector above was trained on the [Manga109-s](https://huggingface.co/datasets/hal-utokyo/Manga109-s)
  dataset (Aizawa et al., "Building a Manga Dataset 'Manga109' with Annotations for Multimedia Applications",
  IEEE MultiMedia 27(2), 2020; Matsui et al., "Sketch-based Manga Retrieval using Manga109 Dataset",
  Multimedia Tools and Applications 76(20), 2017). The dataset's terms allow models trained on it to be
  used commercially when its use is indicated, which this entry does. Paperbunkr does not ship the dataset.
- **Grand Comics Database™** (optional download under Preferences → Connections): series names, issue
  numbers, on-sale and key dates, and series bonds from the [Grand Comics Database](https://www.comics.org),
  licensed under [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). The downloaded extract is
  itself CC BY-SA 4.0, is published at [heisehis/paperbunkr-gcd-data](https://github.com/heisehis/paperbunkr-gcd-data)
  (which also says what was changed from the GCD dump), and records the dump it was made from; Preferences
  shows that date. Lines
  that come from it are marked GCD and link to their page on comics.org. The Grand Comics Database does
  not endorse Paperbunkr.

## Fonts

All under the [SIL Open Font License 1.1](https://openfontlicense.org). The license texts for the two
fonts in `Assets/Fonts` ship alongside them.

- **Inter** (through Avalonia.Fonts.Inter): copyright © The Inter Project Authors.
- **Bebas Neue**: copyright © 2010 Dharma Type.
- **Source Serif 4**: copyright © 2014 The Source Serif 4 Project Authors.
- **Font Awesome Free** (through Optris.Icons.Avalonia): copyright © Fonticons, Inc. Its icons are
  under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), its font files under the SIL Open
  Font License 1.1.

## Logos and artwork

- Publisher, imprint, service and tracker logos are trademarks of their owners. They are shown only
  to identify the publisher or service, and their use does not mean the owners endorse Paperbunkr.
- Several service logos come from [Simple Icons](https://simpleicons.org) (CC0 1.0), and the ESRB
  rating marks from Wikimedia Commons (public domain).
- The Valiant logo is from Wikimedia Commons
  ([File:Valiant-logo.svg](https://commons.wikimedia.org/wiki/File:Valiant-logo.svg)), under
  [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/); see that page for its author.
- The Metron logo comes from the [Metron project](https://github.com/Metron-Project/metron) (GPL 3.0).
- The publisher icon pack and some reader background textures come from
  [ComicRack Community Edition](https://github.com/maforget/ComicRackCE).

## MIT License text

```
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Apache License 2.0 text

Apache License
Version 2.0, January 2004
http://www.apache.org/licenses/

TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION

1. Definitions.

"License" shall mean the terms and conditions for use, reproduction, and distribution as defined by Sections 1 through 9 of this document.

"Licensor" shall mean the copyright owner or entity authorized by the copyright owner that is granting the License.

"Legal Entity" shall mean the union of the acting entity and all other entities that control, are controlled by, or are under common control with that entity. For the purposes of this definition, "control" means (i) the power, direct or indirect, to cause the direction or management of such entity, whether by contract or otherwise, or (ii) ownership of fifty percent (50%) or more of the outstanding shares, or (iii) beneficial ownership of such entity.

"You" (or "Your") shall mean an individual or Legal Entity exercising permissions granted by this License.

"Source" form shall mean the preferred form for making modifications, including but not limited to software source code, documentation source, and configuration files.

"Object" form shall mean any form resulting from mechanical transformation or translation of a Source form, including but not limited to compiled object code, generated documentation, and conversions to other media types.

"Work" shall mean the work of authorship, whether in Source or Object form, made available under the License, as indicated by a copyright notice that is included in or attached to the work (an example is provided in the Appendix below).

"Derivative Works" shall mean any work, whether in Source or Object form, that is based on (or derived from) the Work and for which the editorial revisions, annotations, elaborations, or other modifications represent, as a whole, an original work of authorship. For the purposes of this License, Derivative Works shall not include works that remain separable from, or merely link (or bind by name) to the interfaces of, the Work and Derivative Works thereof.

"Contribution" shall mean any work of authorship, including the original version of the Work and any modifications or additions to that Work or Derivative Works thereof, that is intentionally submitted to Licensor for inclusion in the Work by the copyright owner or by an individual or Legal Entity authorized to submit on behalf of the copyright owner. For the purposes of this definition, "submitted" means any form of electronic, verbal, or written communication sent to the Licensor or its representatives, including but not limited to communication on electronic mailing lists, source code control systems, and issue tracking systems that are managed by, or on behalf of, the Licensor for the purpose of discussing and improving the Work, but excluding communication that is conspicuously marked or otherwise designated in writing by the copyright owner as "Not a Contribution."

"Contributor" shall mean Licensor and any individual or Legal Entity on behalf of whom a Contribution has been received by Licensor and subsequently incorporated within the Work.

2. Grant of Copyright License. Subject to the terms and conditions of this License, each Contributor hereby grants to You a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable copyright license to reproduce, prepare Derivative Works of, publicly display, publicly perform, sublicense, and distribute the Work and such Derivative Works in Source or Object form.

3. Grant of Patent License. Subject to the terms and conditions of this License, each Contributor hereby grants to You a perpetual, worldwide, non-exclusive, no-charge, royalty-free, irrevocable (except as stated in this section) patent license to make, have made, use, offer to sell, sell, import, and otherwise transfer the Work, where such license applies only to those patent claims licensable by such Contributor that are necessarily infringed by their Contribution(s) alone or by combination of their Contribution(s) with the Work to which such Contribution(s) was submitted. If You institute patent litigation against any entity (including a cross-claim or counterclaim in a lawsuit) alleging that the Work or a Contribution incorporated within the Work constitutes direct or contributory patent infringement, then any patent licenses granted to You under this License for that Work shall terminate as of the date such litigation is filed.

4. Redistribution. You may reproduce and distribute copies of the Work or Derivative Works thereof in any medium, with or without modifications, and in Source or Object form, provided that You meet the following conditions:

You must give any other recipients of the Work or Derivative Works a copy of this License; and


You must cause any modified files to carry prominent notices stating that You changed the files; and


You must retain, in the Source form of any Derivative Works that You distribute, all copyright, patent, trademark, and attribution notices from the Source form of the Work, excluding those notices that do not pertain to any part of the Derivative Works; and


If the Work includes a "NOTICE" text file as part of its distribution, then any Derivative Works that You distribute must include a readable copy of the attribution notices contained within such NOTICE file, excluding those notices that do not pertain to any part of the Derivative Works, in at least one of the following places: within a NOTICE text file distributed as part of the Derivative Works; within the Source form or documentation, if provided along with the Derivative Works; or, within a display generated by the Derivative Works, if and wherever such third-party notices normally appear. The contents of the NOTICE file are for informational purposes only and do not modify the License. You may add Your own attribution notices within Derivative Works that You distribute, alongside or as an addendum to the NOTICE text from the Work, provided that such additional attribution notices cannot be construed as modifying the License.
You may add Your own copyright statement to Your modifications and may provide additional or different license terms and conditions for use, reproduction, or distribution of Your modifications, or for any such Derivative Works as a whole, provided Your use, reproduction, and distribution of the Work otherwise complies with the conditions stated in this License.

5. Submission of Contributions. Unless You explicitly state otherwise, any Contribution intentionally submitted for inclusion in the Work by You to the Licensor shall be under the terms and conditions of this License, without any additional terms or conditions. Notwithstanding the above, nothing herein shall supersede or modify the terms of any separate license agreement you may have executed with Licensor regarding such Contributions.

6. Trademarks. This License does not grant permission to use the trade names, trademarks, service marks, or product names of the Licensor, except as required for reasonable and customary use in describing the origin of the Work and reproducing the content of the NOTICE file.

7. Disclaimer of Warranty. Unless required by applicable law or agreed to in writing, Licensor provides the Work (and each Contributor provides its Contributions) on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied, including, without limitation, any warranties or conditions of TITLE, NON-INFRINGEMENT, MERCHANTABILITY, or FITNESS FOR A PARTICULAR PURPOSE. You are solely responsible for determining the appropriateness of using or redistributing the Work and assume any risks associated with Your exercise of permissions under this License.

8. Limitation of Liability. In no event and under no legal theory, whether in tort (including negligence), contract, or otherwise, unless required by applicable law (such as deliberate and grossly negligent acts) or agreed to in writing, shall any Contributor be liable to You for damages, including any direct, indirect, special, incidental, or consequential damages of any character arising as a result of this License or out of the use or inability to use the Work (including but not limited to damages for loss of goodwill, work stoppage, computer failure or malfunction, or any and all other commercial damages or losses), even if such Contributor has been advised of the possibility of such damages.

9. Accepting Warranty or Additional Liability. While redistributing the Work or Derivative Works thereof, You may choose to offer, and charge a fee for, acceptance of support, warranty, indemnity, or other liability obligations and/or rights consistent with this License. However, in accepting such obligations, You may act only on Your own behalf and on Your sole responsibility, not on behalf of any other Contributor, and only if You agree to indemnify, defend, and hold each Contributor harmless for any liability incurred by, or claims asserted against, such Contributor by reason of your accepting any such warranty or additional liability.

END OF TERMS AND CONDITIONS
