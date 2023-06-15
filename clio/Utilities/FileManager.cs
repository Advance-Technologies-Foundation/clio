using System;
using System.Diagnostics;

namespace Clio.Utilities
{
    internal class FileManager
    {
		public static void OpenFile(string filePath)
		{
			// clio cfg open
			if (OSPlatformChecker.GetIsWindowsEnvironment())
			{
				Console.WriteLine($"Open {filePath}...");
				Process.Start(filePath);
				//Process.Start(new ProcessStartInfo("cmd", $"/c start {filePath}") { CreateNoWindow = true });
			}
			else
			{
				throw new NotFiniteNumberException("Command not supported for current platform...");
			}
		}
	}
}
