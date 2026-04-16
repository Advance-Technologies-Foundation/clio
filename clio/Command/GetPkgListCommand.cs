using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command
{

	#region Class: PkgListOptions

	[Verb("list-packages", Aliases = ["get-pkg-list", "packages"], HelpText = "Get environments packages")]
	public class PkgListOptions : EnvironmentNameOptions
	{

		#region Properties: Public

		[Option('f', "Filter", Required = false, HelpText = "Contains name filter",
		Default = null)]
		public string SearchPattern { get; set; } = string.Empty;

		[Option('j', "Json", Required = false, Default = false, HelpText = "Returns response in json format")]
		public bool? Json { get; set; }


		#endregion

	}

	#endregion

	#region Class: GetPkgListCommand

	public class GetPkgListCommand : Command<PkgListOptions>
	{
		private const string MinClioGateVersion = "2.0.0.0";

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;
		private readonly IJsonResponseFormater _jsonResponseFormater;
		private readonly ILogger _logger;
		private readonly IClioGateway _clioGateway;

		#endregion

		#region Constructors: Public

		public GetPkgListCommand(EnvironmentSettings environmentSettings, 
				IApplicationPackageListProvider applicationPackageListProvider,
				IJsonResponseFormater jsonResponseFormater,
				ILogger logger, IClioGateway clioGateway) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			jsonResponseFormater.CheckArgumentNull(nameof(jsonResponseFormater));
			_environmentSettings = environmentSettings;
			_applicationPackageListProvider = applicationPackageListProvider;
			_jsonResponseFormater= jsonResponseFormater;
			_logger = logger;
			_clioGateway = clioGateway;
		}

		#endregion

		#region Methods: Private

		private static string[] CreateRow(string nameColumn, string versionColumn, string maintainerColumn) {
			return new[] { nameColumn, versionColumn, maintainerColumn };
		}

		private static string[] CreateEmptyRow() {
			return CreateRow(string.Empty, string.Empty, string.Empty);
		}

		private void PrintPackageList(IEnumerable<PackageInfo> packages) {
			IList<string[]> table = new List<string[]>();
			table.Add(CreateRow("Name", "Version", "Maintainer"));
			table.Add(CreateEmptyRow());
			foreach (PackageInfo pkg in packages) {
				table.Add(CreateRow(pkg.Descriptor.Name, pkg.Descriptor.PackageVersion, pkg.Descriptor.Maintainer));
			}
			_logger.WriteLine();
			_logger.WriteInfo(TextUtilities.ConvertTableToString(table));
			_logger.WriteLine();
		}

		private static IEnumerable<PackageInfo> FilterPackages(IEnumerable<PackageInfo> packages, 
				string searchPattern) {
			return packages
				.Where(p => p.Descriptor.Name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
				.OrderBy(p => p.Descriptor.Name);
		}

		private void PrintPackageList(PkgListOptions options, IEnumerable<PackageInfo> filteredPackages) {
			if (options.Json.HasValue && options.Json.Value) {
				_logger.WriteLine(_jsonResponseFormater.Format(filteredPackages));
			} else {
				if (filteredPackages.Any()) {
					PrintPackageList(filteredPackages);
				}
				_logger.WriteLine();
				_logger.WriteInfo($"Find {filteredPackages.Count()} packages in {_environmentSettings.Uri}");
			}
		}

		private void PrintError(PkgListOptions options, Exception e) {
			if (options.Json.HasValue && options.Json.Value) {
				_logger.WriteInfo(_jsonResponseFormater.Format(e));
			} else {
				_logger.WriteInfo(e.ToString());
			}
		}

		internal bool TryGetFilteredPackages(PkgListOptions options, out IReadOnlyList<PackageInfo> packages,
			out string errorMessage, out string remediationMessage) {
			if (!_clioGateway.IsCompatibleWith(MinClioGateVersion)) {
				packages = Array.Empty<PackageInfo>();
				errorMessage =
					$"To view packages feature requires cliogate package version {MinClioGateVersion} or higher installed in Creatio.";
				remediationMessage = string.IsNullOrWhiteSpace(options.Environment)
					? "To install cliogate use the following command: clio install-gate"
					: $"To install cliogate use the following command: clio install-gate -e {options.Environment}";
				return false;
			}

			packages = FilterPackages(_applicationPackageListProvider.GetPackages(), options.SearchPattern).ToList();
			errorMessage = string.Empty;
			remediationMessage = string.Empty;
			return true;
		}

		#endregion

		#region Methods: Public

		public override int Execute(PkgListOptions options) {
			try {
				if (!TryGetFilteredPackages(options, out IReadOnlyList<PackageInfo> filteredPackages,
						out string errorMessage, out string remediationMessage)) {
					_logger.WriteError(errorMessage);
					_logger.WriteInfo(remediationMessage);
					return 0;
				}

				PrintPackageList(options, filteredPackages);
				return 0;
			} catch (Exception e) {
				PrintError(options, e);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
