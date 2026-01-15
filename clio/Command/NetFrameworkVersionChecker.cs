using System;
using Microsoft.Win32;

namespace Clio.Command;

public interface INetFrameworkVersionChecker{
	#region Methods: Public

	string GetInstalledVersion();
	bool IsNetFramework472OrHigherInstalled();

	#endregion
}

public class NetFrameworkVersionChecker : INetFrameworkVersionChecker{
	#region Constants: Private

	private const int MinimumRelease = 461808; // .NET Framework 4.7.2

	#endregion

	#region Methods: Private

	private static string GetVersionFromReleaseKey(int releaseKey) {
		if (releaseKey >= 533320) {
			return "4.8.1 or later";
		}

		if (releaseKey >= 528040) {
			return "4.8";
		}

		if (releaseKey >= 461808) {
			return "4.7.2";
		}

		if (releaseKey >= 461308) {
			return "4.7.1";
		}

		if (releaseKey >= 460798) {
			return "4.7";
		}

		if (releaseKey >= 394802) {
			return "4.6.2";
		}

		if (releaseKey >= 394254) {
			return "4.6.1";
		}

		if (releaseKey >= 393295) {
			return "4.6";
		}

		return $"Unknown (Release: {releaseKey})";
	}

	#endregion

	#region Methods: Public

	public string GetInstalledVersion() {
		try {
			using RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
												  .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\");

			if (ndpKey?.GetValue("Release") != null) {
				int releaseKey = (int)ndpKey.GetValue("Release");
				return GetVersionFromReleaseKey(releaseKey);
			}

			return "Not installed";
		}
		catch (Exception) {
			return "Unable to determine";
		}
	}

	public bool IsNetFramework472OrHigherInstalled() {
		try {
			using RegistryKey ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
												  .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\");

			if (ndpKey?.GetValue("Release") != null) {
				int releaseKey = (int)ndpKey.GetValue("Release");
				return releaseKey >= MinimumRelease;
			}

			return false;
		}
		catch (Exception) {
			return false;
		}
	}

	#endregion
}
