# Third-party notices

The MIT license at the repository root covers the Estudar application code.

- PDF.js 6.4.299 (Mozilla and contributors), Apache-2.0: vendored from the official `pdfjs-dist` npm package. Its license is in `Estudo.Windows/Viewer/LICENSE`, with additional font, decoder and color-management notices in that directory's `standard_fonts` and `wasm` folders. `Viewer/VERSION` records the package version and npm integrity digest. The custom `index.html` and `viewer.mjs` are Estudar code.
- Microsoft.Web.WebView2 1.0.4258.31: Microsoft redistribution license and notices in `licenses/WebView2-LICENSE.txt` and `licenses/WebView2-NOTICE.txt`. The Evergreen WebView2 Runtime is installed separately and is not bundled in the release.
- Microsoft.Data.Sqlite 10.0.13 and .NET: MIT; see `licenses/dotnet-MIT.txt`. Self-contained Windows distributions include the .NET runtime and its third-party notices.
- SQLitePCLRaw 2.1.13 (SourceGear, LLC): Apache-2.0; see `licenses/SQLitePCLRaw-LICENSE.txt`. SQLite is public domain: https://sqlite.org/copyright.html.

Exam documents are not included in this repository or in application releases. The catalogue contains factual metadata, official URLs, integrity hashes and ENEM answer/page references. Downloaded documents retain the rights and notices of their respective official publishers (Inep, Cebraspe and UnB); the application's MIT license does not license those documents. Some catalogue entries are preliminary, adapted or carry version warnings; the library preserves these labels.

Segoe UI is provided by Windows and is not redistributed. No third-party icon font is bundled.
