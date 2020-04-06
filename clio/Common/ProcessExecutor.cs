using System.Diagnostics;

namespace Clio.Common
{

	#region Class: ProcessExecutor

	public class ProcessExecutor : IProcessExecutor
	{

		#region Methods: Public

		public string Execute(string program, string command, bool waitForExit, string workingDirectory = null) {
			program.CheckArgumentNullOrWhiteSpace(nameof(program));
			command.CheckArgumentNullOrWhiteSpace(nameof(command));
			using (var process = new Process()) {
				process.StartInfo = new ProcessStartInfo {
					FileName = program,
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

		#endregion

	}

	#endregion

}