param(
    [switch] $WaitForDebugger,
    [switch] $Diagnostics
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher 2"
$modsDir = Join-Path $gameRoot "Mods"

function Copy-BuiltFile {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    if (!(Test-Path $Source)) {
        throw "Missing build output: $Source"
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

Set-Location $projectRoot

Write-Host "Building RancherMinimap..."
$buildArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $projectRoot "build.ps1"))
if ($Diagnostics) {
    $buildArgs += "-Diagnostics"
}
& powershell @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "build.ps1 failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $projectRoot "dist\RancherMinimap.dll"
$pdb = Join-Path $projectRoot "dist\RancherMinimap.pdb"

New-Item -ItemType Directory -Force -Path $modsDir | Out-Null
$installedDll = Join-Path $modsDir "RancherMinimap.dll"
$installedPdb = Join-Path $modsDir "RancherMinimap.pdb"
$oldInstalledDll = Join-Path $modsDir "SR2FreshMinimap.dll"
$oldInstalledPdb = Join-Path $modsDir "SR2FreshMinimap.pdb"
Remove-Item -LiteralPath $installedDll -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installedPdb -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $oldInstalledDll -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $oldInstalledPdb -Force -ErrorAction SilentlyContinue
Copy-BuiltFile $dll $installedDll
Copy-BuiltFile $pdb $installedPdb
Assert-InstalledHashMatches $dll $installedDll

Write-Host "Installed RancherMinimap.dll + PDB."

if ($WaitForDebugger) {
    $env:RANCHERMINIMAP_WAIT_FOR_DEBUGGER = "1"
    Write-Host "Debugger wait is enabled."
}
else {
    Remove-Item Env:\RANCHERMINIMAP_WAIT_FOR_DEBUGGER -ErrorAction SilentlyContinue
}

& (Join-Path $gameRoot "SlimeRancher2.exe")
