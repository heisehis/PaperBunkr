using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using cYo.Common.Collections;
using cYo.Common.Drawing;
using cYo.Common.IO;
using cYo.Common.Runtime;
using cYo.Common.Text;
using ICSharpCode.SharpZipLib.Zip;

namespace cYo.Projects.ComicRack.Engine
{
	public class PackageManager
	{
		public enum PackageType
		{
			None,
			Installed,
			PendingInstall,
			PendingRemove
		}

		public class Package
		{
			public string Name
			{
				get;
				private set;
			}

			public string PackagePath
			{
				get;
				private set;
			}

			public PackageType PackageType
			{
				get;
				private set;
			}

			public string Description
			{
				get;
				private set;
			}

			public string Author
			{
				get;
				private set;
			}

			public string Version
			{
				get;
				private set;
			}

			public string HelpLink
			{
				get;
				private set;
			}

			public Image Image
			{
				get;
				private set;
			}

			public bool Installed
			{
				get
				{
					if (PackageType != PackageType.Installed)
					{
						return PackageType == PackageType.PendingInstall;
					}
					return true;
				}
			}

			public string[] KeepFiles
			{
				get;
				private set;
			}

			/// <summary>
			/// True when this package's own <c>plugin.xml</c> (at <see cref="PackagePath"/>'s root)
			/// declares <c>tier="Native"</c> (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-
			/// tier-design.md §4, implementation plan Phase 2 Step 2.1). Computed once here, in
			/// <see cref="InitValues"/>, so both the fresh-zip-peek path (<see cref="CreateFromFile"/>,
			/// via its own temp flat-unzip already done to read package.ini) and the read-from-disk
			/// path (<see cref="CreateFromPath(string,bool)"/>, used by <see cref="PackageManager.GetPackages"/>
			/// for an already-pending or already-installed package) agree, without a second manifest
			/// read. A raw <see cref="XDocument"/> peek, not the real <c>PluginManifest</c> type -
			/// this project has no dependency on <c>Paperbunkr.Plugins</c> and shouldn't gain one just
			/// for a single attribute check.
			/// </summary>
			public bool IsNativeTier
			{
				get;
				private set;
			}

			/// <summary>
			/// Correlation key matching <c>Paperbunkr.Plugins.Command.PluginKey</c> - both read the
			/// same <c>plugin.xml</c> root <c>key</c> attribute (docs/superpowers/specs/2026-09-12-
			/// plugin-management-screen-redesign-design.md §4.1). Previously there was no such
			/// property at all: <see cref="Name"/> came from package.ini/a folder-name heuristic,
			/// while commands were keyed from the manifest independently - two uncoordinated identity
			/// systems that happened to agree only by luck of folder naming. Falls back to a hash of
			/// the absolute install path (never the bare folder name, and never empty) when the
			/// manifest is missing/unreadable or has no key - see <see cref="FallbackKey"/>.
			/// </summary>
			public string Key
			{
				get;
				private set;
			}

			private Package(string name)
			{
				Name = name;
			}

			private T GetValue<T>(string value, T def)
			{
				return IniFile.GetValue(Path.Combine(PackagePath, "package.ini"), value, def);
			}

			private void InitValues()
			{
				IniFile iniFile = new IniFile(Path.Combine(PackagePath, "package.ini"));

				(bool isNative, string manifestKey, string manifestName) = ReadManifestAttributes(PackagePath);
				IsNativeTier = isNative;
				Key = !string.IsNullOrWhiteSpace(manifestKey) ? manifestKey : FallbackKey(PackagePath);

				string originalName = Name;
				Name = !string.IsNullOrWhiteSpace(manifestName)
					? manifestName
					: iniFile.GetValue("Name", FileToName(originalName));

				Description = iniFile.GetValue("Description", string.Empty);
				Author = iniFile.GetValue("Author", string.Empty);
				Version = iniFile.GetValue("Version", string.Empty);
				HelpLink = iniFile.GetValue("HelpLink", string.Empty);
				KeepFiles = iniFile.GetValue("KeepFiles", string.Empty).Split(',').TrimStrings()
					.RemoveEmpty()
					.ToArray();
				try
				{
					Image = BitmapExtensions.BitmapFromFile(Path.Combine(PackagePath, iniFile.GetValue("Image", string.Empty))).Scale(32, 32);
				}
				catch
				{
				}
			}

