using System;
using System.Diagnostics;

namespace Clio.Common
{

	#region Class: ProcessExecutor

	public class ProcessExecutor : IProcessExecutor
	{

		#region Methods: Public

		public string Execute(string program, string command, bool waitForExit, string workingDirectory = null, bool showOutput = false) {
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
					RedirectStandardError = true
				};
				
				
				process.EnableRaisingEvents = waitForExit;
				
				if(showOutput) {
					process.OutputDataReceived += (sender, e) =>
					{
						if (e.Data != null)
						{
							Console.WriteLine(e.Data);
						}
					};
					process.ErrorDataReceived +=(sender, e) =>
					{
						if (e.Data != null)
						{
							ConsoleColor color = Console.ForegroundColor;
							Console.ForegroundColor = e.Data.ToLower().Contains("error") 
								? ConsoleColor.DarkRed : ConsoleColor.DarkYellow;
							Console.WriteLine(e.Data);
							Console.ForegroundColor = color;
						}
					};
				}
				
				process.Start();
				if(showOutput) {
					process.BeginOutputReadLine();
					process.BeginErrorReadLine();
				}
				
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