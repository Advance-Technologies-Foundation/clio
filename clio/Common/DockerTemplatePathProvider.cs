#pragma warning disable CLIO001 // Exception construction is not resolved from DI.

using System;
using System.IO;
using System.IO.Abstractions;
using System.Reflection;

namespace Clio.Common;

/// <summary>
/// Resolves bundled and custom Docker image template folders.
/// </summary>
public interface IDockerTemplatePathProvider {
	/// <summary>
	/// Ensures bundled templates are available under the clio settings folder and resolves the requested template.
	/// </summary>
	/// <param name="templateNameOrPath">Bundled template name or custom template folder path.</param>
	/// <returns>The resolved template metadata.</returns>
	DockerTemplateResolution ResolveTemplate(string templateNameOrPath);
}

/// <summary>
/// Describes a resolved Docker template.
/// </summary>
/// <param name="Name">Stable template name used in image naming.</param>
/// <param name="TemplatePath">Absolute path to the template directory.</param>
/// <param name="IsBundled">Whether the template came from clio bundled assets.</param>
public sealed record DockerTemplateResolution(string Name, string TemplatePath, bool IsBundled);

/// <summary>
/// Default implementation of <see cref="IDockerTemplatePathProvider"/>.
/// </summary>
public sealed class DockerTemplatePathProvider(
	IFileSystem fileSystem,
	System.IO.Abstractions.IFileSystem msFileSystem,
	string settingsRootPath = null,
	string bundledTemplatesRootPath = null)
	: IDockerTemplatePathProvider {
	private const string DockerTemplatesFolderName = "docker-templates";
	private const string DockerfileName = "Dockerfile";

	private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	private readonly System.IO.Abstractions.IFileSystem _msFileSystem =
		msFileSystem ?? throw new ArgumentNullException(nameof(msFileSystem));
	private readonly string _bundledTemplatesRootPath = bundledTemplatesRootPath ?? string.Empty;
	private readonly string _settingsRootPath = settingsRootPath ?? string.Empty;

	/// <inheritdoc />
	public DockerTemplateResolution ResolveTemplate(string templateNameOrPath) {
		if (string.IsNullOrWhiteSpace(templateNameOrPath)) {
			throw new ArgumentException("Template name or path is required.", nameof(templateNameOrPath));
		}

		if (LooksLikePath(templateNameOrPath)) {
			string customTemplatePath = _fileSystem.GetFullPath(templateNameOrPath);
			ValidateTemplateDirectory(customTemplatePath);
			string customTemplateName = NormalizeTemplateName(_msFileSystem.Path.GetFileName(customTemplatePath));
			return new DockerTemplateResolution(customTemplateName, customTemplatePath, false);
		}

		string bundledTemplatesPath = EnsureBundledTemplatesAvailable();
		string bundledTemplatePath = _fileSystem.Combine(bundledTemplatesPath, templateNameOrPath);
		if (!_fileSystem.ExistsDirectory(bundledTemplatePath)) {
			throw new DirectoryNotFoundException(
				$"Bundled Docker template '{templateNameOrPath}' was not found in '{bundledTemplatesPath}'.");
		}

		ValidateTemplateDirectory(bundledTemplatePath);
		return new DockerTemplateResolution(
			NormalizeTemplateName(templateNameOrPath),
			_fileSystem.GetFullPath(bundledTemplatePath),
			true);
	}

	private string EnsureBundledTemplatesAvailable() {
		string destinationRoot = GetSettingsTemplatesRoot();
		string sourceRoot = GetBundledTemplatesRoot();

		if (!_fileSystem.ExistsDirectory(sourceRoot)) {
			throw new DirectoryNotFoundException($"Bundled Docker templates were not found at '{sourceRoot}'.");
		}

		_fileSystem.CopyDirectory(sourceRoot, destinationRoot, true);
		return destinationRoot;
	}

	private string GetBundledTemplatesRoot() {
		if (!string.IsNullOrWhiteSpace(_bundledTemplatesRootPath)) {
			return _fileSystem.GetFullPath(_bundledTemplatesRootPath);
		}

		string assemblyDirectory =
			_msFileSystem.FileInfo.New(Assembly.GetExecutingAssembly().Location).DirectoryName ?? string.Empty;
		return _fileSystem.NormalizeFilePathByPlatform($"{assemblyDirectory}/tpl/{DockerTemplatesFolderName}");
	}

	private string GetSettingsTemplatesRoot() {
		string settingsRoot = string.IsNullOrWhiteSpace(_settingsRootPath)
			? SettingsRepository.AppSettingsFolderPath
			: _settingsRootPath;
		return _fileSystem.Combine(settingsRoot, DockerTemplatesFolderName);
	}

	private bool LooksLikePath(string templateNameOrPath) {
		return _fileSystem.IsPathRooted(templateNameOrPath)
			|| templateNameOrPath.Contains("/")
			|| templateNameOrPath.Contains("\\");
	}

	private string NormalizeTemplateName(string templateName) {
		if (string.IsNullOrWhiteSpace(templateName)) {
			return "custom";
		}

		return templateName.Trim().ToLowerInvariant();
	}

	private void ValidateTemplateDirectory(string templateDirectoryPath) {
		if (!_fileSystem.ExistsDirectory(templateDirectoryPath)) {
			throw new DirectoryNotFoundException($"Docker template directory '{templateDirectoryPath}' was not found.");
		}

		string dockerfilePath = _fileSystem.Combine(templateDirectoryPath, DockerfileName);
		if (!_fileSystem.ExistsFile(dockerfilePath)) {
			throw new InvalidOperationException(
				$"Docker template directory '{templateDirectoryPath}' must contain a '{DockerfileName}'.");
		}
	}
}

#pragma warning restore CLIO001 // Exception construction is not resolved from DI.
