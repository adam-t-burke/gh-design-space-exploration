<#
.SYNOPSIS
  Builds AllProjects.sln and stages a Yak package for Design Space Exploration.

.DESCRIPTION
  Default: restores NuGet packages, compiles with MSBuild, copies Grasshopper
  .gha files plus dependency DLLs (including flat Windows NLopt aliases) into a
  staging folder, then runs `yak build --platform win`. That path writes
  artifacts/yak and does not publish.

  -MultiTarget: `dotnet build` (every target framework), stages net48/ and net7.0/
  under artifacts/yak-any, then runs `yak build --platform any`. Does not delete
  or overwrite artifacts/yak.

  A plugin is required in a TFM folder only when its csproj lists that TFM.
  Managed optimization assemblies and the NLopt runtimes/ tree are copied from
  each plugin TFM output, beside the .gha the build already emitted. They are
  not taken from the NuGet cache. A plugin whose output does not contain
  DSE.Optimization.Core, DSE.Optimization.UI, NLoptNet, MathNet.Numerics, or
  System.Resources.Extensions does not contribute those files. JClement.NLoptNet
  supplies the osx-arm64 and osx-x64 libnlopt.dylib files and the Windows
  nlopt.dll files under that runtimes tree. Eto, RhinoCommon, and Grasshopper
  are not copied into the package.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipYak,
    [switch]$MultiTarget
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
    if ($SdkAssemblyNames.Contains($name)) { return $true }
    # Rhino ships Eto. Do not vendor Eto.dll or Eto.* into the Yak package.
    if ($name -eq "Eto" -or $name.StartsWith("Eto.")) { return $true }
    return $false
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

function Get-CsprojText([string]$CsprojPath) {
    $text = Get-Content -LiteralPath $CsprojPath -Raw
    return [regex]::Replace($text, '(?s)<!--.*?-->', '')
}

function Get-ProjectTfms([string]$CsprojPath) {
    $text = Get-CsprojText $CsprojPath
    $tfms = New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($text, '<TargetFrameworks>\s*([^<]+?)\s*</TargetFrameworks>')) {
        foreach ($part in $match.Groups[1].Value.Split(';')) {
            $token = $part.Trim()
            if ($token) { $tfms.Add($token) }
        }
    }
    foreach ($match in [regex]::Matches($text, '<TargetFramework>\s*([^<]+?)\s*</TargetFramework>')) {
        $token = $match.Groups[1].Value.Trim()
        if ($token) { $tfms.Add($token) }
    }
    return @($tfms | Select-Object -Unique)
}

function Get-SolutionProjects([string]$SolutionPath) {
    $projects = New-Object System.Collections.Generic.List[string]
    foreach ($line in Get-Content -LiteralPath $SolutionPath) {
        if ($line -match 'Project\("\{[0-9A-Fa-f-]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"') {
            $projects.Add((Join-Path $RepoRoot $Matches[1]))
        }
    }
    return @($projects)
}

function Test-ExeProject([string]$CsprojPath) {
    $text = Get-CsprojText $CsprojPath
    return $text -match '<OutputType>\s*(Exe|WinExe)\s*</OutputType>'
}

function Test-GrasshopperProject([string]$CsprojPath) {
    $text = Get-CsprojText $CsprojPath
    return $text -match '<DseGrasshopperPlugin>\s*true\s*</DseGrasshopperPlugin>'
}

function Get-PluginOutputName([string]$CsprojPath) {
    $text = Get-CsprojText $CsprojPath
    if ($text -match '<TargetName>\s*([^<]+?)\s*</TargetName>') {
        return $Matches[1].Trim()
    }
    if ($text -match '<AssemblyName>\s*([^<]+?)\s*</AssemblyName>') {
        return $Matches[1].Trim()
    }
    return [System.IO.Path]::GetFileNameWithoutExtension($CsprojPath)
}

