using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace cYo.Common.Win32
{
	public static class ShellRegister
	{
		private static class Native
		{
			[Flags]
			public enum HChangeNotifyEventID
			{
				SHCNE_ALLEVENTS = int.MaxValue,
				SHCNE_ASSOCCHANGED = 0x8000000,
				SHCNE_ATTRIBUTES = 0x800,
				SHCNE_CREATE = 0x2,
				SHCNE_DELETE = 0x4,
				SHCNE_DRIVEADD = 0x100,
				SHCNE_DRIVEADDGUI = 0x10000,
				SHCNE_DRIVEREMOVED = 0x80,
				SHCNE_EXTENDED_EVENT = 0x4000000,
				SHCNE_FREESPACE = 0x40000,
				SHCNE_MEDIAINSERTED = 0x20,
				SHCNE_MEDIAREMOVED = 0x40,
				SHCNE_MKDIR = 0x8,
				SHCNE_NETSHARE = 0x200,
				SHCNE_NETUNSHARE = 0x400,
				SHCNE_RENAMEFOLDER = 0x20000,
				SHCNE_RENAMEITEM = 0x1,
				SHCNE_RMDIR = 0x10,
				SHCNE_SERVERDISCONNECT = 0x4000,
				SHCNE_UPDATEDIR = 0x1000,
				SHCNE_UPDATEIMAGE = 0x8000
			}

			[Flags]
			public enum HChangeNotifyFlags
			{
				SHCNF_DWORD = 0x3,
				SHCNF_IDLIST = 0x0,
				SHCNF_PATHA = 0x1,
				SHCNF_PATHW = 0x5,
				SHCNF_PRINTERA = 0x2,
				SHCNF_PRINTERW = 0x6,
				SHCNF_FLUSH = 0x1000,
				SHCNF_FLUSHNOWAIT = 0x2000
			}

			[DllImport("shell32.dll")]
			public static extern void SHChangeNotify(HChangeNotifyEventID wEventId, HChangeNotifyFlags uFlags, IntPtr dwItem1, IntPtr dwItem2);
		}

		private static int result = -1;

		public static RegistryKey ClassesRoot => Registry.ClassesRoot;

		/// <summary>
		/// Base of the writable classes hive. Every write in this class used to go straight through
		/// <see cref="ClassesRoot"/> (<c>HKEY_CLASSES_ROOT</c>), which - contrary to what its merged
		/// read view might suggest - requires admin elevation to CREATE a new key: .NET's
		/// <c>RegistryKey.CreateSubKey</c> resolves that write to the underlying
		/// <c>HKEY_LOCAL_MACHINE\SOFTWARE\Classes</c>, not a per-user location, and does not get the
		/// legacy UAC registry-virtualization fallback once an app manifest declares any
		/// <c>requestedExecutionLevel</c> (ComicRackCE's own app.manifest does, at "asInvoker" - see
		/// its comment on that element). Confirmed live: a non-elevated write via
		/// <see cref="ClassesRoot"/> throws <see cref="UnauthorizedAccessException"/>
		/// ("Access to the registry key 'HKEY_CLASSES_ROOT\...' is denied"), which crashed
		/// Paperbunkr's Preferences &gt; Advanced file-association toggle - CE's own equivalent
		/// (<see cref="Paperbunkr.Engine.IO.Provider.FileFormat.RegisterShell"/>, not currently wired
		/// into Paperbunkr's UI) wraps this in a bare <c>catch</c> so it fails silently instead of
		/// crashing, but silent failure isn't better - either way the feature just doesn't work
		/// without elevation. <c>HKEY_CURRENT_USER\Software\Classes</c> is the standard per-user
		/// equivalent: no elevation needed, and it's merged into the effective
		/// <c>HKEY_CLASSES_ROOT</c> read view (winning over HKLM on conflicts), so every read in this
		/// class can stay exactly as-is - only the write targets change.
		/// </summary>
		private static RegistryKey ClassesRootWritable => Registry.CurrentUser;

		private static string WritablePath(string subkey) => @"Software\Classes\" + subkey;

		public static bool CanRegisterShell
		{
			get
			{
				if (result != -1)
				{
					return result != 0;
				}
				string subkey = WritablePath(Guid.NewGuid().ToString());
				try
				{
					using (ClassesRootWritable.CreateSubKey(subkey))
					{
					}
					result = 1;
					return true;
				}
				catch
				{
					result = 0;
					return false;
				}
				finally
				{
					try
					{
						ClassesRootWritable.DeleteSubKey(subkey);
					}
					catch
					{
					}
				}
			}
		}

		public static void RefreshShell()
		{
			Native.SHChangeNotify(Native.HChangeNotifyEventID.SHCNE_ASSOCCHANGED, Native.HChangeNotifyFlags.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
		}

		public static void RegisterFileOpen(string typeId, string docExtension, string docName, string appPath, string iconPath)
		{
			using (RegistryKey registryKey = ClassesRootWritable.CreateSubKey(WritablePath(docExtension)))
			{
				registryKey.SetValue(null, typeId);
			}
			using (RegistryKey registryKey2 = ClassesRootWritable.CreateSubKey(WritablePath(typeId)))
			{
				registryKey2.SetValue(null, docName);
				using (RegistryKey registryKey3 = registryKey2.CreateSubKey("DefaultIcon"))
				{
					registryKey3.SetValue(null, iconPath);
				}
				using (RegistryKey registryKey4 = registryKey2.CreateSubKey("shell\\open\\command"))
				{
					registryKey4.SetValue(null, "\"" + appPath + "\" \"%1\"");
				}
			}
		}

		public static void RegisterFileOpen(string typeId, string docExtension, string docName, int icon)
		{
			string location = Assembly.GetEntryAssembly().Location;
			string iconPath = "\"" + location + "\"," + icon;
			RegisterFileOpen(typeId, docExtension, docName, location, iconPath);
		}

		public static void UnregisterFileOpen(string typeId, string docExtension)
		{
			if (IsFileOpenRegistered(typeId, docExtension))
			{
				using (RegistryKey registryKey = ClassesRootWritable.OpenSubKey(WritablePath(docExtension), true))
				{
					registryKey?.SetValue(null, string.Empty);
				}
			}
		}

		public static bool IsFileOpenRegistered(string typeId, string docExtension)
		{
			using (RegistryKey registryKey = ClassesRoot.OpenSubKey(docExtension))
			{
				return registryKey != null && typeId == (string)registryKey.GetValue(null);
			}
		}

		public static bool IsFileOpenInUse(string typeId, string docExtension)
		{
			if (IsFileOpenRegistered(typeId, docExtension))
			{
				return false;
			}
			using (RegistryKey registryKey = Registry.ClassesRoot.OpenSubKey(docExtension))
			{
				return registryKey != null;
			}
		}

		public static void RegisterFileOpenWith(string docExtension, string appPath, string friendlyName, string typeId)
		{
			string fileName = Path.GetFileName(appPath);
			using (RegistryKey registryKey = ClassesRootWritable.CreateSubKey(WritablePath(docExtension + "\\OpenWithProgIds")))
			{
				registryKey?.SetValue(typeId, string.Empty);
			}
			using (RegistryKey registryKey2 = ClassesRootWritable.CreateSubKey(WritablePath("Applications\\" + fileName + "\\shell\\Open")))
			{
				if (!string.IsNullOrEmpty(friendlyName))
				{
					registryKey2.SetValue("FriendlyAppName", friendlyName);
				}
				using (RegistryKey registryKey3 = ClassesRootWritable.CreateSubKey(WritablePath("command")))
				{
					registryKey3.SetValue(null, "\"" + appPath + "\" \"%1\"");
				}
			}
		}

		public static void RegisterFileOpenWith(string docExtension, string typeId)
		{
			Assembly entryAssembly = Assembly.GetEntryAssembly();
			RegisterFileOpenWith(docExtension, entryAssembly.Location, entryAssembly.GetCustomAttributes(inherit: false).OfType<AssemblyTitleAttribute>().FirstOrDefault()
				.Title, typeId);
		}

		public static void UnregisterFileOpenWith(string docExtension, string appPath, string typeId)
		{
			string fileName = Path.GetFileName(appPath);
			ClassesRootWritable.DeleteSubKeyTree(WritablePath(docExtension + "\\OpenWithList\\" + fileName), false); //Delete any old entries
			using (RegistryKey registryKey = ClassesRootWritable.OpenSubKey(WritablePath(docExtension + "\\OpenWithProgIds"), true))
			{
				registryKey?.DeleteValue(typeId, false);
			}
		}

		public static void UnregisterFileOpenWith(string docExtension, string typeId)
		{
			UnregisterFileOpenWith(docExtension, Assembly.GetEntryAssembly().Location, typeId);
		}

		public static bool IsFileOpenWithRegistered(string docExtension, string appPath, string typeId)
		{
			string fileName = Path.GetFileName(appPath);
			using (RegistryKey registryKey = ClassesRoot.OpenSubKey(docExtension + "\\OpenWithProgIds"))
			{
				return registryKey?.GetValue(typeId) != null;
			}
		}

		public static bool IsFileOpenWithRegistered(string docExtension, string typeId)
		{
			return IsFileOpenWithRegistered(docExtension, Assembly.GetEntryAssembly().Location, typeId);
		}

		public static void RegisterFileCommand(string docExtension, string verbName, string menuText, string appPath, string commandParameters)
		{
			verbName = verbName.Replace("&", "");
			using (RegistryKey registryKey = ClassesRootWritable.CreateSubKey(WritablePath(docExtension + "\\Shell\\" + verbName)))
			{
				registryKey.SetValue(string.Empty, menuText);
				using (RegistryKey registryKey2 = registryKey.CreateSubKey("command"))
				{
					registryKey2.SetValue(string.Empty, "\"" + appPath + "\" " + commandParameters);
				}
			}
		}

		public static void RegisterFileCommand(string docExtension, string verbName, string menuText, string commandParameters)
		{
			RegisterFileCommand(docExtension, verbName, menuText, Assembly.GetEntryAssembly().Location, commandParameters);
		}

		public static void RegisterFileCommand(string docExtension, string menuText, string commandParameters)
		{
			RegisterFileCommand(docExtension, menuText, menuText, commandParameters);
		}

		public static void RegisterFileCommand(string docExtension, string menuText)
		{
			RegisterFileCommand(docExtension, menuText, "\"%1\"");
		}
	}
}
