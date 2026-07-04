using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: RemovePackageDependencyOptions

/// <summary>
/// Command-line options for the <c>remove-package-dependency</c> command.
/// </summary>
[Verb("remove-package-dependency", Aliases = ["remove-pkg-dependency", "remove-pkg-dep"],
	HelpText = "Remove one or more package dependencies from a package")]
public class RemovePackageDependencyOptions : RemoteCommandOptions
{

	#region Properties: Public

	/// <summary>
	/// Target package whose dependency list is trimmed.
	/// </summary>
	[Option("package-name", Required = true, HelpText = "Target package whose dependency list is trimmed")]
	public string PackageName { get; set; }

	/// <summary>
	/// Dependency package names to remove. Matched by name (case-insensitive); a trailing <c>:version</c> is
	/// accepted for symmetry with <c>add-package-dependency</c> but ignored.
	/// </summary>
	[Option("dependencies", Required = true, Min = 1, Separator = ',',
		HelpText = "Dependency package names to remove (matched by name). "
			+ "Example: --dependencies CrtLeadOppMgmtApp")]
	public IEnumerable<string> Dependencies { get; set; }

	#endregion

}

#endregion

#region Class: RemovePackageDependencyCommand

/// <summary>
/// Removes dependencies from a Creatio package via the <c>PackageService.svc</c> endpoint. Use this to roll
/// back a dependency that was added only to unblock the schema designer once it is no longer needed.
/// </summary>
public class RemovePackageDependencyCommand : RemoteCommand<RemovePackageDependencyOptions>
{

	#region Fields: Private

	private readonly IPackageDependencyManager _packageDependencyManager;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="RemovePackageDependencyCommand"/> class.
	/// </summary>
	/// <param name="packageDependencyManager">Service that performs the dependency removal and save.</param>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	public RemovePackageDependencyCommand(IPackageDependencyManager packageDependencyManager,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings) {
		_packageDependencyManager = packageDependencyManager;
	}

	#endregion

	#region Methods: Private

	private static IReadOnlyList<string> ParseDependencyNames(IEnumerable<string> rawDependencies) {
		List<string> names = (rawDependencies ?? [])
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(ParseDependencyName)
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.ToList();
		if (names.Count == 0) {
			throw new ArgumentException("At least one dependency must be specified via --dependencies.");
		}
		return names;
	}

	private static string ParseDependencyName(string value) {
		string trimmed = value.Trim();
		int separatorIndex = trimmed.IndexOf(':');
		return separatorIndex < 0 ? trimmed : trimmed[..separatorIndex].Trim();
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the remove-package-dependency command.
	/// </summary>
	/// <param name="options">Parsed command options.</param>
	/// <returns>Returns 0 when the dependencies are removed successfully; otherwise, returns 1.</returns>
	public override int Execute(RemovePackageDependencyOptions options) {
		try {
			IReadOnlyList<string> dependencyNames = ParseDependencyNames(options.Dependencies);
			Logger.WriteInfo(
				$"Removing {dependencyNames.Count} dependency(ies) from package \"{options.PackageName}\"...");
			IReadOnlyList<string> resultingDependencies =
				_packageDependencyManager.RemoveDependencies(options.PackageName, dependencyNames);
			Logger.WriteInfo(resultingDependencies.Count == 0
				? $"Package \"{options.PackageName}\" now has no dependencies"
				: $"Package \"{options.PackageName}\" now depends on: {string.Join(", ", resultingDependencies)}");
			Logger.WriteInfo("Done");
			return 0;
		} catch (Exception e) {
			Logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}

#endregion
