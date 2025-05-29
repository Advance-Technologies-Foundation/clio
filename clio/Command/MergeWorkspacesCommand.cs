using System;
using System.Collections.Generic;
using System.IO;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using System.Linq;

namespace Clio.Command
{
	#region Class: MergeWorkspacesCommandOptions

	[Verb("merge-workspaces", Aliases = ["mergew"], 
		HelpText = "Merge packages from multiple workspaces and install them to the environment")]
	public class MergeWorkspacesCommandOptions : EnvironmentOptions
	{
		[Option("workspaces", Required = true, Separator = ',', 
			HelpText = "Comma-separated list of workspace paths to merge")]
		public IEnumerable<string> WorkspacePaths { get; set; }

		[Option("output", Required = false, Default = "",
			HelpText = "Path where to save the merged ZIP file. If not specified, ZIP will not be saved")]
		public string OutputPath { get; set; }

		[Option("name", Required = false, Default = "MergedCreatioPackages",
			HelpText = "Name for the resulting ZIP file (without .zip extension)")]
		public string ZipFileName { get; set; }

		[Option("install", Required = false, Default = true,
			HelpText = "Whether to install the merged packages into Creatio")]
		public bool Install { get; set; }
	}

	#endregion

	#region Class: MergeWorkspacesCommand

	public class MergeWorkspacesCommand : Command<MergeWorkspacesCommandOptions>
	{
		#region Fields: Private

		private readonly IWorkspaceMerger _workspaceMerger;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public MergeWorkspacesCommand(IWorkspaceMerger workspaceMerger, ILogger logger)
		{
			workspaceMerger.CheckArgumentNull(nameof(workspaceMerger));
			logger.CheckArgumentNull(nameof(logger));
			_workspaceMerger = workspaceMerger;
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		public override int Execute(MergeWorkspacesCommandOptions options)
		{
			try
			{
				_logger.WriteInfo("Merging workspaces...");
				
				string[] workspacePaths = options.WorkspacePaths
												.Where(i=> !string.IsNullOrWhiteSpace(i))
												.ToArray();
				
				// Verify all workspace paths exist
				foreach (string workspacePath in workspacePaths)
				{
					if (!Directory.Exists(workspacePath))
					{
						_logger.WriteError($"Workspace directory not found: {workspacePath}");
						return 1;
					}
				}
				
				// If output path is specified, save the merged ZIP file
				bool saveZip = !string.IsNullOrWhiteSpace(options.OutputPath);
				
				if (saveZip)
				{
					string zipPath = _workspaceMerger.MergeToZip(
						workspacePaths,
						options.OutputPath,
						options.ZipFileName);
						
					_logger.WriteInfo($"Packages from {workspacePaths.Length} workspaces were successfully merged to: {zipPath}");
					
					// Install if requested
					if (options.Install)
					{
						_logger.WriteInfo("Installing the merged packages...");
						_workspaceMerger.MergeAndInstall(workspacePaths, options.ZipFileName);
					}
				}
				else if (options.Install)
				{
					// Just install without saving
					_workspaceMerger.MergeAndInstall(workspacePaths, options.ZipFileName);
					_logger.WriteInfo($"Packages from {workspacePaths.Length} workspaces were successfully merged and installed.");
				}
				else
				{
					_logger.WriteWarning("Neither output path nor installation was requested. No action was performed.");
					return 0;
				}
				
				_logger.WriteInfo("Done");
				return 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError(ex.Message);
				return 1;
			}
		}

		#endregion
	}

	#endregion
}