using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DSOptimization
{
    /// <summary>
    /// Loads NLoptNet's native library (DllImport name "nlopt") from the runtimes folders
    /// copied next to this assembly. See native/README.md.
    /// </summary>
    public static class NLoptNative
    {
        private static int _registered;

        public static void Register()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) == 1)
                return;

            Assembly nlopt = typeof(NLoptNet.NLoptSolver).Assembly;
#if NETFRAMEWORK
            PreloadWindows(nlopt);
#else
            NativeLibrary.SetDllImportResolver(nlopt, Resolve);
#endif
        }

#if NETFRAMEWORK
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        private static void PreloadWindows(Assembly nlopt)
        {
            foreach (string path in CandidateFiles(nlopt))
            {
                if (File.Exists(path))
                {
                    LoadLibrary(path);
                    return;
                }
            }
        }
#else
        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName == null || libraryName.IndexOf("nlopt", StringComparison.OrdinalIgnoreCase) < 0)
                return IntPtr.Zero;

            foreach (string path in CandidateFiles(assembly))
            {
                if (File.Exists(path))
                    return NativeLibrary.Load(path);
            }
            return IntPtr.Zero;
        }
#endif

        private static IEnumerable<string> CandidateFiles(Assembly assembly)
        {
            var roots = new List<string>();
            AddRoot(roots, assembly);
            AddRoot(roots, typeof(NLoptNative).Assembly);

            string[] names = NativeFileNames();
            foreach (string root in roots)
            {
                foreach (string rid in RidSearchOrder())
                {
                    foreach (string name in names)
                        yield return Path.Combine(root, "runtimes", rid, "native", name);
                }
                foreach (string name in names)
                    yield return Path.Combine(root, name);
            }
        }

        private static void AddRoot(List<string> roots, Assembly assembly)
        {
            if (assembly == null)
                return;
            string location = assembly.Location;
            if (string.IsNullOrEmpty(location))
                return;
            string dir = Path.GetDirectoryName(location);
            if (!string.IsNullOrEmpty(dir) && !roots.Contains(dir))
                roots.Add(dir);
        }

        private static IEnumerable<string> RidSearchOrder()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string preferred = PreferredRid();
            if (seen.Add(preferred))
                yield return preferred;
            if (preferred.StartsWith("osx", StringComparison.OrdinalIgnoreCase))
            {
                if (seen.Add("osx-arm64")) yield return "osx-arm64";
                if (seen.Add("osx-x64")) yield return "osx-x64";
            }
            else if (preferred.StartsWith("win", StringComparison.OrdinalIgnoreCase))
            {
                if (seen.Add("win-x64")) yield return "win-x64";
                if (seen.Add("win-x86")) yield return "win-x86";
            }
            else
            {
                if (seen.Add("linux-x64")) yield return "linux-x64";
                if (seen.Add("linux-arm64")) yield return "linux-arm64";
            }
        }

        private static string PreferredRid()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
            return Environment.Is64BitProcess ? "win-x64" : "win-x86";
        }

        private static string[] NativeFileNames()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new[] { "libnlopt.dylib", "libnlopt.0.dylib" };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new[] { "libnlopt.so", "nlopt.so" };
            return new[] { "nlopt.dll", "nlopt_x64.dll", "nlopt_x32.dll", "libnlopt-0.dll" };
        }
    }
}
