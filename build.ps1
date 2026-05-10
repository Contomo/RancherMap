param(
    [switch] $Diagnostics
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $projectRoot "src"
$distDir = Join-Path $projectRoot "dist"
$objDir = Join-Path $projectRoot "obj"
$gameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Slime Rancher 2"
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $projectRoot)
$melonNet35 = Join-Path $gameRoot "MelonLoader\net35\MelonLoader.dll"
$melonNet6 = Join-Path $gameRoot "MelonLoader\net6"
$harmony = Join-Path $melonNet6 "0Harmony.dll"
$il2cppDir = Join-Path $gameRoot "MelonLoader\Il2CppAssemblies"
$runtimeDir = "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.36"
$roslyn = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"

if (!(Test-Path $roslyn)) {
    throw "Roslyn compiler not found at $roslyn"
}

if (!(Test-Path $melonNet35)) {
    throw "MelonLoader net35 assembly not found at $melonNet35"
}

if (!(Test-Path $harmony)) {
    throw "Harmony assembly not found at $harmony"
}

if (!(Test-Path $runtimeDir)) {
    throw "Expected .NET 6 runtime not found at $runtimeDir"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

$runtimeRefs = Get-ChildItem $runtimeDir -Filter *.dll | Where-Object {
    $_.Name -match '^(System|Microsoft\.CSharp|mscorlib|netstandard|WindowsBase)'
} | Where-Object {
    try {
        [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
        $true
    } catch {
        $false
    }
}

$il2cppRefs = Get-ChildItem $il2cppDir -Filter *.dll | Where-Object {
    try {
        [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
        $true
    } catch {
        $false
    }
}

$references = @(
    $runtimeRefs.FullName
    $melonNet35
    $harmony
    (Join-Path $melonNet6 "Il2CppInterop.Runtime.dll")
    (Join-Path $melonNet6 "Il2CppInterop.Common.dll")
    $il2cppRefs.FullName
)

$sources = Get-ChildItem $srcDir -Filter *.cs -Recurse | Select-Object -ExpandProperty FullName
$responsePath = Join-Path $objDir "build.rsp"
$outputPath = Join-Path $distDir "RancherMinimap.dll"
$pdbPath = Join-Path $distDir "RancherMinimap.pdb"

if (Test-Path $outputPath) {
    Remove-Item $outputPath -Force
}

if (Test-Path $pdbPath) {
    Remove-Item $pdbPath -Force
}

$defines = @("TRACE")
if ($Diagnostics) {
    $defines += "DEBUG"
    $defines += "RMM_DIAGNOSTICS"
}

$optimize = if ($Diagnostics) { "/optimize-" } else { "/optimize+" }

$rspLines = @(
    "/nologo"
    "/target:library"
    "/nostdlib+"
    "/langversion:latest"
    "/debug:portable"
    "/define:$($defines -join ';')"
    $optimize
    "/out:`"$outputPath`""
)

$rspLines += $references | ForEach-Object { "/reference:`"$_`"" }

$optionsIconPath = Join-Path $projectRoot "assets\iconCategoryWorld.rgba"
if (!(Test-Path $optionsIconPath)) {
    throw "Required embedded options icon asset not found at $optionsIconPath"
}
$rspLines += "/resource:`"$optionsIconPath`",rancher_minimap.assets.iconCategoryWorld.rgba"

$starlightIconPath = Join-Path $projectRoot "assets\iconCategoryWorld.png"
if (!(Test-Path $starlightIconPath)) {
    throw "Required embedded Starlight icon asset not found at $starlightIconPath"
}
$rspLines += "/resource:`"$starlightIconPath`",RancherMinimap.icon.png"

$rspLines += $sources | ForEach-Object { "`"$_`"" }

Set-Content -Path $responsePath -Value $rspLines -Encoding ASCII

& $roslyn "@$responsePath"

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Built $outputPath"
if (Test-Path $pdbPath) {
    Write-Host "Built $pdbPath"
}