function Get-ProjectOutputDir([string]$ProjectPath, [string]$Tfm) {
    $projectDir = Split-Path -Parent $ProjectPath
    $configs = @($Configuration)
    if ($Configuration -eq "Debug") {
        $configs = @("Debug", "Debug64", "Debug32")
    }

    $relativeBins = @()
    foreach ($cfg in $configs) {
        $relativeBins += (Join-Path "bin" (Join-Path $cfg $Tfm))
        # Solution configs such as Tilde's Debug|Any CPU map to Debug|x86, so the
        # SDK writes bin\x86\Debug\<tfm> instead of bin\Debug\<tfm>.
        foreach ($platform in @("x64", "x86")) {
            $relativeBins += (Join-Path "bin" (Join-Path $platform (Join-Path $cfg $Tfm)))
        }
    }

    foreach ($relative in $relativeBins) {
        $dir = Join-Path $projectDir $relative
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $payload = @(Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in ".gha", ".dll" })
        if ($payload.Count -gt 0) { return $dir }
    }

    if ($Tfm -eq "net48") {
        foreach ($cfg in $configs) {
            $flat = Join-Path $projectDir (Join-Path "bin" $cfg)
            if (-not (Test-Path -LiteralPath $flat)) { continue }
            $gha = @(Get-ChildItem -LiteralPath $flat -File -Filter "*.gha" -ErrorAction SilentlyContinue)
            if ($gha.Count -gt 0) { return $flat }
        }
    }

    return $null
}

