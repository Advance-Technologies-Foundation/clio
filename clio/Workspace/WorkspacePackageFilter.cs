namespace Clio.Workspaces
{
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using Clio.Utilities;

	#region Class: WorkspacePackageFilter

	/// <summary>
	/// Provides filtering functionality for workspace packages based on ignore patterns.
	/// </summary>
	public class WorkspacePackageFilter : IWorkspacePackageFilter
	{

		#region Fields: Private

		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		/// <summary>
		/// Initializes a new instance of the <see cref="WorkspacePackageFilter"/> class.
		/// </summary>
		/// <param name="logger">Logger for outputting filtering information</param>
		public WorkspacePackageFilter(ILogger logger) {
			logger.CheckArgumentNull(nameof(logger));
			_logger = logger;
		}

		#endregion

		#region Methods: Public

		/// <summary>
		/// Filters the list of packages by applying ignore patterns from workspace settings.
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
               /// Filters the list of packages by applying provided ignore patterns.
               /// </summary>
               /// <param name="packages">The original list of packages</param>
               /// <param name="ignorePatterns">List of ignore patterns to apply</param>
               /// <returns>Filtered list of packages with ignored packages removed</returns>
               public IEnumerable<string> FilterPackages(IEnumerable<string> packages, IEnumerable<string> ignorePatterns) {
                       if (packages == null) {
                               return Enumerable.Empty<string>();
                       }

                       var ignorePatternsList = ignorePatterns?.ToList();
                       if (ignorePatternsList == null || !ignorePatternsList.Any()) {
                               return packages;
                       }

                       var filteredPackages = new List<string>();
                       var ignoredPackages = new List<string>();

                       foreach (string packageName in packages) {
                               if (PackageIgnoreMatcher.IsIgnored(packageName, ignorePatternsList)) {
                                       ignoredPackages.Add(packageName);
                               } else {
                                       filteredPackages.Add(packageName);
                               }
                       }

                       // Log information about ignored packages
                       if (ignoredPackages.Any()) {
                               _logger.WriteInfo($"Ignored {ignoredPackages.Count} package(s): {string.Join(", ", ignoredPackages)}");
                       }

                       return filteredPackages;
               }

               #endregion

       }

       #endregion

}