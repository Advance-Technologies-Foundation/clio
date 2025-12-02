#region

using System.Collections.Generic;
using System.Linq;
using Clio.Common;

#endregion

namespace Clio.Workspaces;

/// <summary>
/// Provides filtering functionality for workspace packages based on ignore patterns.
/// </summary>
public interface IWorkspacePackageFilter
{

	#region Methods: Public

	/// <summary>
	/// Filters the list of packages by applying ignore patterns from workspace settings.
	/// </summary>
	/// <param name="packages">The original list of packages</param>
	/// <param name="workspaceSettings">Workspace settings containing ignore patterns</param>
	/// <returns>Filtered list of packages with ignored packages removed</returns>
	IEnumerable<string> FilterPackages(IEnumerable<string> packages, WorkspaceSettings workspaceSettings);

	/// <summary>
	/// Filters the list of packages by applying provided ignore patterns.
	/// </summary>
	/// <param name="packages">The original list of packages</param>
	/// <param name="ignorePatterns">List of ignore patterns to apply</param>
	/// <returns>Filtered list of packages with ignored packages removed</returns>
	IEnumerable<string> FilterPackages(IEnumerable<string> packages, IEnumerable<string> ignorePatterns);

	/// <summary>
	/// Returns only the packages that are present both in the workspace settings and the provided list.
	/// </summary>
	IEnumerable<string> IncludedPackages(IEnumerable<string> packagesInWorkSpace, WorkspaceSettings workspaceSettings);

	#endregion

}


#region Class: WorkspacePackageFilter

/// <summary>
///     Provides filtering functionality for workspace packages based on ignore patterns.
/// </summary>
public class WorkspacePackageFilter : IWorkspacePackageFilter{
	#region Fields: Private

	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	///     Initializes a new instance of the <see cref="WorkspacePackageFilter" /> class.
	/// </summary>
	/// <param name="logger">Logger for outputting filtering information</param>
	public WorkspacePackageFilter(ILogger logger) {
		logger.CheckArgumentNull(nameof(logger));
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	///     Filters the list of packages by applying ignore patterns from workspace settings.
	/// </summary>
	/// <param name="packages">The original list of packages</param>
	/// <param name="workspaceSettings">Workspace settings containing ignore patterns</param>
	/// <returns>Filtered list of packages with ignored packages removed</returns>
	public IEnumerable<string> FilterPackages(IEnumerable<string> packages, WorkspaceSettings workspaceSettings) {
		workspaceSettings.CheckArgumentNull(nameof(workspaceSettings));

		if (workspaceSettings.IgnorePackages == null || !workspaceSettings.IgnorePackages.Any()) {
			return packages;
		}

		return FilterPackages(packages, workspaceSettings.IgnorePackages);
	}

	/// <summary>
	///     Filters the list of packages by applying provided ignore patterns.
	/// </summary>
	/// <param name="packages">The original list of packages</param>
	/// <param name="ignorePatterns">List of ignore patterns to apply</param>
	/// <returns>Filtered list of packages with ignored packages removed</returns>
	public IEnumerable<string> FilterPackages(IEnumerable<string> packages, IEnumerable<string> ignorePatterns) {
		if (packages == null) {
			return Enumerable.Empty<string>();
		}

		List<string> ignorePatternsList = ignorePatterns?.ToList();
		if (ignorePatternsList == null || !ignorePatternsList.Any()) {
			return packages;
		}

		List<string> filteredPackages = new();
		List<string> ignoredPackages = new();

		foreach (string packageName in packages) {
			if (PackageIgnoreMatcher.IsIgnored(packageName, ignorePatternsList)) {
				ignoredPackages.Add(packageName);
			}
			else {
				filteredPackages.Add(packageName);
			}
		}

		// Log information about ignored packages
		if (ignoredPackages.Any()) {
			_logger.WriteInfo($"Ignored {ignoredPackages.Count} package(s): {string.Join(", ", ignoredPackages)}");
		}

		return filteredPackages;
	}
	
	/// <summary>
	/// Returns only the packages that are present both in the workspace settings and the provided list.
	/// </summary>
	public IEnumerable<string> IncludedPackages(IEnumerable<string> packagesInWorkSpace, WorkspaceSettings workspaceSettings) {
		//return only packages that are in workspace settings and packagesInWorkSpace
		IEnumerable<string> inWorkSpace = packagesInWorkSpace as string[] ?? packagesInWorkSpace.ToArray();

		if (workspaceSettings == null) {
			return [];
		}

		if (workspaceSettings.Packages == null || !workspaceSettings.Packages.Any()) {
			return [];
		}
		
		return inWorkSpace.Any(p => workspaceSettings.Packages.Contains(p))
			? inWorkSpace.Where(p => workspaceSettings.Packages.Contains(p)).ToList()
			: Enumerable.Empty<string>();
	}

	#endregion
}

#endregion
