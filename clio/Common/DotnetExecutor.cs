using System;
using System.Diagnostics;

namespace Clio.Common
{
	public class DotnetExecutor : IDotnetExecutor
	{

		public string Execute(string command, bool waitForExit, string workingDirectory = null) {
			command.CheckArgumentNullOrWhiteSpace(nameof(command));
			using (var process = new Process()) {
				process.StartInfo = new ProcessStartInfo {
					FileName = "dotnet",
					Arguments = command,
					CreateNoWindow = true,
					UseShellExecute = false,
					WorkingDirectory = workingDirectory,
					RedirectStandardOutput = true,
				};
				process.EnableRaisingEvents = waitForExit;
				process.Start();
				if (waitForExit) {
					process.WaitForExit();
				}
				return process.StandardOutput.ReadToEnd();
			}
		}

	}
}