[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$publishScript = Join-Path $PSScriptRoot 'Publish-GitHubRelease.ps1'
if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) { throw 'The numeric-ID release publisher is missing.' }
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('ToolsBox-github-release-tests-' + [Guid]::NewGuid().ToString('N'))
$previousToken = $env:GH_TOKEN

# This function shadows the HTTP cmdlet only inside this fixture process. No request leaves it.
function Invoke-RestMethod {
    param($Method = 'Get', $Uri, $Headers, $Body, $ContentType, $InFile)
    $state = $global:ToolsBoxReleaseFixture
    $state.Requests.Add("$Method $Uri")
    if ($Headers.Authorization -cne 'Bearer fixture-only-token') { throw 'Expected the isolated fixture token.' }
    if ($Uri -like '*/releases/tags/*') { throw 'Draft lookup by tag is forbidden in this regression fixture.' }
    if ($Method -eq 'Get' -and $Uri -match '/releases\?per_page=100&page=(\d+)$') {
        $page = [int]$Matches[1]
        if ($state.Scenario -in @('ResumePage2', 'PublishedPage2')) {
            if ($page -eq 1) {
                return ,@(1..100 | ForEach-Object { [pscustomobject]@{ id = 1000 + $_; tag_name = "v0.0.$_"; draft = $false } })
            }
            return ,@($state.Release)
        }
        return ,@()
    }
    if ($Method -eq 'Post' -and $Uri -eq 'https://api.github.com/repos/owner/repository/releases') {
        $request = $Body | ConvertFrom-Json
        if (-not $request.draft -or $request.tag_name -cne $state.Tag) { throw 'Creation must request a draft for the exact tag.' }
        $state.Creates++
        return $state.Release
    }
    if ($Method -eq 'Get' -and $Uri -eq 'https://api.github.com/repos/owner/repository/releases/42') {
        $state.Gets++
        if ($state.Gets -ge 2 -and $state.Scenario -eq 'WrongTag') { $state.Release.tag_name = 'v0.0.0' }
        if ($state.Gets -ge 2 -and $state.Scenario -eq 'PublishedDuringUpload') { $state.Release.draft = $false }
        $state.Release.assets = @($state.Assets)
        return $state.Release
    }
    if ($Method -eq 'Post' -and $Uri -match '^https://uploads.github.com/repos/owner/repository/releases/42/assets\?name=(.+)$') {
        if ($state.Scenario -eq 'UploadFailure' -and $state.Uploads -eq 2) { throw 'fixture upload failure' }
        $name = [Uri]::UnescapeDataString($Matches[1])
        if ($name -cne [IO.Path]::GetFileName($InFile)) { throw 'Upload name does not match its input file.' }
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $InFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($state.Scenario -eq 'HashMismatch' -and $state.Uploads -eq 0) { $digest = 'sha256:' + ('0' * 64) }
        $asset = [pscustomobject]@{ name = $name; size = (Get-Item -LiteralPath $InFile).Length; digest = $digest; state = 'uploaded' }
        $state.Assets.Add($asset)
        $state.Uploads++
        return $asset
    }
    if ($Method -eq 'Patch' -and $Uri -eq 'https://api.github.com/repos/owner/repository/releases/42') {
        $request = $Body | ConvertFrom-Json
        if ($request.draft -ne $false -or $state.Gets -lt 2 -or $state.Assets.Count -ne 7) {
            throw 'Publishing must follow numeric-ID refetch and complete uploads.'
        }
        $state.Patches++
        $state.Release.draft = $false
        return $state.Release
    }
    throw "Unexpected fixture HTTP operation: $Method $Uri"
}

try {
    $assetsDirectory = Join-Path $fixtureRoot 'assets'
    [IO.Directory]::CreateDirectory($assetsDirectory) | Out-Null
    foreach ($name in @('self.exe', 'framework.exe', 'self.zip', 'framework.zip', 'SHA256SUMS.txt', 'LICENSE', 'NOTICE.md')) {
        [IO.File]::WriteAllText((Join-Path $assetsDirectory $name), "synthetic asset: $name")
    }
    $notes = Join-Path $fixtureRoot 'notes.md'
    [IO.File]::WriteAllText($notes, 'Synthetic release notes.')
    $env:GH_TOKEN = 'fixture-only-token'
    $cases = @(
        @{ Name = 'Fresh'; Failure = $null },
        @{ Name = 'ResumePage2'; Failure = $null },
        @{ Name = 'PublishedPage2'; Failure = '*published release already exists*' },
        @{ Name = 'HashMismatch'; Failure = '*verification failed*' },
        @{ Name = 'WrongTag'; Failure = '*tag or draft state changed*' },
        @{ Name = 'PublishedDuringUpload'; Failure = '*tag or draft state changed*' },
        @{ Name = 'UploadFailure'; Failure = '*fixture upload failure*' }
    )
    foreach ($case in $cases) {
        $state = [pscustomobject]@{
            Scenario = $case.Name; Tag = 'v2026.10.22.2'; Creates = 0; Gets = 0; Uploads = 0; Patches = 0
            Requests = [Collections.Generic.List[string]]::new()
            Assets = [Collections.Generic.List[object]]::new()
            Release = [pscustomobject]@{
                id = 42; tag_name = 'v2026.10.22.2'; draft = ($case.Name -ne 'PublishedPage2'); assets = @()
                upload_url = 'https://uploads.github.com/repos/owner/repository/releases/42/assets{?name,label}'
                html_url = 'https://example.test/releases/42'
            }
        }
        if ($case.Name -eq 'ResumePage2') {
            $file = Get-Item -LiteralPath (Join-Path $assetsDirectory 'self.exe')
            $state.Assets.Add([pscustomobject]@{ name = $file.Name; size = $file.Length; state = 'uploaded'
                digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() })
        }
        $global:ToolsBoxReleaseFixture = $state
        $caught = $null
        try {
            & $publishScript -ReleaseTag $state.Tag -Repository 'owner/repository' -ApiUrl 'https://api.github.com' `
                -AssetsDirectory $assetsDirectory -NotesFile $notes
        }
        catch { $caught = $_.Exception.Message }
        if ($null -eq $case.Failure) {
            if ($null -ne $caught) { throw "$($case.Name) unexpectedly failed: $caught" }
            if ($state.Patches -ne 1 -or $state.Release.draft) { throw "$($case.Name) did not publish its verified draft." }
            $expectedUploads = if ($case.Name -eq 'ResumePage2') { 6 } else { 7 }
            if ($state.Uploads -ne $expectedUploads) { throw 'Resume must preserve matching uploaded assets and upload only missing files.' }
            if ($case.Name -eq 'ResumePage2' -and $state.Creates -ne 0) { throw 'A paginated existing draft must be reused.' }
        }
        elseif ($caught -notlike $case.Failure -or $state.Patches -ne 0) {
            throw "$($case.Name) did not stop safely: $caught"
        }
        if ($case.Name -eq 'PublishedPage2' -and ($state.Creates -ne 0 -or $state.Uploads -ne 0)) {
            throw 'An existing published release must not receive writes.'
        }
        Write-Host "Release HTTP fixture passed: $($case.Name)"
    }
}
finally {
    $env:GH_TOKEN = $previousToken
    Remove-Variable -Name ToolsBoxReleaseFixture -Scope Global -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $expectedParent = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath()))
    if ([IO.Path]::GetDirectoryName($resolved) -ne $expectedParent -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('ToolsBox-github-release-tests-')) {
        throw 'Refusing fixture cleanup outside its temporary directory.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
