using System;
using System.IO;

namespace Clio.Common.IIS;

internal static class AppCmdPath {
	internal static string Resolve() => OperatingSystem.IsWindows()
		? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "inetsrv", "appcmd.exe")
		: @"C:\Windows\System32\inetsrv\appcmd.exe";
}
