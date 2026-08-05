using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace cYo.Projects.ComicRack.Engine.IO.Provider.Native
{
	/// <summary>
	/// Shared native-library resolution for Paperbunkr's five thin P/Invoke shims
	/// (libwebp, libheif, jxl, 7z, pdfium). Written fresh for Paperbunkr -- see
	/// docs/onboarding.md §2/§4 -- rather than porting ComicRackCE's per-shim DllImport
	/// strings, which baked hardcoded relative paths (and, for jxl, the ".dll" extension)
	/// directly into the DllImport attribute. That pattern breaks the moment you're not on
	/// Windows x86/x64 with the exact "Resources\x86\..." folder layout CE shipped.
	///
	/// Registers one <see cref="NativeLibrary.SetDllImportResolver"/> per library name that:
	///  - tries the platform-appropriate file name (libwebp.dll / libwebp.so / libwebp.dylib)
	///  - searches next to the assembly first, then an arch-specific subfolder (x86/x64/arm64)
	///  - falls back to letting the default OS search paths handle it
	///
	/// This is a Windows-first, forward-compatible shape: no non-Windows native binaries are
	/// bundled yet (that's explicitly out of scope for this retarget spike per docs/onboarding.md
	/// §4), but callers don't need to change when that work happens later.
	/// </summary>
	internal static class NativeInterop
	{
		/// <summary>
		/// Resolves an on-disk path for a native asset that needs an explicit LoadLibrary path
		/// rather than a DllImport name (e.g. 7z.dll, loaded dynamically by
		/// cYo.Common.Compression.SevenZip.SevenZipFactory via kernel32 LoadLibrary/GetProcAddress
		/// rather than a static DllImport). Written fresh for Paperbunkr to replace CE's baked
		/// "Resourcesz.dll" / "Resourcesdz.dll" strings built from
		/// Assembly.GetExecutingAssembly().Location -- same arch-subfolder search order as
		/// <see cref="RegisterResolver"/>, just returning a path instead of registering a resolver.
		/// Falls back to the bare file name so LoadLibrary can still find it on the default search
		/// path (e.g. install-time xcopy next to the exe) if no arch subfolder is present.
		/// </summary>
		public static string ResolveNativeAssetPath(Assembly assembly, string fileName)
		{
			string baseDir = Path.GetDirectoryName(assembly.Location) ?? AppContext.BaseDirectory;
			string arch = RuntimeInformation.ProcessArchitecture switch
			{
				Architecture.X64 => "x64",
				Architecture.X86 => "x86",
				Architecture.Arm64 => "arm64",
				_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
			};

			foreach (string candidate in new[]
			{
				Path.Combine(baseDir, arch, fileName),
				Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
				Path.Combine(baseDir, fileName)
			})
			{
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}

			return fileName;
		}

		public static void RegisterResolver(Assembly assembly, string libraryName)
		{
			NativeLibrary.SetDllImportResolver(assembly, (name, requestingAssembly, searchPath) =>
			{
				if (!string.Equals(name, libraryName, StringComparison.OrdinalIgnoreCase))
				{
					return IntPtr.Zero;
				}

				string fileName = MapLibraryName(libraryName);
				string baseDir = Path.GetDirectoryName(requestingAssembly.Location) ?? AppContext.BaseDirectory;
				string arch = RuntimeInformation.ProcessArchitecture switch
				{
					Architecture.X64 => "x64",
					Architecture.X86 => "x86",
					Architecture.Arm64 => "arm64",
					_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()
				};

				foreach (string candidate in new[]
				{
					Path.Combine(baseDir, arch, fileName),
					Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", fileName),
					Path.Combine(baseDir, fileName)
				})
				{
					if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
					{
						return handle;
					}
				}

				// Fall back to default resolution (PATH / rpath / already-loaded).
				return NativeLibrary.TryLoad(fileName, assembly, searchPath, out IntPtr fallback) ? fallback : IntPtr.Zero;
			});
		}

		private static string MapLibraryName(string libraryName)
		{
			if (OperatingSystem.IsWindows())
			{
				return libraryName + ".dll";
			}
			if (OperatingSystem.IsMacOS())
			{
				return "lib" + libraryName.TrimStart('l', 'i', 'b') + ".dylib";
			}
			return "lib" + libraryName.TrimStart('l', 'i', 'b') + ".so";
		}
	}
}
