# NLopt native libraries

Radical's solver is `NLoptSolver` from the `NLoptNet` assembly.

## Package choice

`NLoptNet` 1.4.3 is API-compatible with this code, but its NuGet package only ships native libraries for `win-x64`, `win-x86`, and Linux. It has no `osx-arm64` or `osx-x64` RID, so a Mac build cannot load a dylib from that package.

`JClement.NLoptNet` 2.10.1 exposes the same public surface (`NLoptSolver`, `NLoptAlgorithm`, `NloptResult`, the same constraint and objective delegates, and the same constructor). It restores on this repo's `net48` and `net7.0` targets, and it ships real natives:

- `runtimes/win-x64/native/nlopt.dll`
- `runtimes/win-x86/native/nlopt.dll`
- `runtimes/osx-arm64/native/libnlopt.dylib`
- `runtimes/osx-x64/native/libnlopt.dylib`
- Linux `nlopt.so` RIDs as well

The Core project references **JClement.NLoptNet 2.10.1** for that reason. No macOS binary is checked into this repository. The Core build copies the package's `nlopt.dll` and `libnlopt.dylib` files into `bin/<Configuration>/<tfm>/runtimes/...`. The Grasshopper projects copy that tree next to the `.gha`.

`DSE.Optimization.Core.NLoptNative` calls `NativeLibrary.SetDllImportResolver` on `net7.0` (the Rhino 8 Mac runtime). The import name inside NLoptNet is `nlopt`. The resolver maps that name to:

- `runtimes/osx-arm64/native/libnlopt.dylib`
- `runtimes/osx-x64/native/libnlopt.dylib`
- `runtimes/win-x64/native/nlopt.dll` and `win-x86` (also `nlopt_x64.dll` / `nlopt_x32.dll`, which is the name NLoptNet 1.4.3's own `.targets` copied on Windows)

On `net48` (Windows only) `NativeLibrary` is not in the framework. The same class preloads `nlopt.dll` with `LoadLibrary` from the `win-x64` or `win-x86` folder before the first P/Invoke, which is the equivalent that keeps the existing Windows natives loading.

## If the packaged dylib is rejected on a Mac

The dylibs above come from the NuGet package. They are not a substitute you should invent or commit as an empty file. If macOS refuses them (quarantine or signature), rebuild NLopt 2.7 or newer on a Mac and replace the two dylibs in the package output (do not commit a fake binary):

```bash
cmake -S nlopt -B build-arm64 -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES=arm64 -DBUILD_SHARED_LIBS=ON \
  -DNLOPT_PYTHON=OFF -DNLOPT_OCTAVE=OFF -DNLOPT_MATLAB=OFF \
  -DNLOPT_GUILE=OFF -DNLOPT_SWIG=OFF
cmake --build build-arm64 --config Release

cmake -S nlopt -B build-x64 -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_ARCHITECTURES=x86_64 -DBUILD_SHARED_LIBS=ON \
  -DNLOPT_PYTHON=OFF -DNLOPT_OCTAVE=OFF -DNLOPT_MATLAB=OFF \
  -DNLOPT_GUILE=OFF -DNLOPT_SWIG=OFF
cmake --build build-x64 --config Release

lipo -create build-arm64/libnlopt*.dylib build-x64/libnlopt*.dylib -output libnlopt.dylib
codesign --force --sign - libnlopt.dylib
```

Copy that signed `libnlopt.dylib` to both `runtimes/osx-arm64/native/` and `runtimes/osx-x64/native/` next to `DSE.Optimization.Core.dll`. The resolver already looks there. NLopt's C API (`nlopt_create`, `nlopt_optimize`, bounds, and constraints) is what NLoptNet P/Invokes.
