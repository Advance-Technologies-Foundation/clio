using System;
using System.IO;
using Clio.Common;

namespace Clio.Package;

#region Interface: IThemeCreator

/// <summary>
/// Scaffolds a theme artifact (<c>theme.json</c> + <c>theme.css</c>) into a workspace package.
/// </summary>
public interface IThemeCreator
{

	#region Methods: Public

	/// <summary>
	/// Creates a theme inside the given package from the baseline template. The hosting package is
	/// downloaded from the environment when it already exists there and the caller opts in, otherwise a
	/// new local package is created.
	/// </summary>
	/// <param name="cssClassName">Root CSS class name.</param>
	/// <param name="packageName">Package that will host the theme.</param>
	/// <param name="caption">Optional explicit caption; derived from the class name when omitted.</param>
	/// <param name="id">Optional explicit id; a UUID is generated when omitted.</param>
	/// <param name="enableDownloadPackage">Callback deciding whether an existing remote package is downloaded.</param>
	/// <returns>The resolved <see cref="ThemeIdentifiers"/>.</returns>
	ThemeIdentifiers CreateTheme(string cssClassName, string packageName, string caption = null, string id = null,
		Func<string, bool> enableDownloadPackage = null);

	#endregion

}

#endregion

#region Class: ThemeCreator

/// <inheritdoc cref="IThemeCreator"/>
public class ThemeCreator : IThemeCreator
{

	#region Constants: Private

	private const string FilesDirectoryName = "Files";
	private const string ThemesDirectoryName = "themes";
	private const string ThemeJsonFileName = "theme.json";
	private const string ThemeCssFileName = "theme.css";

	#endregion

	#region Fields: Private

	private readonly IWorkspacePackageProvisioner _packageProvisioner;
	private readonly IThemeArtifactBuilder _artifactBuilder;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public ThemeCreator(IWorkspacePackageProvisioner packageProvisioner, IThemeArtifactBuilder artifactBuilder,
		IFileSystem fileSystem) {
		packageProvisioner.CheckArgumentNull(nameof(packageProvisioner));
		artifactBuilder.CheckArgumentNull(nameof(artifactBuilder));
		fileSystem.CheckArgumentNull(nameof(fileSystem));
		_packageProvisioner = packageProvisioner;
		_artifactBuilder = artifactBuilder;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public ThemeIdentifiers CreateTheme(string cssClassName, string packageName, string caption = null,
		string id = null, Func<string, bool> enableDownloadPackage = null) {
		ThemeIdentifiers identifiers = _artifactBuilder.DeriveIdentifiers(cssClassName, caption, id);
		_artifactBuilder.Validate(identifiers);
		// Generate both files before any side effect so a generation error cannot leave a half-written
		// theme (or an orphan package). A failed disk write on the second file is still possible but rare.
		string themeJson = _artifactBuilder.BuildThemeJson(identifiers);
		string themeCss = _artifactBuilder.BuildThemeCss(identifiers);
		_packageProvisioner.EnsurePackage(packageName, enableDownloadPackage ?? (_ => false));
		string themeDirectory = Path.Combine(_packageProvisioner.PackagesPath, packageName, FilesDirectoryName,
			ThemesDirectoryName, identifiers.CssClassName);
		_fileSystem.CreateDirectoryIfNotExists(themeDirectory);
		_fileSystem.WriteAllTextToFile(Path.Combine(themeDirectory, ThemeJsonFileName), themeJson);
		_fileSystem.WriteAllTextToFile(Path.Combine(themeDirectory, ThemeCssFileName), themeCss);
		return identifiers;
	}

	#endregion

}

#endregion
