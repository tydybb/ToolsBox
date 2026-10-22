[CmdletBinding()]
param(
    [ValidatePattern('^v\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$ReleaseTag = $env:RELEASE_TAG,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$ApiUrl = $env:GITHUB_API_URL,
    [string]$AssetsDirectory = 'bin/release',
    [string]$NotesFile = "docs/releases/$ReleaseTag.md"
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'GH_TOKEN is required.' }
if ([string]::IsNullOrWhiteSpace($ReleaseTag) -or [string]::IsNullOrWhiteSpace($Repository)) { throw 'A release tag and repository are required.' }
if ([string]::IsNullOrWhiteSpace($ApiUrl)) { $ApiUrl = 'https://api.github.com' }
$apiUri = [Uri]$ApiUrl
if ($apiUri.Scheme -cne 'https' -or $apiUri.Host -cne 'api.github.com' -or $apiUri.Port -ne 443 -or
    $apiUri.UserInfo -ne '' -or $apiUri.AbsolutePath.TrimEnd('/') -ne '' -or $apiUri.Query -ne '' -or $apiUri.Fragment -ne '') {
    throw 'Only the official GitHub API endpoint is supported.'
}
$baseUrl = "https://api.github.com/repos/$Repository/releases"
$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$notes = Get-Content -LiteralPath $NotesFile -Raw
$assets = @(Get-ChildItem -LiteralPath $AssetsDirectory -File | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Name = $_.Name; Path = $_.FullName; Size = $_.Length
        Digest = 'sha256:' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
if ($assets.Count -ne 7) { throw 'Expected both EXEs, both ZIPs, checksums, LICENSE, and NOTICE.md.' }

function Find-Release {
    for ($page = 1; ; $page++) {
        $response = Invoke-RestMethod -Method Get -Uri "$($baseUrl)?per_page=100&page=$page" -Headers $headers
        $releases = @($response)
        $matching = @($releases | Where-Object { $_.tag_name -ceq $ReleaseTag })
        if ($matching.Count -gt 1) { throw 'Multiple releases have the exact requested tag.' }
        if ($matching.Count -eq 1) { return $matching[0] }
        if ($releases.Count -lt 100) { return $null }
    }
}

function Get-VerifiedDraft {
    $draft = Invoke-RestMethod -Method Get -Uri $releaseUrl -Headers $headers
    if ($draft.id -ne $releaseId -or $draft.tag_name -cne $ReleaseTag -or $draft.draft -ne $true) {
        throw 'Release ID, tag or draft state changed; refusing to modify it.'
    }
    return $draft
}

function Assert-Asset($Remote, $Local) {
    if ($Remote.state -cne 'uploaded' -or $Remote.size -ne $Local.Size -or $Remote.digest -cne $Local.Digest) {
        throw "Uploaded asset verification failed: $($Local.Name)"
    }
}

$release = Find-Release
if ($null -ne $release -and $release.draft -ne $true) {
    throw 'A published release already exists; refusing to overwrite it.'
}
if ($null -eq $release) {
    $body = @{ tag_name = $ReleaseTag; name = "ToolsBox $ReleaseTag"; body = $notes; draft = $true } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Post -Uri $baseUrl -Headers $headers -ContentType 'application/json' -Body $body
}
$releaseId = 0L
if (-not [long]::TryParse([string]$release.id, [ref]$releaseId) -or $releaseId -le 0) { throw 'GitHub did not return a numeric release ID.' }
$releaseUrl = "$baseUrl/$releaseId"
$draft = Get-VerifiedDraft
$assetNames = @($assets.Name)
foreach ($remote in @($draft.assets)) {
    if ($assetNames -cnotcontains $remote.name) { throw 'The existing draft contains unexpected assets; refusing to replace them.' }
}
$uploadUrl = ([string]$draft.upload_url -replace '\{.*$', '')
$uploadUri = [Uri]$uploadUrl
if ($uploadUri.Scheme -cne 'https' -or $uploadUri.Host -cne 'uploads.github.com' -or $uploadUri.Port -ne 443 -or
    $uploadUri.UserInfo -ne '' -or $uploadUri.AbsolutePath -cne "/repos/$Repository/releases/$releaseId/assets" -or
    $uploadUri.Query -ne '' -or $uploadUri.Fragment -ne '') {
    throw 'GitHub returned an unexpected release upload URL.'
}
foreach ($asset in $assets) {
    $existingAssets = @($draft.assets | Where-Object { $_.name -ceq $asset.Name })
    if ($existingAssets.Count -gt 1) { throw "The draft has duplicate assets: $($asset.Name)" }
    if ($existingAssets.Count -eq 1) {
        Assert-Asset $existingAssets[0] $asset
        continue
    }
    $uri = "$($uploadUrl)?name=$([Uri]::EscapeDataString($asset.Name))"
    Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/octet-stream' -InFile $asset.Path | Out-Null
}
$draft = Get-VerifiedDraft
if (@($draft.assets).Count -ne $assets.Count) { throw 'Uploaded asset count changed; the release remains a draft.' }
foreach ($asset in $assets) {
    $remote = @($draft.assets | Where-Object { $_.name -ceq $asset.Name })
    if ($remote.Count -ne 1) { throw "Uploaded asset verification failed: $($asset.Name)" }
    Assert-Asset $remote[0] $asset
}
$body = @{ draft = $false; name = "ToolsBox $ReleaseTag"; body = $notes } | ConvertTo-Json
$published = Invoke-RestMethod -Method Patch -Uri $releaseUrl -Headers $headers -ContentType 'application/json' -Body $body
if ($published.id -ne $releaseId -or $published.tag_name -cne $ReleaseTag -or $published.draft -ne $false) {
    throw 'GitHub did not confirm publication of the verified release.'
}
Write-Host "Published release: $($published.html_url)"
