# Cross-platform status

Shipping Grasshopper plugins multi-target `net48` and `net7.0`. Radical, DSOpt, and Stepper (project `StepperAux/Stepper.csproj`) reference `DSE.Optimization.Core` and `DSE.Optimization.UI`. Their windows are Eto and are opened on the Grasshopper UI thread. NLopt comes from JClement.NLoptNet 2.10.1, which includes macOS `libnlopt.dylib` files. This repository does not commit those binaries; the pack script copies them from the plugin build output at pack time.

This session did not launch Rhino on Windows or Mac. The checklist below is unchecked.

## Rhino 8 smoke checklist

Run these on Rhino 8 for Windows and, separately, Rhino 8 for Mac. Do not treat a successful `dotnet build` as a substitute.

- [ ] Every shipping component loads in Grasshopper.
- [ ] The Radical window opens and closes on the main thread.
- [ ] The Stepper window opens and closes on the main thread.
- [ ] The DSOpt window opens and closes on the main thread.
- [ ] An optimization run completes.
- [ ] CSV export works.
- [ ] Existing `.gh` files open. Component GUIDs are unchanged from `yak-packaging`.

Optimization components ship NLopt macOS natives from JClement.NLoptNet. That package restore was checked on Windows. Those natives were not smoke-tested in Rhino for Mac.

## What this session checked

- `dotnet build AllProjects.sln -c Debug` completed with 0 errors.
- `dotnet build AllProjects.sln -c Debug -f net7.0` completed with 0 errors. Every project in `AllProjects.sln` lists a literal `net7.0` TFM, so the macOS CI job can use that command. The Windows job still builds the solution with no `-f`.
- Radical, DSOpt, and `StepperAux/Stepper.csproj` contain `net48;net7.0` and do not set `UseWPF`. Their `.xaml` files are not compiled: each project removes `**\*.cs` and compiles only the Eto entry points, and there is no `Page` or `ApplicationDefinition` item.
- Shipping code outside `temp/`, `structurefit-master/`, `DesignTool/`, `Stepper/` (old duplicate), `StormCloud/`, `StormCloudAnalysis/`, and `packages/` has no `using System.Windows.Forms`. Message boxes in `DSE.Optimization.UI` are `Eto.Forms.MessageBox`. Leftover Radical and DSOpt `.xaml.cs` files still mention `System.Windows.MessageBox`; those files are removed from compile.
- Component and assembly-info GUIDs in shipping `*Info.cs` and `*Component.cs` files match `yak-packaging`. No shipping GUID changed.
- Restored `jclement.nloptnet` 2.10.1 contains `runtimes/win-x64`, `win-x86`, `osx-arm64`, and `osx-x64` natives. The same four files are in `Radical`, `DSOpt`, and `StepperAux` `bin\Debug\net48` and `net7.0`.
- `tools/Build-YakPackage.ps1 -MultiTarget -SkipBuild -SkipYak -Configuration Debug` staged `artifacts/yak-any/net48` and `net7.0` from that output, including the `runtimes/` tree. It did not modify `artifacts/yak`. Yak.exe is installed; `yak build` was not run.

`-MultiTarget` copies, from each plugin TFM output into `artifacts/yak-any/<tfm>/`:

- the `.gha` and the dependency DLLs already beside it
- `DSE.Optimization.Core.dll`, `DSE.Optimization.UI.dll`, `NLoptNet.dll`, `MathNet.Numerics.dll`, and `System.Resources.Extensions.dll` only when that output already contains them
- `runtimes/win-x64/native/nlopt.dll`
- `runtimes/win-x86/native/nlopt.dll`
- `runtimes/osx-arm64/native/libnlopt.dylib`
- `runtimes/osx-x64/native/libnlopt.dylib`

Eto, RhinoCommon, and Grasshopper are not copied. The script does not read the NLoptNet 1.4.3 package for the multi-target layout. The default Windows path, `artifacts/yak` via `yak build --platform win`, is unchanged.

## Legacy folders that remain Windows-only

These trees are not part of the cross-platform Grasshopper package:

- `temp/`
- `structurefit-master/`
- `DesignTool/`
- `Stepper/` (old duplicate; the shipping project is `StepperAux/Stepper.csproj`)
- `StormCloud/`
- `StormCloudAnalysis/`
