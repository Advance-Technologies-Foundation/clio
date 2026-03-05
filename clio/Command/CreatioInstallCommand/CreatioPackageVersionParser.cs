using System;
using System.Text.RegularExpressions;

namespace Clio.Command.CreatioInstallCommand;

/// <summary>
/// Provides version extraction from Creatio package file names.
/// </summary>
public interface ICreatioPackageVersionParser{
	#region Methods: Public

	/// <summary>
	/// Attempts to parse a Creatio build version from the package path.
	/// </summary>
	/// <param name="packagePath">Path to package zip file or extracted package directory.</param>
	/// <param name="version">Parsed version when extraction succeeds.</param>
	/// <returns><c>true</c> when version is parsed; otherwise, <c>false</c>.</returns>
	bool TryParseVersion(string packagePath, out Version version);

	#endregion
}

/// <summary>
/// Default implementation of <see cref="ICreatioPackageVersionParser"/>.
/// </summary>
public class CreatioPackageVersionParser : ICreatioPackageVersionParser{
	#region Fields: Private

	private static readonly Regex VersionPattern =
		new(@"^(?<version>\d+\.\d+\.\d+(?:\.\d+)?)_", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public bool TryParseVersion(string packagePath, out Version version) {
		version = null;

		if (string.IsNullOrWhiteSpace(packagePath)) {
			return false;
		}

		string fileName = GetFileName(packagePath);
		if (string.IsNullOrWhiteSpace(fileName)) {
			return false;
		}

		string packageName = TrimZipExtension(fileName);
		Match match = VersionPattern.Match(packageName);
		if (!match.Success) {
			return false;
		}

		return Version.TryParse(match.Groups["version"].Value, out version);
	}

	#endregion

	#region Methods: Private

	private static string GetFileName(string path) {
		int slashIndex = path.LastIndexOf('/');
		int backslashIndex = path.LastIndexOf('\\');
		int lastDelimiterIndex = Math.Max(slashIndex, backslashIndex);
		return lastDelimiterIndex >= 0 ? path[(lastDelimiterIndex + 1)..] : path;
	}

	private static string TrimZipExtension(string fileName) {
		return fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
			? fileName[..^4]
			: fileName;
	}

	#endregion
}
