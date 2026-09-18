#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[string]]::new()
$succeeded = $false
try {
    $executable = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $dotnetRoot = Join-Path $env:ProgramFiles 'dotnet'
    $hostVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'host\fxr') -Directory |
        Where-Object Name -Like '8.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $hostVersion) { throw 'This diagnostic requires an installed .NET 8 x64 host to construct an isolated test layout.' }
    $workRoot = Join-Path (Split-Path $PSScriptRoot -Parent) ('bin\runtime-check-' + [guid]::NewGuid().ToString('N'))
    $hostFolder = Join-Path $workRoot ('host\fxr\' + $hostVersion.Name)
    New-Item -ItemType Directory -Path $hostFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $hostVersion.FullName 'hostfxr.dll') -Destination $hostFolder

    function Assert-MissingFramework([string]$Framework) {
        $start = [Diagnostics.ProcessStartInfo]::new($executable)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardError = $true
        $start.RedirectStandardOutput = $true
        # These settings are confined to the child; never alter the machine's configuration.
        $start.EnvironmentVariables['DOTNET_ROOT_X64'] = $workRoot
        $start.EnvironmentVariables['DOTNET_ROOT'] = $workRoot
        $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
        $start.EnvironmentVariables['DOTNET_DISABLE_GUI_ERRORS'] = '1'
        $process = [Diagnostics.Process]::Start($start)
        try {
            $errors = $process.StandardError.ReadToEndAsync()
            $output = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit(15000)) {
                # Only terminate this diagnostic's newly created process, not an existing toolbox.
                $process.Kill()
                throw 'Runtime diagnostic timed out.'
            }
            $message = $errors.GetAwaiter().GetResult() + $output.GetAwaiter().GetResult()
            if ($process.ExitCode -eq 0 -or $message -notmatch [regex]::Escape($Framework) -or
                $message -notmatch 'https://aka.ms/dotnet-core-applaunch' -or $message -notmatch 'arch=x64') {
                throw "Missing expected runtime requirement/download information (exit $($process.ExitCode)): $message"
            }
            $results.Add("PASS: missing $Framework; x64 official download URL present; exit $($process.ExitCode).")
            $results.Add($message.Trim())
        }
        finally { $process.Dispose() }
    }

    Assert-MissingFramework 'Microsoft.NETCore.App'
    $coreVersion = Get-ChildItem -LiteralPath (Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App') -Directory |
        Where-Object Name -Like '8.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $coreVersion) { throw 'No installed .NET 8 Core runtime found for the Desktop-only missing case.' }
    $coreFolder = Join-Path $workRoot 'shared\Microsoft.NETCore.App'
    New-Item -ItemType Directory -Path $coreFolder -Force | Out-Null
    Copy-Item -LiteralPath $coreVersion.FullName -Destination $coreFolder -Recurse
    Assert-MissingFramework 'Microsoft.WindowsDesktop.App'
    $succeeded = $true
}
catch {
    $results.Add('FAIL: ' + $_.Exception.Message)
}
finally {
    $results | Write-Output
    if ($ResultPath) {
        [pscustomobject]@{ Succeeded = $succeeded; Messages = $results.ToArray() } |
            ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
    }
}
if (-not $succeeded) { exit 1 }
