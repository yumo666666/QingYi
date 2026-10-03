$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $MyInvocation.MyCommand.Path
$localSdk = Join-Path $workspace '.tools\dotnet\dotnet.exe'

if (Test-Path -LiteralPath $localSdk) {
    $dotnet = $localSdk
} else {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $command) { throw '未找到 .NET 8 SDK。请安装 .NET 8 SDK 后重试。' }
    $dotnet = $command.Source
}

& $dotnet publish (Join-Path $workspace '轻译\轻译.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableWindowsTargeting=true -o (Join-Path $workspace '发布版')
if ($LASTEXITCODE -ne 0) { throw "发布失败，dotnet publish 返回 $LASTEXITCODE。" }
Write-Host (Join-Path $workspace '发布版\轻译.exe')
