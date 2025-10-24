using Clio.Workspace;

namespace Clio.Command
{
	using System;
	using System.IO.Abstractions;
	using System.Linq;
	using Common;
	using Workspaces;
	using CommandLine;
	using FluentValidation;
	using FluentValidation.Results;

	#region Class: DownloadConfigurationCommandOptionsValidator

	public class DownloadConfigurationCommandOptionsValidator : AbstractValidator<DownloadConfigurationCommandOptions>
	{
		public DownloadConfigurationCommandOptionsValidator(System.IO.Abstractions.IFileSystem fileSystem)
		{
			System.IO.Abstractions.IFileSystem fileSystem1 = fileSystem;
			
			RuleFor(o => o.BuildZipPath)
				.Custom((value, context) =>
				{
					if (string.IsNullOrWhiteSpace(value))
					{
						// If BuildZipPath is not provided, no validation needed (will use environment)
						return;
					}

					// Check if the path exists (either file or directory)
					bool isFile = fileSystem1.File.Exists(value);
					bool isDirectory = fileSystem1.Directory.Exists(value);

					if (!isFile && !isDirectory)
					{
						context.AddFailure(new ValidationFailure
						{
							ErrorCode = "FILE001",
							ErrorMessage = $"Path not found: {value}",
							Severity = Severity.Error,
							AttemptedValue = value,
						});
						return;
					}

					// If it's a file, check if it has .zip extension
					if (isFile)
					{
						string extension = fileSystem1.Path.GetExtension(value);
						if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
						{
							context.AddFailure(new ValidationFailure
							{
								ErrorCode = "FILE002",
								ErrorMessage = $"File must have .zip extension. Current extension: {extension}",
								Severity = Severity.Error,
								AttemptedValue = value,
							});
							return;
						}

						// Check if the file is not empty
						IFileInfo fileInfo = fileSystem1.FileInfo.New(value);
						if (fileInfo.Length == 0)
						{
							context.AddFailure(new ValidationFailure
							{
								ErrorCode = "FILE003",
								ErrorMessage = $"Zip file is empty: {value}",
								Severity = Severity.Error,
								AttemptedValue = value,
							});
						}
					}
					// If it's a directory, check if it's not empty
					else {
						if (!fileSystem1.Directory.EnumerateFileSystemEntries(value).Any())
						{
							context.AddFailure(new ValidationFailure
							{
								ErrorCode = "FILE004",
								ErrorMessage = $"Directory is empty: {value}",
								Severity = Severity.Error,
								AttemptedValue = value,
							});
						}
					}
				});
		}
	}

	#endregion

	#region Class: DownloadLibsCommandOptions

	[Verb("download-configuration", Aliases = ["dconf"],
		HelpText = "Download libraries from web-application")]
	public class DownloadConfigurationCommandOptions : EnvironmentOptions
	{
	[Option('b', "build", Required = false, 
		HelpText = "Path to Creatio zip file or extracted directory to get configuration from")]
	public string BuildZipPath { get; set; }
	}

	#endregion

	#region Class: DownloadLibsCommand

	public class DownloadConfigurationCommand : Command<DownloadConfigurationCommandOptions>
	{
		
		#region Fields: Private

		private readonly IApplicationDownloader _applicationDownloader;
		private readonly IZipBasedApplicationDownloader _zipBasedApplicationDownloader;
		private readonly IWorkspace _workspace;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public DownloadConfigurationCommand(
			IApplicationDownloader applicationDownloader, 
			IZipBasedApplicationDownloader zipBasedApplicationDownloader,
			IWorkspace workspace, 
			ILogger logger) {
			applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
			zipBasedApplicationDownloader.CheckArgumentNull(nameof(zipBasedApplicationDownloader));
			workspace.CheckArgumentNull(nameof(workspace));
			_applicationDownloader = applicationDownloader;
			_zipBasedApplicationDownloader = zipBasedApplicationDownloader;
			_workspace = workspace;
			_logger = logger;
		}
		#endregion
		
		#region Methods: Public

	public override int Execute(DownloadConfigurationCommandOptions options) {
		try
		{
			if (!string.IsNullOrWhiteSpace(options.BuildZipPath))
			{
				// Download from the zip file or directory (auto-detected)
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG] DownloadConfigurationCommand: Using build mode with path={options.BuildZipPath}");
				}
				_zipBasedApplicationDownloader.DownloadFromPath(options.BuildZipPath);
			}
			else
			{
				// Download from the live environment
				if (Program.IsDebugMode)
				{
					_logger.WriteInfo($"[DEBUG] DownloadConfigurationCommand: Using environment mode");
				}
				_applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
			}
			
			_logger.WriteLine("Done");
			return 0;
		}
		catch (Exception ex)
		{
			_logger.WriteError($"Error: {ex.Message}");
			
			if (Program.IsDebugMode)
			{
				_logger.WriteError($"[DEBUG] Stack trace: {ex.StackTrace}");
			}
			
			return 1;
		}
	}

	#endregion

	}

	#endregion

}