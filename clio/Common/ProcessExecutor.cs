using System;
using System.Diagnostics;
using System.Text;

namespace Clio.Common
{

	#region Class: ProcessExecutor

	public class ProcessExecutor : IProcessExecutor{
		private readonly ILogger _logger;

		public ProcessExecutor(ILogger logger) {
			_logger = logger;
		}
		#region Methods: Public

		public string Execute(string program, string command, bool waitForExit, string workingDirectory = null, bool showOutput = false, bool suppressErrors = false) {
			program.CheckArgumentNullOrWhiteSpace(nameof(program));
			command.CheckArgumentNullOrWhiteSpace(nameof(command));
			using Process process = new Process();
			process.StartInfo = new ProcessStartInfo {
				FileName = program,
				Arguments = command,
				CreateNoWindow = true,
				UseShellExecute = false,
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			};
			StringBuilder sb = new StringBuilder();
			process.EnableRaisingEvents = waitForExit;
				
			if(showOutput) {
				process.OutputDataReceived += (sender, e) => {
					if (e.Data != null) {
						_logger.WriteInfo(e.Data);
						// Console.WriteLine(e.Data);
						sb.Append(e.Data);
					}
				};
				process.ErrorDataReceived +=(sender, e) => {
					if (e.Data != null) {
						//ConsoleColor color = Console.ForegroundColor;
						
						// Console.ForegroundColor = e.Data.ToLower().Contains("error") 
						// 	? ConsoleColor.DarkRed : ConsoleColor.DarkYellow;
						// Console.WriteLine(e.Data);

						// Suppress errors if requested (useful during connection retries)
						if (!suppressErrors) {
							if (e.Data.ToLower().Contains("error", StringComparison.OrdinalIgnoreCase)) {
								_logger.WriteError(e.Data);
							}
							else {
								_logger.WriteInfo(e.Data);
							}
						}
						
						//Console.ForegroundColor = color;
						sb.Append(e.Data);
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
			if(!showOutput) {
				return process.StandardOutput.ReadToEnd();
			}
			return sb.ToString();
		}

		#endregion

	}

	#endregion

}
