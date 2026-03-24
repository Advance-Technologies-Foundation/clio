#pragma warning disable CLIO001 // Exception construction is not resolved from DI.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Builds Docker images for Creatio distributions.
/// </summary>
public interface IBuildDockerImageService {
	/// <summary>
	/// Builds a Docker image according to the supplied options.
	/// </summary>
	/// <param name="options">Command options.</param>
	/// <returns><c>0</c> on success; otherwise non-zero.</returns>
	int Execute(BuildDockerImageOptions options);
}

/// <summary>
/// Default implementation of <see cref="IBuildDockerImageService"/>.
/// </summary>
public sealed class BuildDockerImageService(
	IProcessExecutor processExecutor,
	ILogger logger,
	ISettingsRepository settingsRepository,
	Clio.Common.IFileSystem fileSystem,
	System.IO.Abstractions.IFileSystem msFileSystem,
	IZipFile zipFile,
	IDockerTemplatePathProvider dockerTemplatePathProvider)
	: IBuildDockerImageService {
	private const string DatabaseSourceLabelName = "org.creatio.database-source";
	private const string DockerCli = "docker";
	private const string NerdctlCli = "nerdctl";
	private const string NerdctlNamespace = "k8s.io";
	private const string SourceFolderName = "source";
	private const string TerrasoftWebHostDll = "Terrasoft.WebHost.dll";
	private const string TerrasoftWebHostConfig = "Terrasoft.WebHost.dll.config";
	private const string NetFrameworkConfig = "Web.config";

	private readonly IDockerTemplatePathProvider _dockerTemplatePathProvider =
		dockerTemplatePathProvider ?? throw new ArgumentNullException(nameof(dockerTemplatePathProvider));
	private readonly Clio.Common.IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
	private readonly System.IO.Abstractions.IFileSystem _msFileSystem =
		msFileSystem ?? throw new ArgumentNullException(nameof(msFileSystem));
	private readonly IProcessExecutor _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
	private readonly ISettingsRepository _settingsRepository =
		settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	private readonly IZipFile _zipFile = zipFile ?? throw new ArgumentNullException(nameof(zipFile));

	/// <inheritdoc />
	public int Execute(BuildDockerImageOptions options) {
		if (options is null) {
			throw new ArgumentNullException(nameof(options));
		}

		string stagingRoot = string.Empty;
		try {
			string sourcePath = ValidateSourcePath(options.SourcePath);
			DockerTemplateResolution templateResolution = _dockerTemplatePathProvider.ResolveTemplate(options.Template);
			string sourceLeafName = GetSourceLeafName(sourcePath);
			string templateName = templateResolution.Name;
			string imageRepository = $"creatio-{templateName}";
			string imageTag = SanitizeTag(sourceLeafName);
			string localImageReference = $"{imageRepository}:{imageTag}";
			string registryImageReference = BuildRegistryImageReference(options.Registry, imageRepository, imageTag);
			string outputTarPath = NormalizeOptionalOutputPath(options.OutputPath);
			ContainerImageCliKind containerImageCli = ResolveContainerImageCli(options);

			stagingRoot = CreateTemporaryDirectory("build-docker-image");
			string preparedSourceRoot = PrepareSourceRoot(sourcePath, stagingRoot);
			string buildContextPath = CreateBuildContext(preparedSourceRoot, templateResolution.TemplatePath, stagingRoot);

			_logger.WriteInfo($"Resolved source: {preparedSourceRoot}");
			_logger.WriteInfo($"Resolved template: {templateResolution.TemplatePath}");
			_logger.WriteInfo($"Container image CLI: {containerImageCli.ToCliName()}");
			_logger.WriteInfo($"Local image reference: {localImageReference}");

			ProcessExecutionResult versionResult = ExecuteContainerCli(containerImageCli, "--version", null);
			if (!WasSuccessful(versionResult)) {
				return LogAndReturnFailure(versionResult,
					$"Container image CLI '{containerImageCli.ToCliName()}' is not installed or not available in PATH.");
			}

			string databaseSourceLabel = BuildDockerLabel(DatabaseSourceLabelName, sourceLeafName);
			ProcessExecutionResult buildResult =
				ExecuteContainerCli(
					containerImageCli,
					$"build --label \"{databaseSourceLabel}\" -t \"{localImageReference}\" \"{buildContextPath}\"",
					buildContextPath);
			if (!WasSuccessful(buildResult)) {
				return LogAndReturnFailure(buildResult,
					$"Container image build failed for image '{localImageReference}' using '{containerImageCli.ToCliName()}'.");
			}

			_logger.WriteInfo($"Built Docker image: {localImageReference}");

			if (!string.IsNullOrWhiteSpace(outputTarPath)) {
				EnsureOutputDirectoryExists(outputTarPath);
				ProcessExecutionResult saveResult =
					ExecuteContainerCli(containerImageCli,
						$"save --output \"{outputTarPath}\" \"{localImageReference}\"", buildContextPath);
				if (!WasSuccessful(saveResult)) {
					return LogAndReturnFailure(saveResult,
						$"Container image save failed for image '{localImageReference}' to '{outputTarPath}'.");
				}

				_logger.WriteInfo($"Saved Docker image tar: {outputTarPath}");
			}

			if (!string.IsNullOrWhiteSpace(registryImageReference)) {
				ProcessExecutionResult tagResult =
					ExecuteContainerCli(containerImageCli,
						$"tag \"{localImageReference}\" \"{registryImageReference}\"", buildContextPath);
				if (!WasSuccessful(tagResult)) {
					return LogAndReturnFailure(tagResult,
						$"Container image tag failed for '{localImageReference}' to '{registryImageReference}'.");
				}

				ProcessExecutionResult pushResult =
					ExecuteContainerCli(containerImageCli, $"push \"{registryImageReference}\"", buildContextPath);
				if (!WasSuccessful(pushResult)) {
					return LogAndReturnFailure(pushResult,
						$"Container image push failed for image '{registryImageReference}'.");
				}

				_logger.WriteInfo($"Pushed Docker image: {registryImageReference}");
			}

			return 0;
		}
		catch (Exception exception) {
			_logger.WriteError(exception.Message);
			return 1;
		}
		finally {
			if (!string.IsNullOrWhiteSpace(stagingRoot)) {
				_fileSystem.DeleteDirectoryIfExists(stagingRoot);
			}
		}
	}

	private string BuildRegistryImageReference(string registry, string imageRepository, string imageTag) {
		if (string.IsNullOrWhiteSpace(registry)) {
			return string.Empty;
		}

		string trimmedRegistry = registry.Trim().TrimEnd('/');
		return $"{trimmedRegistry}/{imageRepository}:{imageTag}";
	}

	private string BuildDockerLabel(string labelName, string labelValue) {
		string escapedValue = labelValue.Replace("\"", "\\\"");
		return $"{labelName}={escapedValue}";
	}

	private string CreateBuildContext(string sourceRootPath, string templatePath, string stagingRoot) {
		string buildContextPath = _fileSystem.Combine(stagingRoot, "context");
		_fileSystem.CopyDirectory(templatePath, buildContextPath, true);
		string buildContextSourcePath = _fileSystem.Combine(buildContextPath, SourceFolderName);
		_fileSystem.CopyDirectory(sourceRootPath, buildContextSourcePath, true);
		return buildContextPath;
	}

	private string CreateTemporaryDirectory(string operationName) {
		string tempRoot = _msFileSystem.Path.Combine(
			_msFileSystem.Path.GetTempPath(),
			"clio",
			operationName,
			Guid.NewGuid().ToString("N"));
		_fileSystem.CreateDirectoryIfNotExists(tempRoot);
		return tempRoot;
	}

	private void EnsureOutputDirectoryExists(string outputTarPath) {
		string outputDirectory = _msFileSystem.Path.GetDirectoryName(outputTarPath) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(outputDirectory)) {
			return;
		}

		_fileSystem.CreateDirectoryIfNotExists(outputDirectory);
	}

	private ProcessExecutionResult ExecuteContainerCli(
		ContainerImageCliKind containerImageCli,
		string arguments,
		string workingDirectory) {
		string effectiveArguments = containerImageCli == ContainerImageCliKind.Nerdctl
			? $"--namespace {NerdctlNamespace} {arguments}"
			: arguments;
		ProcessExecutionOptions executionOptions = new(containerImageCli.ToCliName(), effectiveArguments) {
			WorkingDirectory = workingDirectory,
			MirrorOutputToLogger = false,
			OnOutput = (line, _) => _logger.WriteLine(line)
		};

		return _processExecutor.ExecuteWithRealtimeOutputAsync(executionOptions).GetAwaiter().GetResult();
	}

	private ContainerImageCliKind ResolveContainerImageCli(BuildDockerImageOptions options) {
		if (options.UseDocker && options.UseNerdctl) {
			throw new InvalidOperationException("Use either --use-docker or --use-nerdctl, but not both.");
		}

		if (options.UseDocker) {
			return ContainerImageCliKind.Docker;
		}

		if (options.UseNerdctl) {
			return ContainerImageCliKind.Nerdctl;
		}

		string configuredCli = _settingsRepository.GetContainerImageCli();
		if (string.Equals(configuredCli, DockerCli, StringComparison.OrdinalIgnoreCase)) {
			return ContainerImageCliKind.Docker;
		}

		if (string.Equals(configuredCli, NerdctlCli, StringComparison.OrdinalIgnoreCase)) {
			return ContainerImageCliKind.Nerdctl;
		}

		throw new InvalidOperationException(
			$"Unsupported container-image-cli setting '{configuredCli}'. Allowed values are '{DockerCli}' and '{NerdctlCli}'.");
	}

	private string GetSourceLeafName(string sourcePath) {
		if (_fileSystem.ExistsDirectory(sourcePath)) {
			return _msFileSystem.Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
		}

		return _msFileSystem.Path.GetFileNameWithoutExtension(sourcePath);
	}

	private int LogAndReturnFailure(ProcessExecutionResult result, string fallbackMessage) {
		string message = string.IsNullOrWhiteSpace(result.StandardError)
			? fallbackMessage
			: $"{fallbackMessage}{Environment.NewLine}{result.StandardError}";
		_logger.WriteError(message);
		return result.ExitCode.GetValueOrDefault(1) == 0 ? 1 : result.ExitCode.GetValueOrDefault(1);
	}

	private string NormalizeOptionalOutputPath(string outputPath) {
		if (string.IsNullOrWhiteSpace(outputPath)) {
			return string.Empty;
		}

		return _fileSystem.GetFullPath(outputPath);
	}

	private string PrepareSourceRoot(string sourcePath, string stagingRoot) {
		if (_fileSystem.ExistsDirectory(sourcePath)) {
			string sourceRoot = ResolveApplicationRoot(sourcePath);
			ValidateDotNetPayload(sourceRoot);
			return sourceRoot;
		}

		string extractedPath = _fileSystem.Combine(stagingRoot, "extracted");
		_fileSystem.CreateDirectoryIfNotExists(extractedPath);
		_zipFile.ExtractToDirectory(sourcePath, extractedPath);

		string extractedSourceRoot = ResolveApplicationRoot(extractedPath);
		ValidateDotNetPayload(extractedSourceRoot);
		return extractedSourceRoot;
	}

	private string ResolveApplicationRoot(string rootPath) {
		string normalizedRootPath = _fileSystem.GetFullPath(rootPath);
		if (LooksLikeApplicationRoot(normalizedRootPath)) {
			return normalizedRootPath;
		}

		string[] webHostCandidates =
			_fileSystem.GetFiles(normalizedRootPath, TerrasoftWebHostDll, SearchOption.AllDirectories);
		if (webHostCandidates.Length == 1) {
			return _msFileSystem.Path.GetDirectoryName(webHostCandidates[0]) ?? normalizedRootPath;
		}

		string[] netFrameworkCandidates =
			_fileSystem.GetFiles(normalizedRootPath, NetFrameworkConfig, SearchOption.AllDirectories);
		if (netFrameworkCandidates.Length == 1) {
			return _msFileSystem.Path.GetDirectoryName(netFrameworkCandidates[0]) ?? normalizedRootPath;
		}

		throw new InvalidOperationException(
			$"Could not detect a Creatio application root under '{normalizedRootPath}'.");
	}

	private string SanitizeTag(string sourceLeafName) {
		string sanitized = Regex.Replace(sourceLeafName.ToLowerInvariant(), @"[^a-z0-9._-]+", "_");
		string trimmed = sanitized.Trim('_');
		return string.IsNullOrWhiteSpace(trimmed) ? "latest" : trimmed;
	}

	private string ValidateSourcePath(string sourcePath) {
		if (string.IsNullOrWhiteSpace(sourcePath)) {
			throw new ArgumentException("Source path is required.", nameof(sourcePath));
		}

		string fullSourcePath = _fileSystem.GetFullPath(sourcePath);
		if (_fileSystem.ExistsDirectory(fullSourcePath)) {
			return fullSourcePath;
		}

		if (_fileSystem.ExistsFile(fullSourcePath)
			&& _msFileSystem.Path.GetExtension(fullSourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase)) {
			return fullSourcePath;
		}

		if (_fileSystem.ExistsFile(fullSourcePath)) {
			throw new InvalidOperationException(
				$"Unsupported source '{fullSourcePath}'. Only ZIP archives and directories are supported.");
		}

		throw new InvalidOperationException($"Source '{fullSourcePath}' was not found.");
	}

	private void ValidateDotNetPayload(string applicationRoot) {
		string webHostDllPath = _fileSystem.Combine(applicationRoot, TerrasoftWebHostDll);
		string webHostConfigPath = _fileSystem.Combine(applicationRoot, TerrasoftWebHostConfig);
		if (_fileSystem.ExistsFile(webHostDllPath) && _fileSystem.ExistsFile(webHostConfigPath)) {
			return;
		}

		string netFrameworkConfigPath = _fileSystem.Combine(applicationRoot, NetFrameworkConfig);
		if (_fileSystem.ExistsFile(netFrameworkConfigPath) && !_fileSystem.ExistsFile(webHostDllPath)) {
			throw new InvalidOperationException(
				$"Source '{applicationRoot}' looks like a .NET Framework Creatio distribution. Docker image builds support only .NET 8+ distributions.");
		}

		throw new InvalidOperationException(
			$"Source '{applicationRoot}' does not look like a supported Creatio .NET 8+ distribution.");
	}

	private bool LooksLikeApplicationRoot(string rootPath) {
		string webHostDllPath = _fileSystem.Combine(rootPath, TerrasoftWebHostDll);
		string webHostConfigPath = _fileSystem.Combine(rootPath, TerrasoftWebHostConfig);
		string netFrameworkConfigPath = _fileSystem.Combine(rootPath, NetFrameworkConfig);
		return (_fileSystem.ExistsFile(webHostDllPath) && _fileSystem.ExistsFile(webHostConfigPath))
			|| _fileSystem.ExistsFile(netFrameworkConfigPath);
	}

	private bool WasSuccessful(ProcessExecutionResult result) {
		return result.Started && result.ExitCode.GetValueOrDefault(-1) == 0;
	}
}

/// <summary>
/// Identifies the container image CLI used by build-docker-image.
/// </summary>
public enum ContainerImageCliKind {
	/// <summary>
	/// Use Docker.
	/// </summary>
	Docker,

	/// <summary>
	/// Use nerdctl with the Kubernetes namespace.
	/// </summary>
	Nerdctl
}

internal static class ContainerImageCliKindExtensions {
	public static string ToCliName(this ContainerImageCliKind containerImageCli) {
		return containerImageCli == ContainerImageCliKind.Nerdctl ? "nerdctl" : "docker";
	}
}

#pragma warning restore CLIO001 // Exception construction is not resolved from DI.