			/// <summary>Single read of <c>plugin.xml</c>'s root attributes (tier/key/name together) -
			/// replaces what used to be a separate <c>DetectNativeTier</c> read plus what would
			/// otherwise be two more (docs/superpowers/specs/2026-09-12-plugin-management-screen-
			/// redesign-design.md §4.1's illustrative split was three methods; folded into one file
			/// read here since every call site needs all three attributes from the same file anyway).</summary>
			private static (bool isNative, string key, string name) ReadManifestAttributes(string packagePath)
			{
				string manifestPath = Path.Combine(packagePath, "plugin.xml");
				try
				{
					XDocument manifest = XDocument.Load(manifestPath);
					string tier = manifest.Root?.Attribute("tier")?.Value;
					string key = manifest.Root?.Attribute("key")?.Value;
					string name = manifest.Root?.Attribute("name")?.Value;
					bool isNative = string.Equals(tier, "Native", StringComparison.OrdinalIgnoreCase);
					return (isNative, key, name);
				}
				catch
				{
					return (false, null, null);
				}
			}

			/// <summary>Never string.Empty and never just the bare folder name (docs/superpowers/specs/
			/// 2026-09-12-plugin-management-screen-redesign-design.md §4.1) - a hash of the absolute
			/// install path is trivially unique across the whole plugins root, with no heuristic list
			/// of "generic-sounding" folder names (dist/bin/Release/...) to guess and maintain.</summary>
			private static string FallbackKey(string packagePath)
			{
				byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(packagePath).ToUpperInvariant()));
				return Convert.ToHexString(hash).Substring(0, 16);
			}

			private static string FileToName(string file)
			{
				return Path.GetFileNameWithoutExtension(file).RemoveDigits().Replace(".", " ")
					.Trim()
					.StartToUpper()
					.PascalToSpaced();
			}

			public static void UnzipFile(string packagePath, string targetPath)
			{
				UnzipFile(packagePath, targetPath, preserveStructure: false);
			}

			/// <summary>
			/// <paramref name="preserveStructure"/> true keeps each entry's full relative directory
			/// path instead of flattening to its bare filename - required for a Native-tier package
			/// whose own build output includes e.g. `runtimes/&lt;rid&gt;/native/...` (docs/superpowers/
			/// specs/2026-09-11-plugin-api-v4-native-tier-design.md §4). Flattening (the original,
			/// default behavior, still exactly what every `Script`-tier package continues to get)
			/// silently breaks native-dependency resolution for a `Native` package - a real bug found
			/// during external review of the design, not a hypothetical.
			/// </summary>
			public static void UnzipFile(string packagePath, string targetPath, bool preserveStructure)
			{
				Directory.CreateDirectory(targetPath);
				using (ZipFile zipFile = new ZipFile(packagePath))
				{
					foreach (ZipEntry item in from ZipEntry ze in zipFile
						where ze.IsFile
						select ze)
					{
						string destinationPath = preserveStructure
							? Path.Combine(targetPath, item.Name.Replace('/', Path.DirectorySeparatorChar))
							: Path.Combine(targetPath, Path.GetFileName(item.Name));

						string? destinationDir = Path.GetDirectoryName(destinationPath);
						if (preserveStructure && !string.IsNullOrEmpty(destinationDir))
						{
							Directory.CreateDirectory(destinationDir);
						}

						using (FileStream destination = File.Create(destinationPath))
						{
							using (Stream stream = zipFile.GetInputStream(item))
							{
								stream.CopyTo(destination);
							}
						}
					}
				}
			}

