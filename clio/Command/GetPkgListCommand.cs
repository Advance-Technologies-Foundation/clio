using System;
using System.Collections.Generic;
using System.Linq;
using Clio;
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

		[Option('f', "filter", Required = false, HelpText = "Contains name filter",
		Default = null)]
		public string SearchPattern { get; set; } = string.Empty;

		[Option("Filter", Required = false, Hidden = true, HelpText = "Alias for --filter")]
		public string SearchPatternAlias {
			get => SearchPattern;
			set { if (!string.IsNullOrEmpty(value)) SearchPattern = value; }
		}

		[Option('j', "json", Required = false, HelpText = "Returns response in json format")]
		public bool? Json { get; set; }

		[Option("Json", Required = false, Hidden = true, HelpText = "Alias for --json")]
		public bool? JsonAlias {
			get => Json;
			set { Json = value; }
		}

		[Option("legacy-form", Required = false, Hidden = true,
			HelpText = "Compatibility escape hatch: with --json, emit the legacy {value,success,errorInfo} " +
			"shape instead of the unified envelope. Only meaningful together with --json.")]
		public bool LegacyForm { get; set; }


		#endregion

	}

	#endregion

	#region Class: GetPkgListCommand

	public class GetPkgListCommand : Command<PkgListOptions>
	{

		#region Constants: Private

		/// <summary>Canonical kebab-case command name, emitted in the unified <c>--json</c> envelope.</summary>
		private const string PkgListCommandName = "list-packages";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationPackageListProvider _applicationPackageListProvider;
		private readonly IJsonResponseFormater _jsonResponseFormater;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public GetPkgListCommand(EnvironmentSettings environmentSettings,
				IApplicationPackageListProvider applicationPackageListProvider,
				IJsonResponseFormater jsonResponseFormater,
				ILogger logger) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationPackageListProvider.CheckArgumentNull(nameof(applicationPackageListProvider));
			jsonResponseFormater.CheckArgumentNull(nameof(jsonResponseFormater));
			_environmentSettings = environmentSettings;
			_applicationPackageListProvider = applicationPackageListProvider;
			_jsonResponseFormater= jsonResponseFormater;
			_logger = logger;
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
			if (options.Json == true) {
				// --json defaults to the unified BL-1 envelope; --legacy-form preserves the historical
				// {value,success,errorInfo} shape for consumers not yet migrated. Both emit exactly one
				// JSON object to stdout via WriteLine (no [INF] prefix), keeping the stream jq-parseable.
				string json = options.LegacyForm
					? _jsonResponseFormater.Format(filteredPackages)
					: _jsonResponseFormater.FormatEnvelope(PkgListCommandName, filteredPackages);
				_logger.WriteLine(json);
			} else {
				if (filteredPackages.Any()) {
					PrintPackageList(filteredPackages);
				}
				_logger.WriteLine();
				_logger.WriteInfo($"Find {filteredPackages.Count()} packages in {_environmentSettings.Uri}");
			}
		}

		private void PrintError(PkgListOptions options, Exception e) {
			if (options.Json == true) {
				if (options.LegacyForm) {
					// Faithful legacy behavior: the historical --json error path wrote via WriteInfo.
					_logger.WriteInfo(_jsonResponseFormater.Format(e));
				} else {
					_logger.WriteLine(_jsonResponseFormater.FormatEnvelope(
						PkgListCommandName, CommandErrorCodes.UnexpectedError,
						e.GetReadableMessageException(Program.IsDebugMode)));
				}
			} else {
				_logger.WriteError(e.GetReadableMessageException(Program.IsDebugMode));
			}
		}

		internal bool TryGetFilteredPackages(PkgListOptions options, out IReadOnlyList<PackageInfo> packages,
			out string errorMessage, out string remediationMessage) {
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