function Copy-NativeSubtrees([string]$SourceDir, [string]$DestinationDir) {
    $sourceRoot = [System.IO.Path]::GetFullPath($SourceDir).TrimEnd('\', '/')
    foreach ($name in @("runtimes", "x64", "x86")) {
        $src = Join-Path $SourceDir $name
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $files = @(Get-ChildItem -LiteralPath $src -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in ".dll", ".so", ".dylib" })
        foreach ($file in $files) {
            $full = [System.IO.Path]::GetFullPath($file.FullName)
            if (-not $full.StartsWith($sourceRoot, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
            $relative = $full.Substring($sourceRoot.Length).TrimStart('\', '/')
            $dest = Join-Path $DestinationDir $relative
            $parent = Split-Path -Parent $dest
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Path $parent | Out-Null
            }
            Copy-Item -LiteralPath $full -Destination $dest -Force
        }
    }
}

$OptimizationManagedDlls = @(
    "DSE.Optimization.Core.dll",
    "DSE.Optimization.UI.dll",
    "NLoptNet.dll",
    "MathNet.Numerics.dll",
    "System.Resources.Extensions.dll"
)

function Copy-OptimizationAssembliesIfPresent([string]$SourceDir, [string]$DestinationDir) {
    foreach ($name in $OptimizationManagedDlls) {
        $source = Join-Path $SourceDir $name
        if (-not (Test-Path -LiteralPath $source)) { continue }
        Copy-StagedFile -Source $source -DestinationDir $DestinationDir
    }
}

function Copy-OutputPayload([string]$SourceDir, [string]$DestinationDir) {
    $files = @(Get-CompanionFiles $SourceDir)
    foreach ($file in $files) {
        if ($null -eq $file) { continue }
        if (Test-ExcludedAssembly $file.FullName) { continue }
        if ($file.Extension -eq ".xml") { continue }
        if ($file.Name -match '\.(pdb|xml|deps\.json|runtimeconfig\.json)$') { continue }
        if ($file.Name -in @("app.config", "packages.config", "ConsoleTest.exe.config", "MyProject1.dll")) { continue }

        if ($file.Extension -eq ".dll") {
            $ghaSibling = Join-Path $file.DirectoryName ($file.BaseName + ".gha")
            if (Test-Path -LiteralPath $ghaSibling) { continue }
        }

        # Keep native libraries under runtimes/, x64/, or x86/ instead of flattening them.
        $sourceRoot = [System.IO.Path]::GetFullPath($SourceDir).TrimEnd('\', '/')
        $full = [System.IO.Path]::GetFullPath($file.FullName)
        if ($full.StartsWith($sourceRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $full.Substring($sourceRoot.Length).TrimStart('\', '/')
            $top = ($relative -split '[\\/]', 2)[0]
            if ($top -in @("runtimes", "x64", "x86")) { continue }
        }

        Copy-StagedFile -Source $file.FullName -DestinationDir $DestinationDir
    }

    Copy-OptimizationAssembliesIfPresent -SourceDir $SourceDir -DestinationDir $DestinationDir
    Copy-NativeSubtrees -SourceDir $SourceDir -DestinationDir $DestinationDir
}

function Find-PackageDir([string]$PackageId) {
    $escaped = [regex]::Escape($PackageId)
    $namePattern = "^$escaped\.\d"

    $repoPackages = Join-Path $RepoRoot "packages"
    if (Test-Path -LiteralPath $repoPackages) {
        $hit = @(Get-ChildItem -LiteralPath $repoPackages -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match $namePattern } |
            Sort-Object Name -Descending)
        if ($hit.Count -gt 0) { return $hit[0].FullName }
    }

    $nugetRoot = $env:NUGET_PACKAGES
    if (-not $nugetRoot) {
        $nugetRoot = Join-Path $env:USERPROFILE ".nuget\packages"
    }
    $idDir = Join-Path $nugetRoot $PackageId.ToLowerInvariant()
    if (Test-Path -LiteralPath $idDir) {
        $hit = @(Get-ChildItem -LiteralPath $idDir -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending)
        if ($hit.Count -gt 0) { return $hit[0].FullName }
    }

    return $null
}

function Get-BestLibDirectory([string]$PackageDir, [string]$Tfm) {
    $prefs = if ($Tfm -eq "net7.0") {
        @("net7.0", "net6.0", "net5.0", "netcoreapp3.1", "netstandard2.1", "netstandard2.0", "net48", "net462", "net47")
    }
    else {
        @("net48", "net472", "net471", "net47", "net462", "net461", "net46", "net45", "netstandard2.0", "netstandard2.1")
    }

    foreach ($pref in $prefs) {
        $lib = Join-Path $PackageDir (Join-Path "lib" $pref)
        if (Test-Path -LiteralPath $lib) { return $lib }
    }
    return $null
}

function Copy-PackageLibDlls([string]$PackageId, [string]$Tfm, [string]$DestinationDir) {
    $packageDir = Find-PackageDir $PackageId
    if (-not $packageDir) {
        Write-Warning "Satellite package not found (skipped): $PackageId"
        return
    }

    $lib = Get-BestLibDirectory $packageDir $Tfm
    if (-not $lib) {
        Write-Warning "No compatible lib folder for $PackageId ($Tfm)"
        return
    }

    $files = @(Get-ChildItem -LiteralPath $lib -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq ".dll" -or $_.Name -like "*.dll.config" })
    foreach ($file in $files) {
        if (Test-ExcludedAssembly $file.FullName) { continue }
        Copy-StagedFile -Source $file.FullName -DestinationDir $DestinationDir -PreferExisting
    }
}

function Publish-MultiTargetPackage {
    $stageRoot = Join-Path $ArtifactsRoot "yak-any"
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    Copy-Item -LiteralPath $ManifestSource -Destination (Join-Path $stageRoot "manifest.yml")
    $iconSource = Join-Path $RepoRoot "yak\dselogowhitesquare.png"
    if (-not (Test-Path -LiteralPath $iconSource)) {
        throw "Missing package icon: $iconSource"
    }
    Copy-Item -LiteralPath $iconSource -Destination (Join-Path $stageRoot "dselogowhitesquare.png")

    $projects = @(Get-SolutionProjects $Solution)
    if ($projects.Count -eq 0) {
        throw "No projects found in $Solution"
    }

    # These are not always copied next to every plugin by the SDK build.
    # Optimization assemblies, including MathNet and System.Resources.Extensions,
    # are copied only when a plugin TFM output already contains them.
    $alwaysSatellites = @(
        "Accord",
        "Accord.MachineLearning",
        "Accord.Math",
        "Accord.Statistics",
        "log4net",
        "alglib.net"
    )

    foreach ($tfm in @("net48", "net7.0")) {
        $tfmDir = Join-Path $stageRoot $tfm
        $stagedGha = New-Object System.Collections.Generic.List[string]

        foreach ($project in $projects) {
            if (-not (Test-Path -LiteralPath $project)) { continue }
            $tfms = @(Get-ProjectTfms $project)
            if ($tfms -notcontains $tfm) { continue }
            if (Test-ExeProject $project) { continue }

            $outputDir = Get-ProjectOutputDir $project $tfm
            $isPlugin = Test-GrasshopperProject $project
            if ($isPlugin) {
                $pluginName = Get-PluginOutputName $project
                if (-not $outputDir) {
                    throw "Missing $tfm output for $pluginName ($project). Build $Configuration before packing."
                }
                $gha = Join-Path $outputDir ($pluginName + ".gha")
                if (-not (Test-Path -LiteralPath $gha)) {
                    throw "Missing $pluginName.gha in $outputDir"
                }
            }
            elseif (-not $outputDir) {
                continue
            }

            if (-not (Test-Path -LiteralPath $tfmDir)) {
                New-Item -ItemType Directory -Path $tfmDir | Out-Null
            }
            Copy-OutputPayload -SourceDir $outputDir -DestinationDir $tfmDir
            if ($isPlugin) {
                $stagedGha.Add((Get-PluginOutputName $project))
                Write-Host "Staged ${tfm}: $(Get-PluginOutputName $project).gha"
            }
        }

        if ($stagedGha.Count -eq 0) {
            Write-Warning "No plugins staged for $tfm."
            continue
        }

        foreach ($packageId in $alwaysSatellites) {
            Copy-PackageLibDlls -PackageId $packageId -Tfm $tfm -DestinationDir $tfmDir
        }

        $optimizationPlugins = @($stagedGha | Where-Object { $_ -in @("Radical", "DSOpt", "Stepper") })
        if ($optimizationPlugins.Count -gt 0) {
            $requiredAssets = @(
                "DSE.Optimization.Core.dll",
                "DSE.Optimization.UI.dll",
                "NLoptNet.dll",
                "MathNet.Numerics.dll",
                "System.Resources.Extensions.dll",
                "runtimes\win-x64\native\nlopt.dll",
                "runtimes\win-x86\native\nlopt.dll",
                "runtimes\osx-arm64\native\libnlopt.dylib",
                "runtimes\osx-x64\native\libnlopt.dylib"
            )
            $missingAssets = @($requiredAssets | Where-Object {
                -not (Test-Path -LiteralPath (Join-Path $tfmDir $_))
            })
            if ($missingAssets) {
                throw "Staged $tfm package is missing optimization files from plugin output: $($missingAssets -join ', '). Build Radical, DSOpt, and Stepper before packing. The pack script does not fetch these from the NuGet cache."
            }
            Write-Host "Staged ${tfm} optimization assemblies and runtimes/ from plugin output."
        }

        $missingDlls = @("DSECommon.dll", "JMetalCSharp.dll") | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $tfmDir $_))
        }
        if ($missingDlls) {
            throw "Staged $tfm package is missing required libraries: $($missingDlls -join ', ')"
        }
    }

    $tfmCount = @(Get-ChildItem -LiteralPath $stageRoot -Directory | Where-Object { $_.Name -in @("net48", "net7.0") }).Count
    if ($tfmCount -eq 0) {
        throw "Multi-target stage is empty: $stageRoot"
    }

    if ($SkipYak) {
        Write-Host "Skipping yak build (-SkipYak). Stage: $stageRoot"
        return
    }

    $yak = Find-Yak
    if (-not $yak) {
        throw "Yak.exe not found. Install Rhino 8 or set YAK_EXE to the Yak CLI path."
    }

    Write-Host "Using Yak: $yak"
    Push-Location $stageRoot
    try {
        & $yak build --platform any
        if ($LASTEXITCODE -ne 0) {
            throw "yak build failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $yakFile = Get-ChildItem -LiteralPath $stageRoot -Filter "*.yak" | Select-Object -First 1
    if (-not $yakFile) {
        throw "yak build completed but no .yak file was produced in $stageRoot"
    }

    $destYak = Join-Path $ArtifactsRoot $yakFile.Name
    Move-Item -LiteralPath $yakFile.FullName -Destination $destYak -Force
    Write-Host "Package: $destYak"
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
    "Radical",
    "DSOpt",
    "Stepper"
)

if (-not $SkipBuild) {
    if ($MultiTarget) {
        Write-Host "Building with dotnet ($Configuration, all target frameworks)..."
        & dotnet build $Solution -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE"
        }
    }
    else {
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
}

if ($MultiTarget) {
    Publish-MultiTargetPackage
    return
}

if (Test-Path $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StageDir | Out-Null
Copy-Item -LiteralPath $ManifestSource -Destination (Join-Path $StageDir "manifest.yml")
$iconSource = Join-Path $RepoRoot "yak\dselogowhitesquare.png"
if (-not (Test-Path $iconSource)) {
    throw "Missing package icon: $iconSource"
}
Copy-Item -LiteralPath $iconSource -Destination (Join-Path $StageDir "dselogowhitesquare.png")

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
