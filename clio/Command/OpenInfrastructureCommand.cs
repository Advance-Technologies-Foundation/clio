using System;
using System.Runtime.InteropServices;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("open-k8-files", Aliases = ["cfg-k8f", "cfg-k8s", "cfg-k8"], HelpText = "Open folder K8 files for deployment")]
public class OpenInfrastructureOptions{ }

public class OpenInfrastructureCommand : Command<OpenInfrastructureOptions>{
	#region Fields: Private

	private readonly IInfrastructurePathProvider _infrastructurePathProvider;
	private readonly ILogger _logger;
	private readonly IProcessExecutor _processExecutor;

	#endregion

	#region Constructors: Public

	public OpenInfrastructureCommand(IInfrastructurePathProvider infrastructurePathProvider, ILogger logger,
		IProcessExecutor processExecutor) {
		_infrastructurePathProvider = infrastructurePathProvider;
		_logger = logger;
		_processExecutor = processExecutor;
	}

	#endregion

	#region Methods: Public

	public override int Execute(OpenInfrastructureOptions options) {
		string infrsatructureCfgFilesFolder = _infrastructurePathProvider.GetInfrastructurePath();
		try {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				_processExecutor.Execute("explorer.exe", infrsatructureCfgFilesFolder, waitForExit: false);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				_processExecutor.Execute("open", infrsatructureCfgFilesFolder, waitForExit: false);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				_processExecutor.Execute("xdg-open", infrsatructureCfgFilesFolder, waitForExit: false);
			}
			else {
				_logger.WriteError($"Unsupported platform: {RuntimeInformation.OSDescription}");
				return 1;
			}

			return 0;
		}
		catch (Exception e) {
			_logger.WriteError($"Failed to open folder: {e.Message}");
			_logger.WriteError($"Folder path: {infrsatructureCfgFilesFolder}");
			return 1;
		}
	}

	#endregion
}
