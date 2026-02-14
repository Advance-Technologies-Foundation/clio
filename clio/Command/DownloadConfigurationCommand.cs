using System;
using System.IO.Abstractions;
using System.Linq;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Workspace;
using Clio.Workspaces;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

#region Class: DownloadConfigurationCommandOptionsValidator

public class DownloadConfigurationCommandOptionsValidator : AbstractValidator<DownloadConfigurationCommandOptions>{
	#region Constructors: Public

	public DownloadConfigurationCommandOptionsValidator(IFileSystem fileSystem) {
		IFileSystem fileSystem1 = fileSystem;

		RuleFor(o => o.BuildZipPath)
			.Custom((value, context) => {
				if (string.IsNullOrWhiteSpace(value)) {
					// If BuildZipPath is not provided, no validation needed (will use environment)
					return;
				}

				// Check if the path exists (either file or directory)
				bool isFile = fileSystem1.File.Exists(value);
				bool isDirectory = fileSystem1.Directory.Exists(value);

				if (!isFile && !isDirectory) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "FILE001",
						ErrorMessage = $"Path not found: {value}",
						Severity = Severity.Error,
						AttemptedValue = value
					});
					return;
				}

				// If it's a file, check if it has .zip extension
				if (isFile) {
					string extension = fileSystem1.Path.GetExtension(value);
					if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
						context.AddFailure(new ValidationFailure {
							ErrorCode = "FILE002",
							ErrorMessage = $"File must have .zip extension. Current extension: {extension}",
							Severity = Severity.Error,
							AttemptedValue = value
						});
						return;
					}

					// Check if the file is not empty
					IFileInfo fileInfo = fileSystem1.FileInfo.New(value);
					if (fileInfo.Length == 0) {
						context.AddFailure(new ValidationFailure {
							ErrorCode = "FILE003",
							ErrorMessage = $"Zip file is empty: {value}",
							Severity = Severity.Error,
							AttemptedValue = value
						});
					}
				}

				// If it's a directory, check if it's not empty
				else {
					if (!fileSystem1.Directory.EnumerateFileSystemEntries(value).Any()) {
						context.AddFailure(new ValidationFailure {
							ErrorCode = "FILE004",
							ErrorMessage = $"Directory is empty: {value}",
							Severity = Severity.Error,
							AttemptedValue = value
						});
					}
				}
			});
	}

	#endregion
}

#endregion

#region Class: DownloadLibsCommandOptions

[Verb("download-configuration", Aliases = ["dconf"], HelpText = "Download libraries from web-application")]
public class DownloadConfigurationCommandOptions : EnvironmentOptions{
	#region Properties: Public

	[Option('b', "build", Required = false,
		HelpText = "Path to Creatio zip file or extracted directory to get configuration from")]
	public string BuildZipPath { get; set; }

	#endregion
}

#endregion

#region Class: DownloadLibsCommand

public class DownloadConfigurationCommand : Command<DownloadConfigurationCommandOptions>{
	#region Fields: Private

	private readonly IApplicationDownloader _applicationDownloader;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly Common.IFileSystem _fileSystem;
	private readonly ISettingsRepository _settingsRepository;
	private readonly ILogger _logger;
	private readonly IWorkspace _workspace;
	private readonly IZipBasedApplicationDownloader _zipBasedApplicationDownloader;

	#endregion

	#region Constructors: Public

	public DownloadConfigurationCommand(
		IApplicationDownloader applicationDownloader,
		IZipBasedApplicationDownloader zipBasedApplicationDownloader,
		IWorkspace workspace,
		ILogger logger, Common.IFileSystem fileSystem, ISettingsRepository settingsRepository) {
		applicationDownloader.CheckArgumentNull(nameof(applicationDownloader));
		zipBasedApplicationDownloader.CheckArgumentNull(nameof(zipBasedApplicationDownloader));
		workspace.CheckArgumentNull(nameof(workspace));
		_applicationDownloader = applicationDownloader;
		_zipBasedApplicationDownloader = zipBasedApplicationDownloader;
		_workspace = workspace;
		_logger = logger;
		_fileSystem = fileSystem;
		_settingsRepository = settingsRepository;
	}

	#endregion

	#region Methods: Private

	private int DownloadFromDefaultEnv() {
		if (Program.IsDebugMode) {
			_logger.WriteDebug($"DownloadConfigurationCommand: Using {ConsoleLogger.WrapRed("default environment")} mode");
		}
		_applicationDownloader.Download(_workspace.WorkspaceSettings.Packages);
		return 0;
	}

	private int DownloadFromNamedEnv(DownloadConfigurationCommandOptions options) {
		if (Program.IsDebugMode) {
			_logger.WriteDebug($"DownloadConfigurationCommand: Using {ConsoleLogger.WrapRed("named env mode")} with path={options.Environment}");
		}
		EnvironmentSettings env = _settingsRepository.FindEnvironment(options.Environment);
		if (!string.IsNullOrWhiteSpace(env?.EnvironmentPath) && _fileSystem.ExistsDirectory(env.EnvironmentPath)) {
			_zipBasedApplicationDownloader.DownloadFromPath(env.EnvironmentPath);
		}

		return 0;
	}

	private int DownloadFromPath(DownloadConfigurationCommandOptions options) {
		// Download from the zip file or directory (auto-detected)
		if (Program.IsDebugMode) {
			_logger.WriteDebug($"DownloadConfigurationCommand: Using {ConsoleLogger.WrapRed("build mode")} with path={options.BuildZipPath}");
		}
		_zipBasedApplicationDownloader.DownloadFromPath(options.BuildZipPath);
		return 0;
	}

	#endregion

	#region Methods: Public

	public override int Execute(DownloadConfigurationCommandOptions options) {
		try {
			// Download from the build zip file
			if (!string.IsNullOrWhiteSpace(options.BuildZipPath)) {
				return DownloadFromPath(options);
			}

			// Download from the environment in options
			if (!string.IsNullOrWhiteSpace(options.Environment)) {
				return DownloadFromNamedEnv(options);
			}
			return DownloadFromDefaultEnv();
		}
		catch (Exception ex) {
			_logger.WriteError(ex.Message);
			if (Program.IsDebugMode) {
				_logger.WriteError($"Stack trace: {ex.StackTrace}");
			}

			return 1;
		}
	}

	#endregion
}

#endregion
