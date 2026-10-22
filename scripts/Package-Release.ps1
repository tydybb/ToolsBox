[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$SelfContainedDirectory,
    [Parameter(Mandatory = $true)]
    [string]$FrameworkDependentDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Release output already exists: $output" }

$legalFiles = foreach ($name in @('LICENSE', 'NOTICE.md')) {
    $path = Join-Path $repository $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required $name file." }
    [pscustomobject]@{ Name = $name; Path = $path }
}
$thirdPartyRoot = Join-Path $repository 'docs/third-party'
if (-not (Test-Path -LiteralPath $thirdPartyRoot -PathType Container)) {
    throw 'Missing required docs/third-party license directory.'
}
$thirdPartyFiles = @(Get-ChildItem -LiteralPath $thirdPartyRoot -File -Recurse | Sort-Object FullName)
if ($thirdPartyFiles.Count -eq 0) { throw 'The docs/third-party license directory is empty.' }
foreach ($file in $thirdPartyFiles) {
    $name = [IO.Path]::GetRelativePath($repository, $file.FullName).Replace('\', '/')
    $legalFiles += [pscustomobject]@{ Name = $name; Path = $file.FullName }
}

$modes = @(
    [pscustomobject]@{ Name = 'self-contained'; Directory = $SelfContainedDirectory },
    [pscustomobject]@{ Name = 'framework-dependent'; Directory = $FrameworkDependentDirectory }
)
foreach ($mode in $modes) {
    $executable = Join-Path $mode.Directory '宝哥工具箱.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or (Get-Item -LiteralPath $executable).Length -eq 0) {
        throw "Missing or empty $($mode.Name) portable executable."
    }
    $files = @(Get-ChildItem -LiteralPath $mode.Directory -File -Recurse -Force)
    if ($files.Count -ne 1) { throw "The $($mode.Name) publish contains sidecar files; a standalone EXE is required." }
}

[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($name in @('LICENSE', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $repository $name) -Destination (Join-Path $output $name)
}
$assetNames = [Collections.Generic.List[string]]::new()
foreach ($mode in $modes) {
    $stem = "ToolsBox-$Version-win-x64-$($mode.Name)"
    $exeName = "$stem.exe"
    $executable = Join-Path $output $exeName
    Copy-Item -LiteralPath (Join-Path $mode.Directory '宝哥工具箱.exe') -Destination $executable
    $entries = @([pscustomobject]@{ Name = $exeName; Path = $executable }) + @($legalFiles)
    $checksums = foreach ($entry in $entries) {
        $hash = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($entry.Name)"
    }
    $zipName = "$stem.zip"
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $output $zipName), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Path, $entry.Name,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        $manifest = $zip.CreateEntry('SHA256SUMS.txt')
        $writer = [IO.StreamWriter]::new($manifest.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($checksums -join "`n") + "`n") }
        finally { $writer.Dispose() }
    }
    finally { $zip.Dispose() }
    $assetNames.Add($exeName)
    $assetNames.Add($zipName)
}
$assetNames.Add('LICENSE')
$assetNames.Add('NOTICE.md')
$checksums = foreach ($name in $assetNames) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), [string[]]$checksums, [Text.UTF8Encoding]::new($false))
Write-Host "Release assets created: $output"
