$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameModsDir = "C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher 2\Mods"

function Copy-BuiltFile {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    if (!(Test-Path $Source)) {
        throw "Build output not found at $Source. Run .\build.ps1 first."
    }

    try {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
    }
    catch {
        throw "Failed to copy '$Source' to '$Destination'. Is Slime Rancher 2 still running? Original error: $($_.Exception.Message)"
    }
}

function Assert-InstalledHashMatches {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    if (!(Test-Path $Destination)) {
        throw "Installed output not found: $Destination"
    }

    $srcHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Source).Hash
    $dstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
    Write-Host "RancherMinimap source SHA256:    $srcHash"
    Write-Host "RancherMinimap installed SHA256: $dstHash"

    if ($srcHash -ne $dstHash) {
        throw "Installed RancherMinimap.dll hash does not match freshly built DLL. Source=$srcHash Installed=$dstHash"
    }
}

New-Item -ItemType Directory -Force -Path $gameModsDir | Out-Null
$sourceDll = Join-Path $projectRoot "dist\RancherMinimap.dll"
$sourcePdb = Join-Path $projectRoot "dist\RancherMinimap.pdb"
$installedDll = Join-Path $gameModsDir "RancherMinimap.dll"
$installedPdb = Join-Path $gameModsDir "RancherMinimap.pdb"
$oldInstalledDll = Join-Path $gameModsDir "SR2FreshMinimap.dll"
$oldInstalledPdb = Join-Path $gameModsDir "SR2FreshMinimap.pdb"

Remove-Item -LiteralPath $installedDll -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installedPdb -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $oldInstalledDll -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $oldInstalledPdb -Force -ErrorAction SilentlyContinue
Copy-BuiltFile $sourceDll $installedDll
Copy-BuiltFile $sourcePdb $installedPdb
Assert-InstalledHashMatches $sourceDll $installedDll

Write-Host "Installed RancherMinimap.dll + RancherMinimap.pdb to $gameModsDir"


