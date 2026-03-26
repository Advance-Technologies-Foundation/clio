#pragma warning disable CLIO001 // Exception construction is not resolved from DI.

using System;
using System.Collections.Generic;
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
	ICodeServerArchiveCache codeServerArchiveCache,
	IContainerRegistryPreflightService containerRegistryPreflightService,
	ILogger logger,
	ISettingsRepository settingsRepository,
	Clio.Common.IFileSystem fileSystem,
	System.IO.Abstractions.IFileSystem msFileSystem,
	IZipFile zipFile,
	IDockerTemplatePathProvider dockerTemplatePathProvider)
	: IBuildDockerImageService {
	private const string BaseBundledTemplateName = "base";
	private const string DbBundledTemplateName = "db";
	private const string BaseImageBuildArgumentName = "BASE_IMAGE";
	private const string BundledBaseImageArchiveExtension = ".tar";
	private const string BundledBaseImageArchiveFolderName = "docker-image-cache";
	private const string DefaultBundledBaseImageReference = "creatio-base:8.0-v1";
	private const string DatabaseSourceLabelName = "org.creatio.database-source";
	private const string DbCapabilitySourceLabelName = "org.creatio.capability.db-source";
	private const string BuildContextDockerIgnoreFileName = ".dockerignore";
	private const string BuildWithoutPullFlag = "--pull=false";
	private const string DatabaseDirectoryName = "db";
	private const string BuildkitHostEnvironmentVariable = "BUILDKIT_HOST";
	private const string BuildkitNerdctlNamespace = "buildkit";
	private const string ContainerdNamespaceEnvironmentVariable = "CONTAINERD_NAMESPACE";
	private const string DockerCli = "docker";
	private const string NerdctlCli = "nerdctl";
	private const string NerdctlNamespace = "k8s.io";
	private const string ShellScriptPattern = "*.sh";
	private const string SourceFolderName = "source";
	private const string TerrasoftWebHostDll = "Terrasoft.WebHost.dll";
	private const string TerrasoftWebHostConfig = "Terrasoft.WebHost.dll.config";
	private const string NetFrameworkConfig = "Web.config";

	private readonly ICodeServerArchiveCache _codeServerArchiveCache =
		codeServerArchiveCache ?? throw new ArgumentNullException(nameof(codeServerArchiveCache));
	private readonly IContainerRegistryPreflightService _containerRegistryPreflightService =
		containerRegistryPreflightService ?? throw new ArgumentNullException(nameof(containerRegistryPreflightService));
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
			DockerTemplateResolution templateResolution = _dockerTemplatePathProvider.ResolveTemplate(options.Template);
			bool isBaseTemplate = IsBaseTemplate(templateResolution);
			string sourcePath = isBaseTemplate ? string.Empty : ValidateSourcePath(options.SourcePath);
			string sourceLeafName = isBaseTemplate ? string.Empty : GetSourceLeafName(sourcePath);
			string localImageReference = ResolveLocalImageReference(templateResolution, sourceLeafName, options);
			string registryImageReference = BuildRegistryImageReference(options.Registry, localImageReference);
			string outputTarPath = NormalizeOptionalOutputPath(options.OutputPath);

			stagingRoot = CreateTemporaryDirectory("build-docker-image");
			_logger.WriteInfo($"Created temp directory: {stagingRoot}");

			string preparedSourceRoot = isBaseTemplate ? string.Empty : PrepareSourceRoot(sourcePath, stagingRoot, templateResolution);
			ContainerImageCliKind containerImageCli = ResolveContainerImageCli(options);
			ProcessExecutionResult versionResult = ExecuteContainerCli(containerImageCli, "--version", null);
			if (!WasSuccessful(versionResult)) {
				return LogAndReturnFailure(versionResult,
					$"Container image CLI '{containerImageCli.ToCliName()}' is not installed or not available in PATH.");
			}

			int baseImageValidationResult = ValidateBaseImageAvailability(containerImageCli, templateResolution, options);
			if (baseImageValidationResult != 0) {
				return baseImageValidationResult;
			}

			int registryPreflightResult =
				ValidateRegistryPushTarget(containerImageCli, registryImageReference, options.Registry);
			if (registryPreflightResult != 0) {
				return registryPreflightResult;
			}

			string buildContextPath = CreateBuildContext(
				preparedSourceRoot,
				templateResolution,
				stagingRoot,
				options,
				isBaseTemplate);
			string buildArguments = BuildImageBuildArguments(
				containerImageCli,
				templateResolution,
				sourceLeafName,
				localImageReference,
				options);

			if (!isBaseTemplate) {
				_logger.WriteInfo($"Resolved source: {preparedSourceRoot}");
			}

			_logger.WriteInfo($"Resolved template: {templateResolution.TemplatePath}");
			_logger.WriteInfo($"Container image CLI: {containerImageCli.ToCliName()}");
			_logger.WriteInfo($"Local image reference: {localImageReference}");
			if (isBaseTemplate) {
				_logger.WriteInfo($"Building base image: {localImageReference}");
			}

			ProcessExecutionResult buildResult = ExecuteContainerCli(containerImageCli, buildArguments, buildContextPath);
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

			CacheBundledBaseImageArchive(containerImageCli, templateResolution, localImageReference, buildContextPath);

			if (!string.IsNullOrWhiteSpace(registryImageReference)) {
				_logger.WriteInfo($"Tagging Docker image for registry push: {registryImageReference}");
				ProcessExecutionResult tagResult =
					ExecuteContainerCli(containerImageCli,
						$"tag \"{localImageReference}\" \"{registryImageReference}\"", buildContextPath);
				if (!WasSuccessful(tagResult)) {
					return LogAndReturnFailure(tagResult,
						$"Container image tag failed for '{localImageReference}' to '{registryImageReference}'.");
				}

				_logger.WriteInfo(
					$"Pushing Docker image to registry: {registryImageReference}. Push output can stay quiet if the container CLI does not emit line-based progress.");
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
				TryDeleteDirectoryIfExists(stagingRoot, "temporary Docker build context");
			}
		}
	}

	private string AppendDockerIgnoreEntry(string dockerIgnoreContent, string entry) {
		foreach (string line in dockerIgnoreContent.Split('\n', StringSplitOptions.None)) {
			if (string.Equals(line.Trim(), entry, StringComparison.Ordinal)) {
				return dockerIgnoreContent;
			}
		}

		if (string.IsNullOrEmpty(dockerIgnoreContent)) {
			return $"{entry}\n";
		}

		string suffix = dockerIgnoreContent.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n";
		return $"{dockerIgnoreContent}{suffix}{entry}\n";
	}

	private string BuildDockerLabel(string labelName, string labelValue) {
		string escapedValue = labelValue.Replace("\"", "\\\"");
		return $"{labelName}={escapedValue}";
	}

	private string BuildImageBuildArguments(
		ContainerImageCliKind containerImageCli,
		DockerTemplateResolution templateResolution,
		string sourceLeafName,
		string localImageReference,
		BuildDockerImageOptions options) {
		string baseImageBuildArgument = BuildTemplateBaseImageBuildArgument(containerImageCli, templateResolution, options);
		string buildkitHostArgument = BuildNerdctlBuildkitHostArgument(containerImageCli);
		string imageLabelArguments = BuildTemplateImageLabelArguments(templateResolution, sourceLeafName);
		return $"build {BuildWithoutPullFlag}{buildkitHostArgument}{baseImageBuildArgument}{imageLabelArguments} -t \"{localImageReference}\" \".\"";
	}

	private string BuildTemplateImageLabelArguments(DockerTemplateResolution templateResolution, string sourceLeafName) {
		if (string.IsNullOrWhiteSpace(sourceLeafName) || IsBaseTemplate(templateResolution)) {
			return string.Empty;
		}

		if (IsDbTemplate(templateResolution)) {
			return $" --label \"{BuildDockerLabel(DbCapabilitySourceLabelName, sourceLeafName)}\"";
		}

		return $" --label \"{BuildDockerLabel(DatabaseSourceLabelName, sourceLeafName)}\"";
	}

	private string BuildNerdctlBuildkitHostArgument(ContainerImageCliKind containerImageCli) {
		if (containerImageCli != ContainerImageCliKind.Nerdctl) {
			return string.Empty;
		}

		string buildkitHost = ResolveNerdctlBuildkitHost();
		if (string.IsNullOrWhiteSpace(buildkitHost)) {
			return string.Empty;
		}

		_logger.WriteInfo($"Using nerdctl BuildKit host: {buildkitHost}");
		return $" --buildkit-host \"{buildkitHost}\"";
	}

	private string BuildRegistryImageReference(string registry, string localImageReference) {
		if (string.IsNullOrWhiteSpace(registry)) {
			return string.Empty;
		}

		if (IsRegistryQualifiedImageReference(localImageReference)) {
			return localImageReference;
		}

		string trimmedRegistry = registry.Trim().TrimEnd('/');
		return $"{trimmedRegistry}/{localImageReference}";
	}

	private string BuildTemplateBaseImageBuildArgument(
		ContainerImageCliKind containerImageCli,
		DockerTemplateResolution templateResolution,
		BuildDockerImageOptions options) {
		if (!ShouldInjectBundledBaseImageBuildArgument(templateResolution, containerImageCli)) {
			return string.Empty;
		}

		string baseImageReference = ResolveBaseImageReference(options);
		return $" --build-arg {BaseImageBuildArgumentName}=\"{baseImageReference}\"";
	}

	private string CreateBuildContext(
		string sourceRootPath,
		DockerTemplateResolution templateResolution,
		string stagingRoot,
		BuildDockerImageOptions options,
		bool isBaseTemplate) {
		string buildContextPath = _fileSystem.Combine(stagingRoot, "context");
		_fileSystem.CopyDirectory(templateResolution.TemplatePath, buildContextPath, true);
		if (IsDbTemplate(templateResolution)) {
			string buildContextDatabasePath = _fileSystem.Combine(buildContextPath, DatabaseDirectoryName);
			_fileSystem.CopyDirectory(sourceRootPath, buildContextDatabasePath, true);
		}
		else {
			RemoveDatabaseDirectory(buildContextPath);
		}

		if (!isBaseTemplate && !IsDbTemplate(templateResolution)) {
			EnsureDockerIgnoreExcludesDatabase(buildContextPath);
			string buildContextSourcePath = _fileSystem.Combine(buildContextPath, SourceFolderName);
			_fileSystem.CopyDirectory(sourceRootPath, buildContextSourcePath, true);
			RemoveDatabaseDirectory(buildContextSourcePath);
		}

		StageBundledDevAssets(buildContextPath, templateResolution, options);
		NormalizeShellScriptsToLf(buildContextPath);
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

	private void EnsureDockerIgnoreExcludesDatabase(string buildContextPath) {
		string dockerIgnorePath = _fileSystem.Combine(buildContextPath, BuildContextDockerIgnoreFileName);
		string dockerIgnoreContent = _fileSystem.ExistsFile(dockerIgnorePath)
			? _fileSystem.ReadAllText(dockerIgnorePath)
			: string.Empty;
		string normalizedContent = dockerIgnoreContent
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\r", "\n", StringComparison.Ordinal);
		string updatedContent = AppendDockerIgnoreEntry(normalizedContent, DatabaseDirectoryName);
		updatedContent = AppendDockerIgnoreEntry(updatedContent, $"{SourceFolderName}/{DatabaseDirectoryName}");

		if (!_fileSystem.ExistsFile(dockerIgnorePath)
			|| !string.Equals(dockerIgnoreContent, updatedContent, StringComparison.Ordinal)) {
			_fileSystem.WriteAllTextToFile(dockerIgnorePath, updatedContent);
		}
	}

	private void EnsureOutputDirectoryExists(string outputTarPath) {
		string outputDirectory = _msFileSystem.Path.GetDirectoryName(outputTarPath) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(outputDirectory)) {
			return;
		}

		_fileSystem.CreateDirectoryIfNotExists(outputDirectory);
	}

	private void CacheBundledBaseImageArchive(
		ContainerImageCliKind containerImageCli,
		DockerTemplateResolution templateResolution,
		string localImageReference,
		string workingDirectory) {
		if (!templateResolution.IsBundled || !IsBaseTemplate(templateResolution)) {
			return;
		}

		string archivePath = BuildBundledBaseImageArchivePath(localImageReference);
		EnsureOutputDirectoryExists(archivePath);
		ProcessExecutionResult saveResult =
			ExecuteContainerCli(containerImageCli, $"save --output \"{archivePath}\" \"{localImageReference}\"", workingDirectory);
		if (!WasSuccessful(saveResult)) {
			LogAndReturnFailure(saveResult,
				$"Failed to cache base image '{localImageReference}' to '{archivePath}'.");
			throw new InvalidOperationException(
				$"Failed to cache base image '{localImageReference}'.");
		}

		_logger.WriteInfo($"Cached base image archive: {archivePath}");
		if (containerImageCli == ContainerImageCliKind.Nerdctl) {
			EnsureImageAvailableInNerdctlBuildkitNamespace(localImageReference, archivePath);
		}
	}

	private ProcessExecutionResult ExecuteContainerCli(
		ContainerImageCliKind containerImageCli,
		string arguments,
		string workingDirectory,
		string nerdctlNamespaceOverride = null) {
		string nerdctlNamespace = nerdctlNamespaceOverride ?? NerdctlNamespace;
		string effectiveArguments = containerImageCli == ContainerImageCliKind.Nerdctl
			? $"--namespace {nerdctlNamespace} {arguments}"
			: arguments;
		ProcessExecutionOptions executionOptions = new(containerImageCli.ToCliName(), effectiveArguments) {
			WorkingDirectory = workingDirectory,
			MirrorOutputToLogger = false,
			OnOutput = (line, _) => _logger.WriteLine(line),
			EnvironmentVariables = ResolveContainerCliEnvironmentVariables(containerImageCli, nerdctlNamespace)
		};

		return _processExecutor.ExecuteWithRealtimeOutputAsync(executionOptions).GetAwaiter().GetResult();
	}

	private IReadOnlyDictionary<string, string> ResolveContainerCliEnvironmentVariables(
		ContainerImageCliKind containerImageCli,
		string nerdctlNamespace) {
		if (containerImageCli != ContainerImageCliKind.Nerdctl) {
			return null;
		}

		return new System.Collections.Generic.Dictionary<string, string> {
			[ContainerdNamespaceEnvironmentVariable] = nerdctlNamespace
		};
	}

	private string GetSourceLeafName(string sourcePath) {
		if (_fileSystem.ExistsDirectory(sourcePath)) {
			return _msFileSystem.Path.GetFileName(sourcePath.TrimEnd('/', '\\'));
		}

		return _msFileSystem.Path.GetFileNameWithoutExtension(sourcePath);
	}

	private bool IsBaseTemplate(DockerTemplateResolution templateResolution) {
		return string.Equals(templateResolution.Name, BaseBundledTemplateName, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsDbTemplate(DockerTemplateResolution templateResolution) {
		return string.Equals(templateResolution.Name, DbBundledTemplateName, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsRegistryQualifiedImageReference(string imageReference) {
		if (string.IsNullOrWhiteSpace(imageReference)) {
			return false;
		}

		string repositoryPart = imageReference.Split(':')[0];
		int slashIndex = repositoryPart.IndexOf('/');
		if (slashIndex <= 0) {
			return false;
		}

		string firstSegment = repositoryPart[..slashIndex];
		return firstSegment.Contains('.', StringComparison.Ordinal)
			|| firstSegment.Contains(':', StringComparison.Ordinal)
			|| string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase);
	}

	private string BuildBundledBaseImageArchivePath(string imageReference) {
		string settingsDirectory = _msFileSystem.Path.GetDirectoryName(_settingsRepository.AppSettingsFilePath) ?? string.Empty;
		string cacheDirectory = _fileSystem.Combine(settingsDirectory, BundledBaseImageArchiveFolderName);
		string archiveFileName = $"{SanitizeTag(imageReference)}{BundledBaseImageArchiveExtension}";
		return _fileSystem.Combine(cacheDirectory, archiveFileName);
	}

	private int LogAndReturnFailure(ProcessExecutionResult result, string fallbackMessage) {
		string message = string.IsNullOrWhiteSpace(result.StandardError)
			? fallbackMessage
			: $"{fallbackMessage}{Environment.NewLine}{result.StandardError}";
		_logger.WriteError(message);
		return result.ExitCode.GetValueOrDefault(1) == 0 ? 1 : result.ExitCode.GetValueOrDefault(1);
	}

	private void NormalizeShellScriptsToLf(string buildContextPath) {
		foreach (string shellScriptPath in _fileSystem.GetFiles(buildContextPath, ShellScriptPattern, SearchOption.AllDirectories)) {
			string scriptContents = _fileSystem.ReadAllText(shellScriptPath);
			string normalizedContents = scriptContents
				.Replace("\r\n", "\n", StringComparison.Ordinal)
				.Replace("\r", "\n", StringComparison.Ordinal);
			if (!string.Equals(scriptContents, normalizedContents, StringComparison.Ordinal)) {
				_fileSystem.WriteAllTextToFile(shellScriptPath, normalizedContents);
			}
		}
	}

	private string NormalizeOptionalOutputPath(string outputPath) {
		if (string.IsNullOrWhiteSpace(outputPath)) {
			return string.Empty;
		}

		return _fileSystem.GetFullPath(outputPath);
	}

	private string PrepareSourceRoot(string sourcePath, string stagingRoot, DockerTemplateResolution templateResolution) {
		if (IsDbTemplate(templateResolution)) {
			return PrepareDatabaseSourceRoot(sourcePath, stagingRoot);
		}

		if (_fileSystem.ExistsDirectory(sourcePath)) {
			string sourceRoot = ResolveApplicationRoot(sourcePath);
			ValidateDotNetPayload(sourceRoot);
			return sourceRoot;
		}

		string extractedPath = _fileSystem.Combine(stagingRoot, "extracted");
		_fileSystem.CreateDirectoryIfNotExists(extractedPath);

		_logger.WriteInfo($"Extracting source ZIP to: {extractedPath}");
		_zipFile.ExtractToDirectory(sourcePath, extractedPath);

		string extractedSourceRoot = ResolveApplicationRoot(extractedPath);
		RemoveDatabaseDirectory(extractedPath);
		RemoveDatabaseDirectory(extractedSourceRoot);
		ValidateDotNetPayload(extractedSourceRoot);
		return extractedSourceRoot;
	}

	private string PrepareDatabaseSourceRoot(string sourcePath, string stagingRoot) {
		if (_fileSystem.ExistsDirectory(sourcePath)) {
			return ResolveDatabasePayloadRoot(sourcePath);
		}

		string extractedPath = _fileSystem.Combine(stagingRoot, "extracted");
		_fileSystem.CreateDirectoryIfNotExists(extractedPath);

		_logger.WriteInfo($"Extracting source ZIP to: {extractedPath}");
		_zipFile.ExtractToDirectory(sourcePath, extractedPath);
		return ResolveDatabasePayloadRoot(extractedPath);
	}

	private void RemoveDatabaseDirectory(string rootPath) {
		string databaseDirectoryPath = _fileSystem.Combine(rootPath, DatabaseDirectoryName);
		_fileSystem.DeleteDirectoryIfExists(databaseDirectoryPath);
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

		ProcessExecutionResult dockerInfoResult = ExecuteContainerCli(ContainerImageCliKind.Docker, "info", null);
		if (WasSuccessful(dockerInfoResult)) {
			return ContainerImageCliKind.Docker;
		}

		ProcessExecutionResult nerdctlInfoResult = ExecuteContainerCli(ContainerImageCliKind.Nerdctl, "info", null);
		if (WasSuccessful(nerdctlInfoResult)) {
			return ContainerImageCliKind.Nerdctl;
		}

		throw new InvalidOperationException(
			$"Could not detect an available container image CLI. 'docker info' and 'nerdctl info' both failed. Install or start Docker or nerdctl, or use '--use-docker'/'--use-nerdctl' after the selected CLI becomes available.");
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

	private string ResolveDatabasePayloadRoot(string rootPath) {
		string normalizedRootPath = _fileSystem.GetFullPath(rootPath);
		if (string.Equals(_msFileSystem.Path.GetFileName(normalizedRootPath.TrimEnd('/', '\\')),
				DatabaseDirectoryName,
				StringComparison.OrdinalIgnoreCase)) {
			return normalizedRootPath;
		}

		string directDatabasePath = _fileSystem.Combine(normalizedRootPath, DatabaseDirectoryName);
		if (_fileSystem.ExistsDirectory(directDatabasePath)) {
			return directDatabasePath;
		}

		string[] nestedCandidates =
			_fileSystem.GetDirectories(normalizedRootPath, DatabaseDirectoryName, SearchOption.AllDirectories);
		if (nestedCandidates.Length == 1) {
			return nestedCandidates[0];
		}

		throw new InvalidOperationException(
			$"Source '{normalizedRootPath}' does not contain a '{DatabaseDirectoryName}' directory required by the bundled 'db' template.");
	}

	private string ResolveBaseImageReference(BuildDockerImageOptions options) {
		return string.IsNullOrWhiteSpace(options.BaseImage)
			? DefaultBundledBaseImageReference
			: options.BaseImage.Trim();
	}

	private string ResolveNerdctlBuildkitHost() {
		string configuredBuildkitHost = Environment.GetEnvironmentVariable(BuildkitHostEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(configuredBuildkitHost)) {
			return configuredBuildkitHost.Trim();
		}

		string xdgRuntimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
		if (string.IsNullOrWhiteSpace(xdgRuntimeDirectory)) {
			return string.Empty;
		}

		string[] candidateSocketPaths = [
			_msFileSystem.Path.Combine(xdgRuntimeDirectory, $"buildkit-{NerdctlNamespace}", "buildkitd.sock"),
			_msFileSystem.Path.Combine(xdgRuntimeDirectory, "buildkit-default", "buildkitd.sock"),
			_msFileSystem.Path.Combine(xdgRuntimeDirectory, "buildkit", "buildkitd.sock")
		];

		foreach (string candidateSocketPath in candidateSocketPaths) {
			if (_fileSystem.ExistsFile(candidateSocketPath)) {
				return $"unix://{candidateSocketPath.Replace('\\', '/')}";
			}
		}

		return string.Empty;
	}

	private string ResolveBundledTemplateSourceImageReference(
		DockerTemplateResolution templateResolution,
		BuildDockerImageOptions options,
		string buildContextPath) {
		if (ShouldUseBundledBaseImage(templateResolution)) {
			return ResolveBaseImageReference(options);
		}

		if (templateResolution.IsBundled && IsBaseTemplate(templateResolution)) {
			string dockerfilePath = _fileSystem.Combine(buildContextPath, "Dockerfile");
			string dockerfileContents = _fileSystem.ReadAllText(dockerfilePath);
			Match fromMatch = Regex.Match(dockerfileContents, @"(?im)^\s*FROM\s+(?<image>[^\s]+)");
			if (!fromMatch.Success) {
				throw new InvalidOperationException(
					$"Could not determine the bundled base template source image from '{dockerfilePath}'.");
			}

			return fromMatch.Groups["image"].Value.Trim();
		}

		return string.Empty;
	}

	private string ResolveLocalImageReference(
		DockerTemplateResolution templateResolution,
		string sourceLeafName,
		BuildDockerImageOptions options) {
		if (IsBaseTemplate(templateResolution)) {
			return ResolveBaseImageReference(options);
		}

		string imageRepository = $"creatio-{templateResolution.Name}";
		string imageTag = SanitizeTag(sourceLeafName);
		return $"{imageRepository}:{imageTag}";
	}

	private string SanitizeTag(string sourceLeafName) {
		string sanitized = Regex.Replace(sourceLeafName.ToLowerInvariant(), @"[^a-z0-9._-]+", "_");
		string trimmed = sanitized.Trim('_');
		return string.IsNullOrWhiteSpace(trimmed) ? "latest" : trimmed;
	}

	private bool ShouldInjectBundledBaseImageBuildArgument(
		DockerTemplateResolution templateResolution,
		ContainerImageCliKind containerImageCli) {
		return ShouldUseBundledBaseImage(templateResolution);
	}

	private bool ShouldUseBundledBaseImage(DockerTemplateResolution templateResolution) {
		return templateResolution.IsBundled
			&& (string.Equals(templateResolution.Name, "dev", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(templateResolution.Name, "prod", StringComparison.OrdinalIgnoreCase));
	}

	private bool TryRestoreCachedBaseImageArchive(
		ContainerImageCliKind containerImageCli,
		string imageReference) {
		string archivePath = BuildBundledBaseImageArchivePath(imageReference);
		if (!_fileSystem.ExistsFile(archivePath)) {
			return false;
		}

		_logger.WriteInfo($"Restoring cached base image archive: {archivePath}");
		ProcessExecutionResult loadResult = ExecuteContainerCli(containerImageCli, $"load --input \"{archivePath}\"", null);
		if (!WasSuccessful(loadResult)) {
			LogAndReturnFailure(loadResult,
				$"Failed to restore cached base image archive '{archivePath}' for '{imageReference}'.");
			return false;
		}

		return true;
	}

	private bool EnsureImageAvailableInNerdctlBuildkitNamespace(
		string imageReference,
		string preferredArchivePath = null) {
		ProcessExecutionResult buildkitInspectResult =
			ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"image inspect \"{imageReference}\"", null, BuildkitNerdctlNamespace);
		if (WasSuccessful(buildkitInspectResult)) {
			return true;
		}

		string archivePath = !string.IsNullOrWhiteSpace(preferredArchivePath) && _fileSystem.ExistsFile(preferredArchivePath)
			? preferredArchivePath
			: BuildBundledBaseImageArchivePath(imageReference);

		if (_fileSystem.ExistsFile(archivePath)) {
			_logger.WriteInfo(
				$"Syncing image '{imageReference}' into nerdctl BuildKit namespace '{BuildkitNerdctlNamespace}' from archive '{archivePath}'.");
			ProcessExecutionResult loadCachedArchiveResult =
				ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"load --input \"{archivePath}\"", null, BuildkitNerdctlNamespace);
			if (WasSuccessful(loadCachedArchiveResult)) {
				ProcessExecutionResult restoredInspectResult =
					ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"image inspect \"{imageReference}\"", null, BuildkitNerdctlNamespace);
				return WasSuccessful(restoredInspectResult);
			}
		}

		string temporaryArchiveDirectory = CreateTemporaryDirectory("nerdctl-buildkit-sync");
		string temporaryArchivePath = _fileSystem.Combine(temporaryArchiveDirectory, $"{SanitizeTag(imageReference)}.tar");
		try {
			_logger.WriteInfo(
				$"Syncing image '{imageReference}' into nerdctl BuildKit namespace '{BuildkitNerdctlNamespace}' from namespace '{NerdctlNamespace}'.");
			ProcessExecutionResult saveResult =
				ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"save --output \"{temporaryArchivePath}\" \"{imageReference}\"", null);
			if (!WasSuccessful(saveResult)) {
				LogAndReturnFailure(saveResult,
					$"Failed to stage image '{imageReference}' for the nerdctl BuildKit namespace.");
				return false;
			}

			ProcessExecutionResult loadResult =
				ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"load --input \"{temporaryArchivePath}\"", null, BuildkitNerdctlNamespace);
			if (!WasSuccessful(loadResult)) {
				LogAndReturnFailure(loadResult,
					$"Failed to load image '{imageReference}' into nerdctl BuildKit namespace '{BuildkitNerdctlNamespace}'.");
				return false;
			}

			ProcessExecutionResult syncedInspectResult =
				ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"image inspect \"{imageReference}\"", null, BuildkitNerdctlNamespace);
			return WasSuccessful(syncedInspectResult);
		}
		finally {
			TryDeleteDirectoryIfExists(temporaryArchiveDirectory, "temporary nerdctl BuildKit sync archive");
		}
	}

	private ProcessExecutionResult InspectImageForBuild(
		ContainerImageCliKind containerImageCli,
		string imageReference,
		out string resolvedNerdctlNamespace) {
		resolvedNerdctlNamespace = string.Empty;
		if (containerImageCli != ContainerImageCliKind.Nerdctl) {
			return ExecuteContainerCli(containerImageCli, $"image inspect \"{imageReference}\"", null);
		}

		ProcessExecutionResult defaultNamespaceInspectResult =
			ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"image inspect \"{imageReference}\"", null);
		if (WasSuccessful(defaultNamespaceInspectResult)) {
			resolvedNerdctlNamespace = NerdctlNamespace;
			return defaultNamespaceInspectResult;
		}

		ProcessExecutionResult buildkitNamespaceInspectResult =
			ExecuteContainerCli(ContainerImageCliKind.Nerdctl, $"image inspect \"{imageReference}\"", null, BuildkitNerdctlNamespace);
		if (WasSuccessful(buildkitNamespaceInspectResult)) {
			resolvedNerdctlNamespace = BuildkitNerdctlNamespace;
			_logger.WriteInfo(
				$"Using image '{imageReference}' from nerdctl namespace '{BuildkitNerdctlNamespace}'.");
			return buildkitNamespaceInspectResult;
		}

		return !string.IsNullOrWhiteSpace(defaultNamespaceInspectResult.StandardError)
			? defaultNamespaceInspectResult
			: buildkitNamespaceInspectResult;
	}

	private void StageBundledDevAssets(
		string buildContextPath,
		DockerTemplateResolution templateResolution,
		BuildDockerImageOptions options) {
		if (!templateResolution.IsBundled
			|| !string.Equals(templateResolution.Name, "dev", StringComparison.OrdinalIgnoreCase)) {
			return;
		}

		string cachedArchivePath = _codeServerArchiveCache.EnsureArchiveAvailable(options.VscodeVersion);
		string stagedArchivePath = _fileSystem.Combine(buildContextPath, "code-server.tar.gz");
		_fileSystem.CopyFile(cachedArchivePath, stagedArchivePath, true);
		_logger.WriteInfo($"Staged cached code-server archive into Docker context: {stagedArchivePath}");
	}

	private int ValidateBaseImageAvailability(
		ContainerImageCliKind containerImageCli,
		DockerTemplateResolution templateResolution,
		BuildDockerImageOptions options) {
		string imageReferenceToInspect = string.Empty;
		string missingImageMessage = string.Empty;

		if (ShouldUseBundledBaseImage(templateResolution)) {
			imageReferenceToInspect = ResolveBaseImageReference(options);
			missingImageMessage =
				$"Base image '{imageReferenceToInspect}' is not available locally. Build it first with 'clio build-docker-image --template base'";
			if (!string.IsNullOrWhiteSpace(options.BaseImage)) {
				missingImageMessage +=
					$" or pull/tag the custom base image '{imageReferenceToInspect}' locally before building '{templateResolution.Name}'.";
			}
			else {
				missingImageMessage +=
					$" or override it with '--base-image <image-ref>' before building '{templateResolution.Name}'.";
			}
		}
		else if (containerImageCli == ContainerImageCliKind.Nerdctl
			&& templateResolution.IsBundled
			&& IsBaseTemplate(templateResolution)) {
			imageReferenceToInspect = ResolveBundledTemplateSourceImageReference(templateResolution, options, templateResolution.TemplatePath);
			missingImageMessage =
				$"Base template source image '{imageReferenceToInspect}' is not available locally for nerdctl. Pull or load it locally before building '{templateResolution.Name}'.";
		}

		if (string.IsNullOrWhiteSpace(imageReferenceToInspect)) {
			return 0;
		}

		ProcessExecutionResult inspectResult =
			InspectImageForBuild(containerImageCli, imageReferenceToInspect, out string resolvedNerdctlNamespace);
		if (WasSuccessful(inspectResult)) {
			if (containerImageCli == ContainerImageCliKind.Nerdctl
				&& !string.Equals(resolvedNerdctlNamespace, BuildkitNerdctlNamespace, StringComparison.Ordinal)
				&& !EnsureImageAvailableInNerdctlBuildkitNamespace(imageReferenceToInspect)) {
				_logger.WriteError(
					$"Failed to prepare base image '{imageReferenceToInspect}' for nerdctl BuildKit namespace '{BuildkitNerdctlNamespace}'.");
				return 1;
			}

			_logger.WriteInfo($"Using configured base image: {imageReferenceToInspect}");
			return 0;
		}

		if (ShouldUseBundledBaseImage(templateResolution) && TryRestoreCachedBaseImageArchive(containerImageCli, imageReferenceToInspect)) {
			ProcessExecutionResult restoredInspectResult =
				ExecuteContainerCli(containerImageCli, $"image inspect \"{imageReferenceToInspect}\"", null);
			if (WasSuccessful(restoredInspectResult)) {
				if (containerImageCli == ContainerImageCliKind.Nerdctl
					&& !EnsureImageAvailableInNerdctlBuildkitNamespace(
						imageReferenceToInspect,
						BuildBundledBaseImageArchivePath(imageReferenceToInspect))) {
					_logger.WriteError(
						$"Failed to prepare base image '{imageReferenceToInspect}' for nerdctl BuildKit namespace '{BuildkitNerdctlNamespace}'.");
					return 1;
				}

				_logger.WriteInfo($"Using cached base image: {imageReferenceToInspect}");
				return 0;
			}
		}

		if (!string.IsNullOrWhiteSpace(inspectResult.StandardError)) {
			_logger.WriteError(
				$"Failed to inspect base image '{imageReferenceToInspect}'.{Environment.NewLine}{inspectResult.StandardError}");
			return 1;
		}

		_logger.WriteError(missingImageMessage);
		return 1;
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

	private int ValidateRegistryPushTarget(
		ContainerImageCliKind containerImageCli,
		string registryImageReference,
		string registryPrefix) {
		if (string.IsNullOrWhiteSpace(registryImageReference) || string.IsNullOrWhiteSpace(registryPrefix)) {
			return 0;
		}

		_logger.WriteInfo($"Running registry push preflight for: {registryImageReference}");
		ContainerRegistryPreflightResult preflightResult =
			_containerRegistryPreflightService.ValidatePushTarget(registryPrefix, registryImageReference);
		if (preflightResult.Success) {
			_logger.WriteInfo(
				$"Registry push preflight succeeded via '{preflightResult.Endpoint}' for '{registryImageReference}'.");
			return 0;
		}

		string registryHost = ExtractRegistryHost(registryPrefix);
		if (preflightResult.RequiresAuthentication) {
			_logger.WriteError(
				$"{preflightResult.Message}{Environment.NewLine}Run '{containerImageCli.ToCliName()} login {registryHost}' before using '--registry'.");
			return 1;
		}

		_logger.WriteError(
			$"Registry push preflight failed for '{registryImageReference}'.{Environment.NewLine}{preflightResult.Message}");
		return 1;
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

	private string ExtractRegistryHost(string registryPrefix) {
		if (Uri.TryCreate(registryPrefix, UriKind.Absolute, out Uri absolutePrefix)) {
			return absolutePrefix.Authority;
		}

		return registryPrefix.Trim().TrimEnd('/').Split('/', 2, StringSplitOptions.RemoveEmptyEntries)[0];
	}

	private void TryDeleteDirectoryIfExists(string directoryPath, string description) {
		if (string.IsNullOrWhiteSpace(directoryPath)) {
			return;
		}

		try {
			_fileSystem.DeleteDirectoryIfExists(directoryPath);
		}
		catch (Exception exception) {
			_logger.WriteWarning($"Failed to delete {description} '{directoryPath}': {exception.Message}");
		}
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
