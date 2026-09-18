# Archive engine provenance and rebuilding

ToolsBox embeds the unmodified Windows x64 `7z.dll` from **7-Zip 26.03**.
Copyright (C) 1999-2026 Igor Pavlov. License: LGPL 2.1 or later, with the
upstream BSD notices and unRAR restriction reproduced in `7-Zip-LICENSE.txt`.
Official release and complete corresponding source:
https://github.com/ip7z/7zip/releases/tag/26.03
The release provides `7z2603-src.7z` and `7z2603-x64.msi`.
Embedded DLL SHA-256: `65E4C1F855F9EF6E8F0F5DF8E3F27D9EB5F07311408639DA0A1CA0B8F4871B0D`.

The managed wrapper is **SharpSevenZip 2.0.128**, pinned as a NuGet dependency.
Copyright Jeremy Ansel and contributors. License: LGPL 3.0 or later;
see `SharpSevenZip-LICENSE.txt`. Corresponding source:
https://github.com/JeremyAnsel/SharpSevenZip/tree/f4c16a6a95ace333fa33f38c1d69e44d77812eee

To use modified library versions: build 7-Zip's `CPP/7zip/Bundles/Format7zF`
target with the upstream Windows x64 build instructions, replace
`src/ToolsBox.Windows/Assets/7z.dll`, update `ExpectedSha256` in
`EmbeddedArchiveEngine.cs` to that DLL's SHA-256, and rebuild ToolsBox with
`dotnet build ToolsBox.slnx`. The hash verifies the selected build; it does
not prohibit replacing or modifying the LGPL library. To modify the wrapper,
build its corresponding source, replace the pinned package with your rebuilt
package or project reference, and rebuild the application. Retain all notices
and provide the corresponding library source with redistributed binaries.
Reverse engineering for debugging modifications to these libraries is permitted
to the extent required by their LGPL licenses.

The DLL is extracted to a stable version-and-hash cache using atomic publication;
existing files are verified and never silently overwritten. Absolute path, no reparse
ancestors, held handles and SHA-256 pinning protect loading. Production archive
operations run only inside the isolated worker. Full decompression targets
`Stream.Null`; no archive entry is written or executed.

Test fixture provenance: ZIP/7z fixtures were created locally with 7-Zip 26.03,
password `test-pass`. Four RAR4/RAR5 fixtures (password `test`) are unmodified
from SharpCompress commit `e04d51176c5d87668c4c8779825342230c33aa74`, directory
`tests/TestArchives/Archives`, under its MIT license, reproduced beside the
test fixtures as `SharpCompress-LICENSE.txt`. Their payloads are never executed
or written to disk during validation.

The additional locally generated `7z-unicode-spaces.7z` fixture uses header
encryption and password ` Aa密码1! `, including its leading and trailing spaces.
It exercises Unicode and whitespace preservation through the actual worker IPC.

Qualification: correct passwords fully verify ZIP ZipCrypto, ZIP AES, 7z data
encryption, 7z header encryption, RAR4 data/header encryption and RAR5 data/header
encryption. An explicit native `Wrong password.` result is `NoMatch`. Generic
encrypted-header open failures and encrypted data/CRC failures are
`RejectedUncertain`: password mismatch and damage cannot reliably be distinguished.
Unsupported methods/volumes, structural failures and resource failures stop the run.
All fixture tests decompress to a null stream, check original file hashes and
verify no output files are created in the fixture directory.
