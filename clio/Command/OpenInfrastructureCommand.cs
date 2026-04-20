using System;
using System.Diagnostics;
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

	#endregion

	#region Constructors: Public

	public OpenInfrastructureCommand(IInfrastructurePathProvider infrastructurePathProvider, ILogger logger) {
		_infrastructurePathProvider = infrastructurePathProvider;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public override int Execute(OpenInfrastructureOptions options) {
		string infrsatructureCfgFilesFolder = _infrastructurePathProvider.GetInfrastructurePath();
		try {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				Process.Start("explorer.exe", infrsatructureCfgFilesFolder);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				Process.Start("open", infrsatructureCfgFilesFolder);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				Process.Start("xdg-open", infrsatructureCfgFilesFolder);
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
