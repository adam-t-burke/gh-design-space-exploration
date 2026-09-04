<#
.SYNOPSIS
  Builds AllProjects.sln and stages a Windows Yak package for Design Space Exploration.

.DESCRIPTION
  Restores NuGet packages, compiles Release, copies Grasshopper .gha files plus
  required dependency DLLs (including native NLopt) into a flat staging folder,
  then runs `yak build --platform win`.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipYak
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Solution = Join-Path $RepoRoot "AllProjects.sln"
$ManifestSource = Join-Path $RepoRoot "yak\manifest.yml"
$ArtifactsRoot = Join-Path $RepoRoot "artifacts"
$StageDir = Join-Path $ArtifactsRoot "yak"

$ExpectedGha = @(
    "Capture",
    "Cluster",
    "Contort",
    "DesignLogger",
    "Diversity",
    "Effects",
    "Reader",
    "Sampler",
    "Sift",
    "Tilde",
    "Writer",
    "MOO",
    "Gradient_MOO",
    "Radical",
    "DSOpt",
    "Stepper"
)

$PluginProjectDirs = @(
    "Capture",
    "Cluster",
    "Contort",
    "DesignLogger",
    "Diversity",
    "Effects",
    "Reader",
    "Sampler",
    "Sift",
    "Tilde",
    "Writer",
    "MOO",
    "Gradient_MOO",
    "Radical",
    "DSOpt",
    "StepperAux"
)

$SdkAssemblyNames = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase
)
@(
    "RhinoCommon",
    "Grasshopper",
    "GH_IO",
    "Rhino.UI",
    "Ed.Eto"
) | ForEach-Object { [void]$SdkAssemblyNames.Add($_) }

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if ($found -and (Test-Path $found)) {
            return $found
        }
    }

    $fallback = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if ($fallback) { return $fallback }
    throw "MSBuild.exe not found. Install Visual Studio Build Tools or set PATH."
}

function Find-NuGet {
    $candidates = @(
        (Join-Path $ArtifactsRoot "nuget.exe"),
        (Join-Path $env:TEMP "nuget.exe"),
        "${env:ProgramFiles(x86)}\NuGet\nuget.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.NuGet -property installationPath |
            Select-Object -First 1
        if ($installPath) {
            $vsNuget = Join-Path $installPath "Common7\IDE\CommonExtensions\Microsoft\NuGet\NuGet.exe"
            if (Test-Path $vsNuget) { return $vsNuget }
        }
    }

    if (-not (Test-Path $ArtifactsRoot)) {
        New-Item -ItemType Directory -Path $ArtifactsRoot | Out-Null
    }
    $download = Join-Path $ArtifactsRoot "nuget.exe"
    Write-Host "Downloading nuget.exe..."
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile $download
    return $download
}

function Clear-SdkRestoreArtifacts {
    Get-ChildItem -LiteralPath $RepoRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "obj" } |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -File -Force -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.Name -in @("project.assets.json", "project.nuget.cache") -or
                    $_.Name -like "*.nuget.g.props" -or
                    $_.Name -like "*.nuget.g.targets"
                } |
                ForEach-Object {
                    Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
                }
        }
}

function Find-Yak {
    if ($env:YAK_EXE -and (Test-Path $env:YAK_EXE)) {
        return $env:YAK_EXE
    }

    $candidates = @(
        "${env:ProgramFiles}\Rhino 8\System\Yak.exe",
        "${env:ProgramFiles(x86)}\Rhino 8\System\Yak.exe",
        "${env:ProgramFiles}\Rhino 7\System\Yak.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    $rhinoRoots = Get-ChildItem "${env:ProgramFiles}\Rhino *" -Directory -ErrorAction SilentlyContinue
    foreach ($root in $rhinoRoots) {
        $yak = Join-Path $root.FullName "System\Yak.exe"
        if (Test-Path $yak) { return $yak }
    }

    return $null
}

function Test-ExcludedAssembly([string]$Path) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    return $SdkAssemblyNames.Contains($name)
}

