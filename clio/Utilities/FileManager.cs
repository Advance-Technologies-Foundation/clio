using System;
using System.Diagnostics;

namespace Clio.Utilities
{
    internal class FileManager
    {
		public static void OpenFile(string filePath)
		{
			Console.WriteLine($"Open {filePath}...");
			if (OSPlatformChecker.GetIsWindowsEnvironment()) {
				Process.Start(new ProcessStartInfo("cmd", $"/c start {filePath}") { CreateNoWindow = true });
			}
			else  {
				string terminalPath = "/usr/bin/open";
				Process.Start(terminalPath, filePath);
			}
		}
	}
}
