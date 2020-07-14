using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Clio.Common;
using Clio.Package;
using CommandLine;
using Newtonsoft.Json;

namespace Clio.Command
{

	#region Class: PkgListOptions

	[Verb("get-pkg-list", Aliases = new[] { "packages" }, HelpText = "Get environments packages")]
	public class PkgListOptions : EnvironmentNameOptions
	{

		#region Properties: Public

		[Option('f', "Filter", Required = false, HelpText = "Contains name filter",
		Default = null)]
		public string SearchPattern { get; set; } = string.Empty;

		#endregion

	}

	#endregion

	#region Class: GetPkgListCommand

	public class GetPkgListCommand : Command<PkgListOptions>
	{
		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;

		#endregion

		#region Constructors: Public

		public GetPkgListCommand(EnvironmentSettings environmentSettings, 
				IApplicationPackageListProvider applicationPackageListProvider) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			_environmentSettings = environmentSettings;
			_applicationPackageListProvider = applicationPackageListProvider;
		}

		#endregion

		#region Methods: Private

		private static string[] CreateRow(string nameColumn, string versionColumn, string maintainerColumn) {
			return new[] { nameColumn, versionColumn, maintainerColumn };
		}

		private static string[] CreateEmptyRow() {
			return CreateRow(string.Empty, string.Empty, string.Empty);
		}

		private static void PrintPackageList(IEnumerable<PackageInfo> packages) {
			IList<string[]> table = new List<string[]>();
			table.Add(CreateRow("Name", "Version", "Maintainer"));
			table.Add(CreateEmptyRow());
			foreach (PackageInfo pkg in packages) {
				table.Add(CreateRow(pkg.Descriptor.Name, pkg.Descriptor.PackageVersion, pkg.Descriptor.Maintainer));
			}
			Console.WriteLine();
			Console.WriteLine(TextUtilities.ConvertTableToString(table));
			Console.WriteLine();
		}

		private static IEnumerable<PackageInfo> FilterPackages(IEnumerable<PackageInfo> packages, 
				string searchPattern) {
			return packages
				.Where(p => p.Descriptor.Name.ToLower().Contains(searchPattern.ToLower()))
				.OrderBy(p => p.Descriptor.Name);
		}

		#endregion

		#region Methods: Public

		public override int Execute(PkgListOptions options) {
			try {
				IEnumerable<PackageInfo> packages = _applicationPackageListProvider.GetPackages();
				var filteredPackages = FilterPackages(packages, options.SearchPattern);
				if (filteredPackages.Any()) {
					PrintPackageList(filteredPackages);
				}
				Console.WriteLine();
				Console.WriteLine($"Find {filteredPackages.Count()} packages in {_environmentSettings.Uri}");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}