function Copy-StagedFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$DestinationDir,
        [switch]$PreferExisting
    )

    if (-not (Test-Path $Source)) { return }

    $dest = Join-Path $DestinationDir (Split-Path $Source -Leaf)
    if ((Test-Path $dest) -and $PreferExisting) { return }

    Copy-Item -LiteralPath $Source -Destination $dest -Force
}

function Get-CompanionFiles([string]$Directory) {
    $files = @()
    $files += Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in ".dll", ".gha", ".config" -or
            $_.Name -match '^nlopt' -or
            $_.Name -match 'libnlopt'
        }

    Get-ChildItem -LiteralPath $Directory -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("x64", "x86", "runtimes") } |
        ForEach-Object {
            $files += Get-ChildItem -LiteralPath $_.FullName -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Extension -in ".dll", ".so", ".dylib" }
        }

    return $files
}

if (-not (Test-Path $ManifestSource)) {
    throw "Missing package manifest: $ManifestSource"
}

$ShipTargets = @(
    "DSECommon",
    "JMetalCSharp",
    "StructureEngineCS",
    "Capture",
    "Cluster",
    "Contort",
    "DesignLogger",
    "Diversity",
    "Effects",
    "Reader",
    "Sampler",
    "Sift",
    "Tilde",
    "Writer",
    "MOO",
    "Gradient_MOO",
    "Radical",
    "DSOpt",
    "Stepper"
)

if (-not $SkipBuild) {
    $msbuild = Find-MSBuild
    Write-Host "Using MSBuild: $msbuild"

    Write-Host "Clearing SDK-style restore leftovers from packages.config projects..."
    Clear-SdkRestoreArtifacts

    $nuget = Find-NuGet
    Write-Host "Using NuGet: $nuget"
    Write-Host "Restoring NuGet packages..."
    & $nuget restore $Solution -NonInteractive
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet restore failed with exit code $LASTEXITCODE"
    }

    $targetList = $ShipTargets -join ";"
    Write-Host "Building shipped projects ($Configuration | Any CPU)..."
    & $msbuild $Solution -t:$targetList -p:Configuration=$Configuration -p:Platform="Any CPU" -verbosity:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir | Out-Null
Copy-Item -LiteralPath $ManifestSource -Destination (Join-Path $StageDir "manifest.yml")

$searchRoots = @()
$outputDir = Join-Path $RepoRoot "Output"
if (Test-Path $outputDir) { $searchRoots += $outputDir }
foreach ($dir in $PluginProjectDirs) {
    $projectDir = Join-Path $RepoRoot $dir
    if (Test-Path $projectDir) { $searchRoots += $projectDir }
}

$ghaFiles = foreach ($root in $searchRoots) {
    Get-ChildItem -LiteralPath $root -Filter "*.gha" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $ExpectedGha -contains $_.BaseName }
}

$ghaByName = @{}
foreach ($gha in $ghaFiles) {
    $name = $gha.BaseName
    if (-not $ghaByName.ContainsKey($name)) {
        $ghaByName[$name] = $gha
    }
}

$missing = $ExpectedGha | Where-Object { -not $ghaByName.ContainsKey($_) }
if ($missing) {
    throw "Missing .gha after build: $($missing -join ', ')"
}

foreach ($name in $ExpectedGha) {
    Copy-StagedFile -Source $ghaByName[$name].FullName -DestinationDir $StageDir
    Write-Host "Staged plugin: $name.gha"
}

# Copy companion DLLs from plugin output folders. Process WPF/optimizer projects last
# so their Eto.Forms assemblies win over Rhino-copied Eto.dll.
$companionDirs = @(
    (Join-Path $RepoRoot "DSECommon"),
    (Join-Path $RepoRoot "JMetalCSharp.V05\JMetalCSharp"),
    (Join-Path $RepoRoot "StructureEngineCS\StructureEngineCS")
) + ($PluginProjectDirs | ForEach-Object { Join-Path $RepoRoot $_ }) + @($outputDir)

$wpfLast = @("Radical", "DSOpt", "StepperAux")
$companionDirs = $companionDirs | Sort-Object {
    $leaf = Split-Path $_ -Leaf
    if ($wpfLast -contains $leaf) { 1 } else { 0 }
}

