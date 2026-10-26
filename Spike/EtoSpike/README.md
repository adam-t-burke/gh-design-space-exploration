# Eto spike

Standalone Grasshopper component. It is not included in `AllProjects.sln`.

Double-clicking the component calls `GH_ComponentAttributes.RespondToMouseDoubleClick` and opens an `Eto.Forms.Form` whose `Owner` is `Rhino.UI.RhinoEtoApp.MainWindow`.

## What was verified on Windows

Commands, from the repo root, with the .NET SDK 9.0.306:

```text
dotnet build Spike\EtoSpike\EtoSpike.csproj -p:DseRhinoVersion=8.0.23304.9001
dotnet build Spike\EtoSpike\EtoSpike.csproj -p:DseRhinoVersion=8.19.25132.1001 -f net7.0
```

Both succeeded with 0 warnings and 0 errors.

- `8.0.23304.9001` is the package the shipping projects already used. It contains only `lib/net48`. The `net7.0` build still compiled `GH_ComponentAttributes.RespondToMouseDoubleClick` (`GH_Canvas`, `GH_CanvasMouseEvent`) and `Eto.Forms.Form` by NuGet asset fallback onto those net48 reference assemblies. `NU1701` is suppressed in `Directory.Build.props`. Output: `bin\Debug\net48\EtoSpike.gha` and `bin\Debug\net7.0\EtoSpike.gha`.
- `8.19.25132.1001` is the first stable Grasshopper/RhinoCommon 8.x package that contains `lib/net7.0`. The same component compiled against that real net7.0 reference assembly.

The component was not loaded in Rhino. This session did not start Rhino 8, so double-click behavior and parenting to the main window were not exercised.

## What still needs a Mac

- Load `EtoSpike.gha` in Rhino 8 for Mac and confirm the form opens on double-click, owned by `RhinoEtoApp.MainWindow`.
- Shipping projects still reference `8.0.23304.9001` (net48 assets only). A Mac load of the other plugins should use a Grasshopper package that ships `lib/net7.0` (8.19 or later) if the net48 fallback assembly does not load.
- `NLoptNet` 1.4.3 has native runtimes for win-x64, win-x86, and Linux only. There is no osx-arm64 or osx-x64 binary. Radical and Stepper cannot run NLopt on Mac until that library is built and shipped.
