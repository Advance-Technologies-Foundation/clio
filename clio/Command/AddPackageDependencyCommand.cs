using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

#region Class: AddPackageDependencyOptions

/// <summary>
/// Command-line options for the <c>add-package-dependency</c> command.
/// </summary>
[Verb("add-package-dependency", Aliases = ["add-pkg-dependency", "add-pkg-dep"],
	HelpText = "Add one or more package dependencies to a package")]
public class AddPackageDependencyOptions : RemoteCommandOptions
{

	#region Properties: Public

	/// <summary>
	/// Target package whose dependency list is extended.
	/// </summary>
	[Option("package-name", Required = true, HelpText = "Target package whose dependency list is extended")]
	public string PackageName { get; set; }

	/// <summary>
	/// Dependency package names to add. Each entry is <c>name</c> or <c>name:version</c>; when the version is
	/// omitted, the installed version of the dependency package is used.
	/// </summary>
	[Option("dependencies", Required = true, Min = 1, Separator = ',',
		HelpText = "Dependency package names to add. Use 'name' or 'name:version'. "
			+ "Example: --dependencies CrtLeadOppMgmtApp")]
	public IEnumerable<string> Dependencies { get; set; }

	#endregion

}

#endregion

#region Class: AddPackageDependencyCommand

/// <summary>
/// Adds dependencies to a Creatio package via the <c>PackageService.svc</c> endpoint, so that schemas of the
/// added packages become visible to the schema designer and compiler for the target package.
/// </summary>
public class AddPackageDependencyCommand : RemoteCommand<AddPackageDependencyOptions>
{

	#region Fields: Private

	private readonly IPackageDependencyManager _packageDependencyManager;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="AddPackageDependencyCommand"/> class.
	/// </summary>
	/// <param name="packageDependencyManager">Service that performs the dependency merge and save.</param>
	/// <param name="environmentSettings">Resolved target environment settings.</param>
	public AddPackageDependencyCommand(IPackageDependencyManager packageDependencyManager,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings) {
		_packageDependencyManager = packageDependencyManager;
	}

	#endregion

	#region Methods: Private

	private static IReadOnlyList<PackageDependencySpec> ParseDependencies(IEnumerable<string> rawDependencies) {
		List<PackageDependencySpec> dependencies = (rawDependencies ?? [])
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(ParseDependency)
			.ToList();
		if (dependencies.Count == 0) {
			throw new ArgumentException("At least one dependency must be specified via --dependencies.");
		}
		return dependencies;
	}

	private static PackageDependencySpec ParseDependency(string value) {
		string trimmed = value.Trim();
		int separatorIndex = trimmed.IndexOf(':');
		if (separatorIndex < 0) {
			return new PackageDependencySpec(trimmed);
		}
		string name = trimmed[..separatorIndex].Trim();
		string version = trimmed[(separatorIndex + 1)..].Trim();
		return new PackageDependencySpec(name, version);
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the add-package-dependency command.
	/// </summary>
	/// <param name="options">Parsed command options.</param>
	/// <returns>Returns 0 when the dependencies are added successfully; otherwise, returns 1.</returns>
	public override int Execute(AddPackageDependencyOptions options) {
		try {
			IReadOnlyList<PackageDependencySpec> dependencies = ParseDependencies(options.Dependencies);
			Logger.WriteInfo(
				$"Adding {dependencies.Count} dependency(ies) to package \"{options.PackageName}\"...");
			IReadOnlyList<string> resultingDependencies =
				_packageDependencyManager.AddDependencies(options.PackageName, dependencies);
			Logger.WriteInfo(
				$"Package \"{options.PackageName}\" now depends on: {string.Join(", ", resultingDependencies)}");
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
