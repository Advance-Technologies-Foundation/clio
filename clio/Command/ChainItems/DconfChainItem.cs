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
		
		string ncDir = Path.Combine(workspacePathBuilder.RootPath, ".application", "net-core");
		string nfDir = Path.Combine(workspacePathBuilder.RootPath, ".application", "net-framework");
		
		bool restoreRequired = (!fileSystem.ExistsDirectory(ncDir) || (fileSystem.ExistsDirectory(ncDir) && fileSystem.IsEmptyDirectory(ncDir)))
							   || 
							   (!fileSystem.ExistsDirectory(nfDir) || (fileSystem.ExistsDirectory(nfDir) && fileSystem.IsEmptyDirectory(nfDir)));
		
		if (!restoreRequired) {
			return 0;
		}
		
		const string question = "Would you like to restore workspace? (y/n)";
		logger.WriteLine(question);
		
		string answer = Console.ReadLine();
		if (answer == null || !answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase)) {
			return 0;
		}
		
		if (context.TryGetValue(nameof(DownloadConfigurationCommandOptions.Environment), out object environment)) {
			string value = environment as string;
			if (!string.IsNullOrWhiteSpace(value)) {
				string[] envs = value.Split(',');
				int i = 0;
				foreach (string env in envs) {
					DownloadConfigurationCommandOptions options = new () {
						Environment = env
					};
					try {
						i+=dconf.Execute(options);
					}
					catch (Exception ex) {
						var message = $"Failed to download configuration from environment {env}";
						return Error.Failure("Failed.Dconf", $"{message}{Environment.NewLine}{ex.Message}");
					}
				}
				return i;
			}
		}
		
		
		if (context.TryGetValue(nameof(DownloadConfigurationCommandOptions.BuildZipPath), out object buildZipPath)) {
			string value = buildZipPath as string;
			if (!string.IsNullOrWhiteSpace(value)) {
				string[] buildPaths = value.Split(',');
				int i = 0;
				foreach (string buildPath in buildPaths) {
					DownloadConfigurationCommandOptions options = new () {
						BuildZipPath = buildPath
					};
					try {
						i+=dconf.Execute(options);
					}
					catch (Exception ex) {
						var message = $"Failed to download configuration from environment {buildPath}";
						return ErrorOr.Error.Failure("Failed.Dconf", $"{message}{Environment.NewLine}{ex.Message}");
					}
				}
				return i;
			}
		}
		return 0;
	}
}
