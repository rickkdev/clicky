param(
    [switch]$Launch
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishRoot = Split-Path -Parent $repoRoot
Set-Location $repoRoot

$project = "windows/src/Clicky.App/Clicky.App.csproj"
$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $publishRoot
)

$staleFiles = @(
    "Clicky.App.exe",
    "Clicky.App.dll",
    "Clicky.App.deps.json",
    "Clicky.App.runtimeconfig.json",
    "Clicky.Api.dll",
    "Clicky.Audio.dll",
    "Clicky.Capture.dll",
    "Clicky.Companion.dll",
    "Clicky.Hotkey.dll",
    "Clicky.Overlay.dll",
    "Clicky.Pointing.dll",
    "Hardcodet.NotifyIcon.Wpf.dll",
    "Microsoft.Extensions.Caching.Abstractions.dll",
    "Microsoft.Extensions.Caching.Memory.dll",
    "Microsoft.Extensions.Configuration.Abstractions.dll",
    "Microsoft.Extensions.Configuration.Binder.dll",
    "Microsoft.Extensions.Configuration.dll",
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
    "Microsoft.Extensions.DependencyInjection.dll",
    "Microsoft.Extensions.Diagnostics.Abstractions.dll",
    "Microsoft.Extensions.Diagnostics.dll",
    "Microsoft.Extensions.Http.dll",
    "Microsoft.Extensions.Logging.Abstractions.dll",
    "Microsoft.Extensions.Logging.dll",
    "Microsoft.Extensions.Options.ConfigurationExtensions.dll",
    "Microsoft.Extensions.Options.dll",
    "Microsoft.Extensions.Primitives.dll",
    "Microsoft.Windows.SDK.NET.dll",
    "NAudio.Asio.dll",
    "NAudio.Core.dll",
    "NAudio.dll",
    "NAudio.Midi.dll",
    "NAudio.Wasapi.dll",
    "NAudio.WinForms.dll",
    "NAudio.WinMM.dll",
    "PostHog.dll",
    "WinRT.Runtime.dll"
)

Get-Process Clicky.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

foreach ($file in $staleFiles)
{
    $path = Join-Path $publishRoot $file
    if (Test-Path -LiteralPath $path)
    {
        Remove-Item -LiteralPath $path -Force
    }
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishRoot "Clicky.App.exe"
$version = (Get-Item $publishedExe).VersionInfo.ProductVersion
Write-Host "Published Clicky.App.exe ($version) to $publishRoot"

if ($Launch)
{
    Start-Process -FilePath $publishedExe
}
