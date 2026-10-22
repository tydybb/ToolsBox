[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$packageScript = Join-Path $PSScriptRoot 'Package-Release.ps1'
if (-not (Test-Path -LiteralPath $packageScript -PathType Leaf)) {
    throw 'Release packaging script is missing.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ToolsBox-release-packaging-' + [Guid]::NewGuid().ToString('N'))
$version = '2026.10.22.1'
try {
    $repository = Join-Path $fixtureRoot 'repository'
    $selfContained = Join-Path $fixtureRoot 'self-contained'
    $frameworkDependent = Join-Path $fixtureRoot 'framework-dependent'
    $output = Join-Path $fixtureRoot 'release'
    foreach ($directory in @($repository, $selfContained, $frameworkDependent)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $repository 'LICENSE'), 'fixture GPL license')
    [IO.File]::WriteAllText((Join-Path $repository 'NOTICE.md'), 'fixture third-party notices')
    [IO.Directory]::CreateDirectory((Join-Path $repository 'docs/third-party')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $repository 'docs/third-party/component.txt'), 'fixture component license')
    [IO.File]::WriteAllBytes((Join-Path $selfContained '宝哥工具箱.exe'), [byte[]](77, 90, 1, 2))
    [IO.File]::WriteAllBytes((Join-Path $frameworkDependent '宝哥工具箱.exe'), [byte[]](77, 90, 3, 4))

    & $packageScript -Version $version -RepositoryRoot $repository -SelfContainedDirectory $selfContained `
        -FrameworkDependentDirectory $frameworkDependent -OutputDirectory $output

    $assetNames = @(
        "ToolsBox-$version-win-x64-self-contained.exe",
        "ToolsBox-$version-win-x64-framework-dependent.exe",
        "ToolsBox-$version-win-x64-self-contained.zip",
        "ToolsBox-$version-win-x64-framework-dependent.zip"
    )
    $expectedNames = @($assetNames + @('SHA256SUMS.txt', 'LICENSE', 'NOTICE.md')) | Sort-Object
    $actualNames = @(Get-ChildItem -LiteralPath $output -File | Select-Object -ExpandProperty Name) | Sort-Object
    if (Compare-Object $expectedNames $actualNames) { throw 'Unexpected release asset set.' }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $output $assetNames[0]))) -ne '4D5A0102' -or
        [Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $output $assetNames[1]))) -ne '4D5A0304') {
        throw 'The two release names must preserve their corresponding input executables.'
    }

    $checksumLines = @(Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt'))
    if ($checksumLines.Count -ne 6) { throw 'Release checksums must cover both EXEs, both ZIPs, and the legal files.' }
    foreach ($line in $checksumLines) {
        if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9._-]+)$') { throw 'Invalid checksum manifest format.' }
        $actualHash = (Get-FileHash -LiteralPath (Join-Path $output $Matches[2]) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $Matches[1]) { throw 'Release checksum mismatch.' }
    }

    foreach ($mode in @('self-contained', 'framework-dependent')) {
        $stem = "ToolsBox-$version-win-x64-$mode"
        $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $output "$stem.zip"))
        try {
            $expectedEntries = @("$stem.exe", 'LICENSE', 'NOTICE.md', 'docs/third-party/component.txt', 'SHA256SUMS.txt') | Sort-Object
            if (Compare-Object $expectedEntries @($zip.Entries.FullName | Sort-Object)) {
                throw 'A portable ZIP must contain exactly its EXE, license, notices, and checksum manifest.'
            }
            $reader = [IO.StreamReader]::new($zip.GetEntry('SHA256SUMS.txt').Open())
            try { $lines = @($reader.ReadToEnd().Trim() -split '\r?\n') }
            finally { $reader.Dispose() }
            foreach ($line in $lines) {
                if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9._/-]+)$') { throw 'Invalid ZIP checksum manifest.' }
                $expectedHash = $Matches[1]
                $entryName = $Matches[2]
                $sourcePath = if ($entryName -eq "$stem.exe") { Join-Path $output $entryName } else { Join-Path $repository $entryName }
                if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
                    throw 'ZIP checksums do not match the original executable and license files.'
                }
                $entryStream = $zip.GetEntry($entryName).Open()
                try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)).ToLowerInvariant() }
                finally { $entryStream.Dispose() }
                if ($entryHash -ne $expectedHash) { throw 'ZIP entry checksum mismatch.' }
            }
            if ($lines.Count -ne 4) { throw 'ZIP checksums must cover its EXE and all legal files.' }
        }
        finally { $zip.Dispose() }
    }

    $failed = $false
    try {
        & $packageScript -Version $version -RepositoryRoot $repository -SelfContainedDirectory $selfContained `
            -FrameworkDependentDirectory $frameworkDependent -OutputDirectory $output
    }
    catch { $failed = $_.Exception.Message -like '*already exists*' }
    if (-not $failed) { throw 'Packaging must refuse to overwrite an existing release directory.' }

    [IO.File]::Delete((Join-Path $repository 'NOTICE.md'))
    $failed = $false
    try {
        & $packageScript -Version $version -RepositoryRoot $repository -SelfContainedDirectory $selfContained `
            -FrameworkDependentDirectory $frameworkDependent -OutputDirectory (Join-Path $fixtureRoot 'missing-notice')
    }
    catch { $failed = $_.Exception.Message -like '*NOTICE.md*' }
    if (-not $failed) { throw 'Packaging must reject a missing NOTICE.md.' }

    Write-Host 'Release packaging fixture passed: asset set, ZIP entries, checksums, overwrite rejection, and missing NOTICE.md.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $expectedParent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('ToolsBox-release-packaging-')) {
        throw 'Refusing fixture cleanup outside the temporary test directory.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
