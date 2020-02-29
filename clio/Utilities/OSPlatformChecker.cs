using System;

namespace Clio.Utilities
{
	class OSPlatformChecker
	{
		public static bool IsWindowsEnvironment() {
			switch (Environment.OSVersion.Platform) {
				case PlatformID.Win32NT:
				case PlatformID.Win32S:
				case PlatformID.Win32Windows:
				case PlatformID.WinCE:
					return true;
				default:
					return false;
			}
		}
	}
}
