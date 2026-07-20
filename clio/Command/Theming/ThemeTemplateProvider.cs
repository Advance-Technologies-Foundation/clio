using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Command.Theming;

/// <summary>
/// Supplies the theme templates bundled under <c>tpl/themes/{version}/</c>, choosing the variant that
/// matches a Creatio version (the highest bundled version not newer than the target).
/// </summary>
public interface IThemeTemplateProvider {
	/// <summary>The <c>theme.css</c> template for the bundled version compatible with <paramref name="creatioVersion"/>.</summary>
	/// <param name="creatioVersion">Target Creatio version; the highest bundled template is used when null/empty.</param>
	/// <exception cref="ArgumentException">The version is malformed or older than the lowest bundled template.</exception>
	string GetCssTemplate(string creatioVersion);

	/// <summary>The <c>theme.json</c> descriptor template for the bundled version compatible with <paramref name="creatioVersion"/>.</summary>
	/// <param name="creatioVersion">Target Creatio version; the highest bundled template is used when null/empty.</param>
	/// <exception cref="ArgumentException">The version is malformed or older than the lowest bundled template.</exception>
	string GetJsonTemplate(string creatioVersion);

	/// <summary>
	/// Resolves, offline, the bundled template version compatible with <paramref name="creatioVersion"/>
	/// (the highest bundled version not newer than the target). Never touches the network.
	/// </summary>
	/// <param name="creatioVersion">Target Creatio version; the highest bundled version is used when null/empty.</param>
	/// <returns>The resolved bundled version as a string (e.g. <c>10.0</c>).</returns>
	/// <exception cref="ArgumentException">The version is malformed or older than the lowest bundled template.</exception>
	string ResolveCompatibleVersion(string creatioVersion);
}

/// <summary>Reads the version-pinned theme templates from the bundled <c>tpl/themes/{version}/</c> folders.</summary>
public sealed class ThemeTemplateProvider : IThemeTemplateProvider {

	private const string ThemesFolder = "themes";
	private const string CssTemplateFile = "theme.css.tpl";
	private const string JsonTemplateFile = "theme.json.tpl";

	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private readonly IFileSystem _fileSystem;

	/// <summary>Initializes the provider with the working-directories provider and file system.</summary>
	public ThemeTemplateProvider(IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem) {
		_workingDirectoriesProvider = workingDirectoriesProvider;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public string GetCssTemplate(string creatioVersion) {
		return ReadTemplate(CssTemplateFile, creatioVersion);
	}

	/// <inheritdoc />
	public string GetJsonTemplate(string creatioVersion) {
		return ReadTemplate(JsonTemplateFile, creatioVersion);
	}

	/// <inheritdoc />
	public string ResolveCompatibleVersion(string creatioVersion) {
		string themesRoot = Path.Combine(_workingDirectoriesProvider.TemplateDirectory, ThemesFolder);
		return ResolveCompatibleVersion(themesRoot, creatioVersion).ToString();
	}

	private string ReadTemplate(string fileName, string creatioVersion) {
		string themesRoot = Path.Combine(_workingDirectoriesProvider.TemplateDirectory, ThemesFolder);
		Version version = ResolveCompatibleVersion(themesRoot, creatioVersion);
		string templatePath = Path.Combine(themesRoot, version.ToString(), fileName);
		if (!_fileSystem.ExistsFile(templatePath)) {
			throw new InvalidOperationException($"Theme template '{fileName}' is missing for Creatio version {version}.");
		}
		return _fileSystem.ReadAllText(templatePath);
	}

	private Version ResolveCompatibleVersion(string themesRoot, string creatioVersion) {
		Version[] available = _fileSystem.GetDirectories(themesRoot)
			.Select(Path.GetFileName)
			.Select(name => Version.TryParse(name, out Version parsed) ? parsed : null)
			.Where(parsed => parsed is not null)
			.OrderBy(parsed => parsed)
			.ToArray();
		if (available.Length == 0) {
			throw new InvalidOperationException($"No bundled theme templates were found under '{themesRoot}'.");
		}
		if (string.IsNullOrWhiteSpace(creatioVersion)) {
			return available[^1];
		}
		if (!Version.TryParse(creatioVersion, out Version target)) {
			throw new ArgumentException($"Invalid Creatio version '{creatioVersion}'.", nameof(creatioVersion));
		}
		Version compatible = available.LastOrDefault(candidate => candidate <= target);
		if (compatible is null) {
			throw new ArgumentException(
				$"Themes require Creatio {available[0]} or newer; version {target} is not supported.", nameof(creatioVersion));
		}
		return compatible;
	}
}
