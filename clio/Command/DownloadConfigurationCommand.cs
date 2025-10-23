namespace Clio.Command
{
	using System;
	using System.IO;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;
	using FluentValidation;
	using FluentValidation.Results;

	#region Class: DownloadConfigurationCommandOptionsValidator

	public class DownloadConfigurationCommandOptionsValidator : AbstractValidator<DownloadConfigurationCommandOptions>
	{
		public DownloadConfigurationCommandOptionsValidator()
		{
			RuleFor(o => o.BuildZipPath)
				.Custom((value, context) =>
				{
					if (string.IsNullOrWhiteSpace(value))
					{
						// If BuildZipPath is not provided, no validation needed (will use environment)
						return;
					}

					// Check if file exists
					if (!File.Exists(value))
					{
						context.AddFailure(new ValidationFailure
						{
							ErrorCode = "FILE001",
							ErrorMessage = $"Zip file not found: {value}",
							Severity = Severity.Error,
							AttemptedValue = value,
						});
						return;
					}

					// Check if file has .zip extension
					string extension = Path.GetExtension(value);
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

					// Check if file is not empty
					FileInfo fileInfo = new FileInfo(value);
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
				});
		}
	}

	#endregion

	#region Class: DownloadLibsCommandOptions

	[Verb("download-configuration", Aliases = new [] { "dconf" },
		HelpText = "Download libraries from web-application")]
	public class DownloadConfigurationCommandOptions : EnvironmentOptions
	{
		[Option('b', "build", Required = false, 
			HelpText = "Path to Creatio zip file to extract configuration from")]
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
					// Download from zip file
					_zipBasedApplicationDownloader.DownloadFromZip(options.BuildZipPath);
				}
				else
				{
					// Download from live environment
					_applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
				}
				
				_logger.WriteLine("Done");
				return 0;
			}
			catch (Exception ex)
			{
				_logger.WriteError($"Error: {ex.Message}");
				return 1;
			}
		}

		#endregion

	}

	#endregion

}