			public static string GetName(string file)
			{
				try
				{
					return CreateFromFile(file).Name;
				}
				catch (Exception)
				{
					return null;
				}
			}

			public static Package CreateFromPath(string name, string path, bool pending)
			{
				Package package = new Package(name)
				{
					PackagePath = path
				};
				package.InitValues();
				if (pending)
				{
					package.PackageType = PackageType.PendingInstall;
				}
				else if (File.Exists(Path.Combine(path, ".remove")))
				{
					package.PackageType = PackageType.PendingRemove;
				}
				else
				{
					package.PackageType = PackageType.Installed;
				}
				return package;
			}

			public static Package CreateFromPath(string path, bool pending)
			{
				return CreateFromPath(FileToName(path), path, pending);
			}

			public static Package CreateFromFile(string file)
			{
				string text = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
				try
				{
					UnzipFile(file, text);
					Package package = CreateFromPath(FileToName(file), text, pending: false);
					package.PackagePath = file;
					package.PackageType = PackageType.None;
					return package;
				}
				catch (Exception)
				{
					return null;
				}
				finally
				{
					FileUtility.SafeDirectoryDelete(text);
				}
			}
		}

		private string packagePath;

		private string pendingPackagePath;

		public string PackagePath
		{
			get
			{
				return packagePath;
			}
			set
			{
				packagePath = value;
				try
				{
					Directory.CreateDirectory(packagePath);
				}
				catch
				{
				}
			}
		}

		public string PendingPackagePath
		{
			get
			{
				return pendingPackagePath;
			}
			set
			{
				pendingPackagePath = value;
				try
				{
					Directory.CreateDirectory(pendingPackagePath);
				}
				catch
				{
				}
			}
		}

		public bool IsValid
		{
			get
			{
				try
				{
					return Directory.Exists(PackagePath) && Directory.Exists(PendingPackagePath);
				}
				catch
				{
					return false;
				}
			}
		}

		public PackageManager(string path, string tempPath, bool commit)
		{
			PackagePath = path;
			PendingPackagePath = tempPath;
			if (commit)
			{
				Commit();
			}
		}

		public IList<Package> GetPackages()
		{
			List<Package> list = new List<Package>();
			try
			{
				string[] directories = Directory.GetDirectories(PackagePath);
				foreach (string text in directories)
				{
					if (!string.Equals(text, PendingPackagePath, StringComparison.OrdinalIgnoreCase))
					{
						list.Add(Package.CreateFromPath(text, pending: false));
					}
				}
				string[] directories2 = Directory.GetDirectories(PendingPackagePath);
				foreach (string path in directories2)
				{
					list.Add(Package.CreateFromPath(path, pending: true));
				}
				return list;
			}
			catch
			{
				return list;
			}
		}

		public IEnumerable<string> GetPackageNames()
		{
			return from p in GetPackages()
				select p.Name;
		}

		public bool PackageExists(string name)
		{
			return GetPackageNames().Contains(name, StringComparer.OrdinalIgnoreCase);
		}

		public bool PackageFileExists(string file)
		{
			try
			{
				return PackageExists(Package.CreateFromFile(file).Name);
			}
			catch (Exception)
			{
				return false;
			}
		}

		public bool Install(string packageFile)
		{
			Package package = Package.CreateFromFile(packageFile);
			if (package == null)
			{
				return false;
			}
			string text = GetPackagePath(package, pending: true);
			try
			{
				FileUtility.SafeDirectoryDelete(text);
				Package.UnzipFile(packageFile, text, package.IsNativeTier);
				return true;
			}
			catch (Exception)
			{
				FileUtility.SafeDirectoryDelete(text);
				return false;
			}
		}

		public bool Uninstall(Package package)
		{
			try
			{
				switch (package.PackageType)
				{
				case PackageType.PendingInstall:
					FileUtility.SafeDirectoryDelete(package.PackagePath);
					break;
				case PackageType.Installed:
					FileUtility.CreateEmpty(Path.Combine(package.PackagePath, ".remove"));
					break;
				}
				return true;
			}
			catch
			{
				return false;
			}
		}

		public void Commit()
		{
			(from p in GetPackages()
				where p.PackageType == PackageType.PendingRemove
				select p).ForEach(delegate(Package p)
			{
				CommitUninstallPackage(p);
			});
			(from p in GetPackages()
				where p.PackageType == PackageType.PendingInstall
				select p).ForEach(delegate(Package p)
			{
				CommitInstallPackage(p);
			});
		}

		public void RemovePending()
		{
			GetPackages().ForEach(delegate(Package p)
			{
				FileUtility.SafeDelete(Path.Combine(p.PackagePath, ".remove"));
			});
			FileUtility.SafeDirectoryDelete(PendingPackagePath);
		}

		private bool CommitInstallPackage(Package package)
		{
			if (package.PackageType != PackageType.PendingInstall)
			{
				return false;
			}
			string text = GetPackagePath(package, pending: false);
			try
			{
				Directory.CreateDirectory(text);
				if (!package.KeepFiles.IsEmpty())
				{
                    string[] keep = package.KeepFiles;

                    // Convert wildcard patterns to regular expressions
                    List<Regex> keepPatterns = keep.Select(pattern =>
                    {
                        string regexPattern = $"^{Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")}$";
                        return new Regex(regexPattern, RegexOptions.IgnoreCase);
                    }).ToList();

                    foreach (string item in from f in new DirectoryInfo(text).EnumerateFiles().Select(x => x.Name)
											where !keepPatterns.Any(regex => regex.IsMatch(f))
                                            select f)
					{
						FileUtility.SafeDelete(item);
					}

                    foreach (string item2 in from f in new DirectoryInfo(text).EnumerateDirectories().Select(x => x.Name)
                                             where !keepPatterns.Any(regex => regex.IsMatch(f))
                                            select f)
                    {
						FileUtility.SafeDirectoryDelete(item2);
					}
				}
				if (package.IsNativeTier)
				{
					CopyDirectoryPreservingStructure(package.PackagePath, text);
				}
				else
				{
					string[] files = Directory.GetFiles(package.PackagePath);
					foreach (string text2 in files)
					{
						File.Copy(text2, Path.Combine(text, Path.GetFileName(text2)), overwrite: true);
					}
				}
				return true;
			}
			catch
			{
				FileUtility.SafeDirectoryDelete(text);
				return false;
			}
			finally
			{
				FileUtility.SafeDirectoryDelete(package.PackagePath);
			}
		}

		/// <summary>
		/// The second of two flattening sites found during implementation (the first being
		/// <see cref="Package.UnzipFile"/> above) - <see cref="CommitInstallPackage"/>'s original,
		/// non-recursive <c>Directory.GetFiles</c> copy would silently discard subfolder structure
		/// again even after fixing extraction alone, for a `Native`-tier package that had it preserved
		/// correctly up to this point (docs/superpowers/specs/2026-09-11-plugin-api-v4-native-tier-
		/// design.md §4).
		/// </summary>
		private static void CopyDirectoryPreservingStructure(string sourceDir, string destinationDir)
		{
			foreach (string filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
			{
				string relativePath = Path.GetRelativePath(sourceDir, filePath);
				string destinationPath = Path.Combine(destinationDir, relativePath);
				string? destinationFileDir = Path.GetDirectoryName(destinationPath);
				if (!string.IsNullOrEmpty(destinationFileDir))
				{
					Directory.CreateDirectory(destinationFileDir);
				}

				File.Copy(filePath, destinationPath, overwrite: true);
			}
		}

		private static bool CommitUninstallPackage(Package package)
		{
			return FileUtility.SafeDirectoryDelete(package.PackagePath);
		}

		private string GetPackagePath(Package package, bool pending)
		{
			return Path.Combine(pending ? PendingPackagePath : PackagePath, package.Name);
		}
	}
}
