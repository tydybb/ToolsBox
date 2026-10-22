[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'TestResults')
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$results = [IO.Path]::GetFullPath($ResultsDirectory)
$projects = @('ToolsBox.Core.Tests', 'ToolsBox.Windows.Tests', 'ToolsBox.MediaDownloads.Tests', 'ToolsBox.App.Tests')
$unsafeTest = 'ToolsBox.App.Tests.Startup.SystemHandleScannerTests.CloseHandleAsync_ReleasesChildProcessFileHandleWithoutEndingProcess'
Push-Location -LiteralPath $repository
try {
    foreach ($name in $projects) {
        $arguments = @('test', "tests/$name/$name.csproj", '--configuration', 'Release',
            '--logger', "trx;LogFileName=$name.trx", '--results-directory', $results,
            '--blame-hang', '--blame-hang-timeout', '3m')
        if ($name -eq 'ToolsBox.App.Tests') { $arguments += @('--filter', "FullyQualifiedName!=$unsafeTest") }
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "$name failed with exit code $LASTEXITCODE." }
    }
}
finally { Pop-Location }
