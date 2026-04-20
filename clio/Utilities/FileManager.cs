using System;
using System.Diagnostics;
using Clio.Common;

namespace Clio.Utilities
{
    internal class FileManager
    {
		public static void OpenFile(string filePath)
		{
			ConsoleLogger.Instance.WriteLine($"Open {filePath}...");
			if (OSPlatformChecker.GetIsWindowsEnvironment()) {
#pragma warning disable CLIO004 // Static context; file-open requires direct Process usage
				Process.Start(new ProcessStartInfo("cmd", $"/c start {filePath}") { CreateNoWindow = true });
#pragma warning restore CLIO004
			} else {
				string terminalPath = "/usr/bin/open";
#pragma warning disable CLIO004 // Static context; file-open requires direct Process usage
				Process.Start(terminalPath, filePath);
#pragma warning restore CLIO004
			}
		}
	}
}
