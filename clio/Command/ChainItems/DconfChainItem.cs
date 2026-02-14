using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using ErrorOr;

namespace Clio.Command.ChainItems;

public class DconfChainItem(DownloadConfigurationCommand dconf, ILogger logger, IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem) : IFollowupUpChainItem{
	public ErrorOr<int> Execute() {
		return ErrorOr.Error.Failure("NoContext","Cannot execute without context");
	}

	public ErrorOr<int> Execute(IDictionary<string, object> context) {

		bool isEnv = context.TryGetValue(nameof(DownloadConfigurationCommandOptions.Environment),
			out object environment);

		bool isBuild = context.TryGetValue(nameof(DownloadConfigurationCommandOptions.BuildZipPath),
			out object buildZipPath);

	
		// Early exist when no environment or build path provided
		if (!(isEnv || isBuild)) {
			return 0;
		}
		
		// Early exist when directories already exist and are not empty
		string ncDir = Path.Combine(workspacePathBuilder.RootPath, ".application", "net-core");
		string nfDir = Path.Combine(workspacePathBuilder.RootPath, ".application", "net-framework");
		
		bool restoreRequired = (!fileSystem.ExistsDirectory(ncDir) || (fileSystem.ExistsDirectory(ncDir) && fileSystem.IsEmptyDirectory(ncDir)))
							   || 
							   (!fileSystem.ExistsDirectory(nfDir) || (fileSystem.ExistsDirectory(nfDir) && fileSystem.IsEmptyDirectory(nfDir)));
		
		if (!restoreRequired) {
			return 0;
		}

		if (isEnv) {
			return Restore(environment as string, true);
		}

		if (isBuild) {
			return Restore(buildZipPath as string, false);
		}
		
		return 0;
	}

	private ErrorOr<int> Restore(string value, bool isEnv) {
		if (string.IsNullOrWhiteSpace(value)) {
			return 0;
		}

		string[] buildPaths = value.Split(',');
		int i = 0;
		foreach (string buildPath in buildPaths) {
			DownloadConfigurationCommandOptions options = isEnv 
				? new DownloadConfigurationCommandOptions { Environment = buildPath }
				: new DownloadConfigurationCommandOptions { BuildZipPath = buildPath };
			
			try {
				i+= dconf.Execute(options);
			}
			catch (Exception ex) {
				string from = isEnv ? "environment" : "build path";
				string message = $"Failed to download configuration from {ConsoleLogger.WrapRed(from)} {value}";
				return Error.Failure("Failed.Dconf", $"{message}{Environment.NewLine}{ex.Message}");
			}
		}
		return i;
	}
	
	
	
	
}
