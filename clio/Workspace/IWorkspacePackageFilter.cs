namespace Clio.Workspaces
{
	using System.Collections.Generic;

	#region Interface: IWorkspacePackageFilter

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

		#endregion

	}

	#endregion

}