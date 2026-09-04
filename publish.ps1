#Requires -Version 5.1
<#
Builds a self-updating single .exe for distribution/install.
Framework-dependent (not self-contained): needs the .NET 9 Desktop Runtime on the
target machine, but produces a single ~350KB exe instead of a ~150MB bundle.
Get the runtime from https://dotnet.microsoft.com/download/dotnet/9.0 if a machine
doesn't already have it (run `dotnet --list-runtimes` to check).
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "src\OpenDnsUpdater\OpenDnsUpdater.csproj"
$out = Join-Path $PSScriptRoot $OutputDir

dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=none `
    -o $out

Write-Host ""
Write-Host "Published: $out\OpenDnsUpdater.exe"
Write-Host "Run it once, then right-click its tray icon and choose Settings to configure it."
