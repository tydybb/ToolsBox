[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string]$Mode,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$originalLocation = Get-Location
$publishLock = $null
$exitCode = 1
$sdkDownload = 'https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0'

try {
    $repository = Split-Path -Parent $PSScriptRoot
    Set-Location -LiteralPath $repository

    Write-Host '[1/3] 检查 .NET SDK...'
    $dotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $dotnet) {
        throw "找不到 dotnet 命令。请安装 .NET 8 SDK 或更高版本后重新打开窗口：$sdkDownload"
    }
    try {
        $sdkList = @(& $dotnet.Source --list-sdks 2>&1)
        $sdkExitCode = $LASTEXITCODE
    }
    catch {
        throw "无法检查 .NET SDK。请确认已安装 SDK：$sdkDownload`n$($_.Exception.Message)"
    }
    $supportedSdk = @($sdkList | Where-Object { "$_" -match '^\s*(\d+)\.\d+\.\d+' -and [int]$Matches[1] -ge 8 })
    if ($sdkExitCode -ne 0 -or $supportedSdk.Count -eq 0) {
        throw "未检测到可用的 .NET 8 或更高版本 SDK，仅安装 Runtime 无法打包。SDK 下载：$sdkDownload"
    }

    $publishRoot = Join-Path $repository 'bin\publish'
    [System.IO.Directory]::CreateDirectory($publishRoot) | Out-Null
    try {
        $publishLock = [System.IO.File]::Open(
            (Join-Path $publishRoot 'publish.lock'),
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw '另一个发布任务正在使用此仓库，请等待其结束后重试。'
    }

    $selfContained = 'false'
    $modeDirectory = 'framework-dependent-win-x64'
    if ($Mode -eq 'SelfContained') {
        $selfContained = 'true'
        $modeDirectory = 'self-contained-win-x64'
    }
    $uniqueDirectory = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss-fff'), ([Guid]::NewGuid().ToString('N'))
    $outputDirectory = Join-Path (Join-Path $publishRoot $modeDirectory) $uniqueDirectory
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    $project = Join-Path $repository 'src\ToolsBox.App\ToolsBox.App.csproj'
    $publishArguments = @(
        'publish', $project,
        '--configuration', 'Release',
        '--framework', 'net8.0-windows',
        '--runtime', 'win-x64',
        '--self-contained', $selfContained,
        '-p:PublishProfile=PortableWinX64',
        "-p:EnableCompressionInSingleFile=$selfContained",
        '--output', $outputDirectory
    )

    Write-Host "[2/3] 正在还原依赖并发布（$Mode）..."
    & $dotnet.Source @publishArguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet publish 失败，退出码：$exitCode。请检查上方构建日志。"
    }

    Write-Host '[3/3] 校验发布文件...'
    $executablePath = Join-Path $outputDirectory '宝哥工具箱.exe'
    if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
        throw "发布未生成预期文件：$executablePath"
    }
    $executable = Get-Item -LiteralPath $executablePath
    if ($executable.Length -le 0) {
        throw "发布生成了空文件：$executablePath"
    }

    Write-Host ''
    Write-Host '发布成功！'
    Write-Host "文件：$executablePath"
    Write-Host ('大小：{0:N2} MiB' -f ($executable.Length / 1MB))
    $exitCode = 0
}
catch {
    if ($exitCode -eq 0) { $exitCode = 1 }
    Write-Host ''
    Write-Host ("发布失败：{0}" -f $_.Exception.Message) -ForegroundColor Red
}
finally {
    if ($null -ne $publishLock) { $publishLock.Dispose() }
    Set-Location -LiteralPath $originalLocation.Path
    if (-not $NoPause) {
        Write-Host '按 Enter 键关闭窗口'
        try { Read-Host | Out-Null }
        catch { Write-Host '无法读取输入，窗口即将关闭。' }
    }
}

exit $exitCode