foreach ($dir in $companionDirs) {
    if (-not (Test-Path $dir)) { continue }
    $binDirs = @(Get-ChildItem -LiteralPath $dir -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("bin", "Release", "Debug", "x64", "x86") })
    $scan = @($dir) + @($binDirs | ForEach-Object { $_.FullName })

    foreach ($scanDir in $scan) {
        if (-not (Test-Path $scanDir)) { continue }
        foreach ($file in @(Get-CompanionFiles $scanDir)) {
            if ($null -eq $file) { continue }
            if (Test-ExcludedAssembly $file.FullName) { continue }
            if ($file.Extension -eq ".gha") { continue }
            if ($file.Extension -eq ".xml") { continue }

            $fileName = $file.Name
            if ($fileName -match '\.(pdb|xml)$') { continue }
            if ($fileName -in @("app.config", "packages.config", "ConsoleTest.exe.config", "MyProject1.dll")) { continue }

            $baseName = $file.BaseName
            if ($file.Extension -eq ".config" -and $baseName.EndsWith(".dll")) {
                $baseName = $baseName.Substring(0, $baseName.Length - 4)
            }
            if ($ExpectedGha -contains $baseName) { continue }

            # Keep Rhino-provided Eto out of the package unless the Eto.Wpf companion is present.
            if ($file.BaseName -eq "Eto") {
                $etoWpf = Join-Path $file.DirectoryName "Eto.Wpf.dll"
                if (-not (Test-Path $etoWpf)) { continue }
            }

            Copy-StagedFile -Source $file.FullName -DestinationDir $StageDir
        }
    }
}

$nloptPackage = Get-ChildItem (Join-Path $RepoRoot "packages") -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "NLoptNet.*" } |
    Select-Object -First 1
if ($nloptPackage) {
    $winX64 = Join-Path $nloptPackage.FullName "runtimes\win-x64\native\nlopt.dll"
    $winX86 = Join-Path $nloptPackage.FullName "runtimes\win-x86\native\nlopt.dll"
    if (Test-Path $winX64) {
        Copy-Item -LiteralPath $winX64 -Destination (Join-Path $StageDir "nlopt.dll") -Force
        Copy-Item -LiteralPath $winX64 -Destination (Join-Path $StageDir "nlopt_x64.dll") -Force
        Copy-Item -LiteralPath $winX64 -Destination (Join-Path $StageDir "libnlopt-0.dll") -Force
    }
    if (Test-Path $winX86) {
        Copy-Item -LiteralPath $winX86 -Destination (Join-Path $StageDir "nlopt_x32.dll") -Force
    }
}

$stagedGha = @(Get-ChildItem -LiteralPath $StageDir -Filter "*.gha")
$stagedDll = @(Get-ChildItem -LiteralPath $StageDir -Filter "*.dll")
Write-Host "Staged $($stagedGha.Count) .gha and $($stagedDll.Count) .dll files into $StageDir"

$requiredDlls = @("DSECommon.dll", "JMetalCSharp.dll")
$missingDlls = $requiredDlls | Where-Object { -not (Test-Path (Join-Path $StageDir $_)) }
if ($missingDlls) {
    throw "Staged package is missing required libraries: $($missingDlls -join ', ')"
}

if ($SkipYak) {
    Write-Host "Skipping yak build (-SkipYak)."
    return
}

$yak = Find-Yak
if (-not $yak) {
    throw "Yak.exe not found. Install Rhino 8 or set YAK_EXE to the Yak CLI path."
}

Write-Host "Using Yak: $yak"
Push-Location $StageDir
try {
    & $yak build --platform win
    if ($LASTEXITCODE -ne 0) {
        throw "yak build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$yakFile = Get-ChildItem -LiteralPath $StageDir -Filter "*.yak" | Select-Object -First 1
if (-not $yakFile) {
    throw "yak build completed but no .yak file was produced in $StageDir"
}

$destYak = Join-Path $ArtifactsRoot $yakFile.Name
Move-Item -LiteralPath $yakFile.FullName -Destination $destYak -Force
Write-Host "Package: $destYak"
