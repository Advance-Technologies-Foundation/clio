using System;
using Clio;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("unregister", HelpText = "Unregister clio commands in context menu")]
internal class UnregisterOptions{
	#region Properties: Public

	[Option('t', "Target", Default = "u", HelpText = "Target environment location. Could be user location or" +
													 " machine location. Use 'u' for set user location and 'm' to set machine location.")]
	public string Target { get; set; }

	[Option('p', "Path", HelpText = "Path where clio is stored.")]
	public string Path { get; set; }

	#endregion
}

internal class UnregisterCommand(ILogger logger, IProcessExecutor processExecutor,
		IOperationSystem operationSystem, IMacOsFinderIntegration macOsFinderIntegration,
		IMacOsMenuBarIntegration macOsMenuBarIntegration) : Command<UnregisterOptions>{
	#region Methods: Private

	private bool TryExecuteProcess(string program, string arguments, string operationDescription) {
		ProcessExecutionResult result = processExecutor.ExecuteAndCaptureAsync(
			new ProcessExecutionOptions(program, arguments) {
				SuppressErrors = true
			}).GetAwaiter().GetResult();

		if (!result.Started) {
			logger.WriteError($"Failed to {operationDescription}: process was not started. {result.StandardError}");
			return false;
		}

		if (result.ExitCode is not 0) {
			logger.WriteError(
				$"Failed to {operationDescription}: process exited with code {result.ExitCode}. {result.StandardError}");
			return false;
		}

		return true;
	}

	#endregion

	#region Methods: Public

	public override int Execute(UnregisterOptions options) {
		try {
			if (operationSystem.IsMacOS) {
				macOsFinderIntegration.UninstallAsync().GetAwaiter().GetResult();
				macOsMenuBarIntegration.UninstallAsync().GetAwaiter().GetResult();
				logger.WriteLine("Clio Finder 'Deploy Creatio' quick action and menu bar app unregistered");
				return 0;
			}

			if (!TryExecuteProcess("cmd", "/c reg delete HKEY_CLASSES_ROOT\\Folder\\shell\\clio /f",
					"remove Folder context menu registration")) {
				return 1;
			}

			if (!TryExecuteProcess("cmd", "/c reg delete HKEY_CLASSES_ROOT\\*\\shell\\clio /f",
					"remove file context menu registration")) {
				return 1;
			}

			logger.WriteLine("Clio context menu successfully unregistered");
			return 0;
		}
		catch (Exception e) {
			logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			return 1;
		}
	}

	#endregion
}
