namespace Clio.Command.PackageCommand;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Package;

/// <summary>
/// Why a package can or cannot be deleted from the environment.
/// </summary>
public enum PackageDeletionStatus {

	/// <summary>
	/// The package exists, is editable and nothing depends on it, so the deletion may be attempted.
	/// </summary>
	Ready,

	/// <summary>
	/// No package with that name exists in the environment.
	/// </summary>
	NotFound,

	/// <summary>
	/// The package is foreign to the environment — its maintainer differs from the environment's maintainer, or it
	/// was installed from a repository (an <c>InstallType</c> other than source control) — and must not be deleted.
	/// </summary>
	Locked,

	/// <summary>
	/// Other installed packages depend on this package; deleting it would leave them without a dependency.
	/// </summary>
	BlockedByDependents,

	/// <summary>
	/// The pre-flight could not establish enough about the package to decide, so nothing is deleted: a deletion
	/// whose dependents were never read is not a safe deletion.
	/// </summary>
	Unavailable
}

/// <summary>
/// What the environment holds inside a package at the moment the deletion was considered. The report cannot be
/// produced after the fact — those rows leave with the package — so this is read BEFORE the call.
/// </summary>
/// <param name="Schemas">Names of the schemas the package holds, capped at
/// <see cref="PackageDeletionPreflight.NamesToListed"/>.</param>
/// <param name="SchemaCount">How many schemas the package holds, as far as the read could tell.</param>
/// <param name="DataBindings">Names of the object data bindings the package declares.</param>
/// <param name="DataBindingCount">How many data bindings the package declares.</param>
/// <param name="SqlScripts">Names of the install-time SQL scripts the package ships.</param>
/// <param name="SqlScriptCount">How many SQL scripts the package ships.</param>
/// <param name="Partial"><see langword="true"/> when one of the three reads failed, so a zero count means
/// "not read", not "none".</param>
public sealed record PackageDeletionInventory(
	IReadOnlyList<string> Schemas,
	int SchemaCount,
	IReadOnlyList<string> DataBindings,
	int DataBindingCount,
	IReadOnlyList<string> SqlScripts,
	int SqlScriptCount,
	bool Partial);

/// <summary>
/// What the pre-flight established about one package before any deletion was attempted.
/// </summary>
/// <param name="Status">Whether the package may be deleted and, if not, why.</param>
/// <param name="PackageName">The package name as the caller spelled it.</param>
/// <param name="PackageUId">Identifier of the package in the environment; <see langword="null"/> when the package
/// was not found or the read failed.</param>
/// <param name="Dependents">Installed packages that depend on this package; never empty when
/// <paramref name="Status"/> is <see cref="PackageDeletionStatus.BlockedByDependents"/>.</param>
/// <param name="Inventory">What the package holds; <see langword="null"/> when nothing was read.</param>
/// <param name="Warning">Deterministic sentence naming which inventory read failed; <see langword="null"/> when
/// every read succeeded.</param>
/// <param name="Error">Why the pre-flight could not decide, with any wording the environment supplied kept in the
/// fenced rendering the MCP envelope requires; <see langword="null"/> when the reads succeeded.</param>
public sealed record PackageDeletionAssessment(
	PackageDeletionStatus Status,
	string PackageName,
	string PackageUId,
	IReadOnlyList<string> Dependents,
	PackageDeletionInventory Inventory,
	string Warning,
	string Error) {

	/// <summary>
	/// Gets whether the deletion may be attempted.
	/// </summary>
	public bool CanDelete => Status == PackageDeletionStatus.Ready;
}

/// <summary>
/// Reads what the environment knows about a package before it is deleted, so an unsafe deletion is refused here
/// instead of arriving back from Creatio as free text.
/// </summary>
public interface IPackageDeletionPreflight {

	/// <summary>
	/// Reads the package, what depends on it and what it holds. Performs no write.
	/// </summary>
	/// <param name="packageName">Package name as the caller spelled it.</param>
	/// <returns>The assessment. A read that failed yields <see cref="PackageDeletionStatus.Unavailable"/> rather
	/// than an optimistic <see cref="PackageDeletionStatus.Ready"/>.</returns>
	PackageDeletionAssessment Assess(string packageName);

	/// <summary>
	/// Asks the environment whether the package is still installed. Used after a deletion, whose outcome the
	/// environment reports only in prose.
	/// </summary>
	/// <param name="packageName">Package name to look for.</param>
	/// <returns><see langword="true"/> when the package is still installed, <see langword="false"/> when it is
	/// gone, <see langword="null"/> when the environment could not be asked.</returns>
	bool? ProbeExists(string packageName);
}

/// <summary>
/// Reads the pre-flight facts from the environment's own package tables through DataService Select:
/// <c>SysPackage</c> for existence and the lock, <c>SysPackageDependency</c> for what depends on the package, and
/// <c>SysSchema</c> / <c>SysPackageSchemaData</c> / <c>SysPackageSqlScript</c> for what the package holds.
/// </summary>
/// <remarks>
/// Creatio enforces much of this itself, but it answers in prose inside <c>errorInfo.message</c>, which a tool
/// cannot hand an agent as a decision — and it does not report the lock at all before the delete attempt. Reading
/// the tables costs four idempotent requests and turns each refusal into a field of the response.
/// <para>
/// The dependency read is the one that decides. The content reads only report: a package the environment
/// considers deletable is not kept alive because <c>SysPackageSqlScript</c> refused a column.
/// </para>
/// </remarks>
public sealed class PackageDeletionPreflight : IPackageDeletionPreflight {

	#region Constants: Private

	private const string PackageSchema = "SysPackage";
	private const string DependencySchema = "SysPackageDependency";
	private const string SchemaSchema = "SysSchema";
	private const string SchemaDataSchema = "SysPackageSchemaData";
	private const string SqlScriptSchema = "SysPackageSqlScript";
	private const string SettingValueSchema = "SysSettingsValue";
	private const string NameColumn = "Name";
	private const string UIdColumn = "UId";
	private const string MaintainerColumn = "Maintainer";
	private const string InstallTypeColumn = "InstallType";
	private const string SettingCodeColumn = "SysSettings.Code";
	private const string SettingValueColumn = "TextValue";
	private const string MaintainerSettingCode = "Maintainer";
	private const string PackageNameFilter = "SysPackage.Name";
	private const string DependOnPackageNameFilter = "DependOnPackage.Name";
	private const string DependentPackageNameColumn = "SysPackage.Name";

	/// <summary>
	/// How many names of one kind a report lists. Only the list is capped, never the count: the caller decides
	/// whether a package holding 400 schemas is the one it meant to delete.
	/// </summary>
	internal const int NamesToListed = 25;

	/// <summary>
	/// Rows one content read accepts. A scratch package holds tens of rows; a product package thousands, and a
	/// product package is refused on the lock before this cap is ever reached.
	/// </summary>
	private const int InventoryRowCount = 1000;

	/// <summary>
	/// The only <c>InstallType</c> Creatio treats as editable; anything else is a package the environment
	/// installed from a repository and will not delete.
	/// </summary>
	private const int EditableInstallType = 0;

	#endregion

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="PackageDeletionPreflight"/> class.
	/// </summary>
	/// <param name="applicationClient">Client the Select reads are sent through.</param>
	/// <param name="serviceUrlBuilder">Builder of the Select endpoint URL.</param>
	/// <param name="logger">Logger the debug channel of a failed read is written to.</param>
	public PackageDeletionPreflight(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public PackageDeletionAssessment Assess(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			return Unavailable(packageName, "The package name is empty.");
		}

		PackageRowDto package;
		try {
			package = ReadPackage(packageName);
		} catch (Exception exception) {
			return Unavailable(packageName, DescribeFailure("could not read the package", exception));
		}

		if (package is null) {
			return new PackageDeletionAssessment(PackageDeletionStatus.NotFound, packageName, null, [], null, null,
				null);
		}

		string? environmentMaintainer;
		try {
			environmentMaintainer = ReadEnvironmentMaintainer();
		} catch (Exception exception) {
			return Unavailable(packageName, DescribeFailure("could not read the environment maintainer", exception));
		}

		if (IsForeign(package, environmentMaintainer)) {
			return new PackageDeletionAssessment(PackageDeletionStatus.Locked, packageName, package.UId, [], null,
				null, null);
		}

		IReadOnlyList<string> dependents;
		try {
			dependents = ReadDependents(packageName);
		} catch (Exception exception) {
			return Unavailable(packageName, DescribeFailure("could not read what depends on the package",
				exception));
		}

		if (dependents.Count > 0) {
			return new PackageDeletionAssessment(PackageDeletionStatus.BlockedByDependents, packageName,
				package.UId, dependents, null, null, null);
		}

		(PackageDeletionInventory inventory, string? warning) = ReadInventory(packageName);

		return new PackageDeletionAssessment(PackageDeletionStatus.Ready, packageName, package.UId, [], inventory,
			warning, null);
	}

	/// <inheritdoc />
	public bool? ProbeExists(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			return null;
		}

		try {
			return ReadPackage(packageName) is not null;
		} catch (Exception exception) {
			// The deletion already happened by the time this runs; a verification read that fails is reported by
			// the caller as "not verified", so only the debug channel carries the environment's own wording.
			_logger.WriteDebug($"Package deletion verification read failed: {exception.Message}");
			return null;
		}
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Whether the package is foreign to the environment, which is the platform's own rule for refusing a delete
	/// (<c>AppInstaller</c>: <c>maintainer != environment maintainer || installType != SourceControl</c>). The
	/// <c>IsLocked</c> column is NOT consulted: it is a design-time checkout marker the delete gate ignores on
	/// .NET, and <c>create-package</c> with dependencies leaves it set on an ordinary scratch package.
	/// </summary>
	/// <param name="package">The package row as read.</param>
	/// <param name="environmentMaintainer">The environment's <c>Maintainer</c> system setting; <see langword="null"/>
	/// when the environment has none, in which case only the install type decides.</param>
	/// <returns>Whether the package must not be deleted.</returns>
	internal static bool IsForeign(PackageRowDto package, string? environmentMaintainer) =>
		package.InstallType is not null && package.InstallType != EditableInstallType
		|| environmentMaintainer is not null
			&& !string.Equals(package.Maintainer, environmentMaintainer, StringComparison.Ordinal);

	#endregion

	#region Methods: Private

	private static PackageDeletionAssessment Unavailable(string packageName, string error) =>
		new(PackageDeletionStatus.Unavailable, packageName, null, [], null, null, error);

	private static string DescribeFailure(string what, Exception exception) =>
		UntrustedText.Fenced($"{what}: {exception.Message}");

	private PackageRowDto ReadPackage(string packageName) {
		PackageSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<PackageSelectResponse>(
			_applicationClient, _serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(PackageSchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition(NameColumn, NameColumn),
					new SelectQueryHelper.SelectQueryColumnDefinition(UIdColumn, UIdColumn),
					new SelectQueryHelper.SelectQueryColumnDefinition(MaintainerColumn, MaintainerColumn),
					new SelectQueryHelper.SelectQueryColumnDefinition(InstallTypeColumn, InstallTypeColumn)
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(NameColumn, packageName,
						SelectQueryHelper.TextDataValueType)
				],
				rowCount: 2));
		return (response?.Rows ?? [])
			.FirstOrDefault(row => string.Equals(row.Name, packageName, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Reads the environment's <c>Maintainer</c> system setting, the value the platform compares a package's
	/// maintainer against when it refuses a foreign package. The setting is <c>MediumText</c>, so its value
	/// lives in <c>SysSettingsValue.TextValue</c> rather than on <c>SysSettings</c> itself.
	/// </summary>
	/// <returns>The maintainer, or <see langword="null"/> when the environment has none set — a null answer
	/// skips the maintainer half of the foreign rule, and a failed read throws for the caller to classify.</returns>
	private string? ReadEnvironmentMaintainer() {
		MaintainerSettingSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<MaintainerSettingSelectResponse>(
			_applicationClient, _serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(SettingValueSchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition(SettingValueColumn, SettingValueColumn)
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(SettingCodeColumn, MaintainerSettingCode,
						SelectQueryHelper.TextDataValueType)
				],
				rowCount: 2));
		return (response?.Rows ?? [])
			.Select(row => row.TextValue)
			.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
	}

	private IReadOnlyList<string> ReadDependents(string packageName) {
		DependencySelectResponse response = SelectQueryHelper.ExecuteSelectQuery<DependencySelectResponse>(
			_applicationClient, _serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(DependencySchema,
				[
					new SelectQueryHelper.SelectQueryColumnDefinition(DependentPackageNameColumn,
						DependentPackageNameColumn)
				],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(DependOnPackageNameFilter, packageName,
						SelectQueryHelper.TextDataValueType)
				],
				rowCount: 100, isDistinct: true));
		return DistinctNames((response?.Rows ?? []).Select(row => row.DependentPackageName));
	}

	/// <summary>
	/// Reads the three content kinds, keeping whatever read and marking the inventory partial when one fails, so a
	/// zero count reads as "not read" rather than "none". The first failure names itself in the warning.
	/// </summary>
	private (PackageDeletionInventory Inventory, string? Warning) ReadInventory(string packageName) {
		bool partial = false;
		string? warning = null;
		IReadOnlyList<string> schemas = ReadNamesSafe(SchemaSchema, packageName, ref partial, ref warning);
		IReadOnlyList<string> dataBindings = ReadNamesSafe(SchemaDataSchema, packageName, ref partial, ref warning);
		IReadOnlyList<string> sqlScripts = ReadNamesSafe(SqlScriptSchema, packageName, ref partial, ref warning);
		return (new PackageDeletionInventory(Listed(schemas), schemas.Count, Listed(dataBindings),
			dataBindings.Count, Listed(sqlScripts), sqlScripts.Count, partial), warning);
	}

	private IReadOnlyList<string> ReadNamesSafe(string rootSchema, string packageName, ref bool partial,
		ref string? warning) {
		try {
			return ReadNames(rootSchema, packageName);
		} catch (Exception exception) {
			partial = true;
			warning ??= DescribeFailure("could not read what the package holds", exception);
			return [];
		}
	}

	private IReadOnlyList<string> ReadNames(string rootSchema, string packageName) {
		NameSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<NameSelectResponse>(
			_applicationClient, _serviceUrlBuilder,
			SelectQueryHelper.BuildSelectQuery(rootSchema,
				[new SelectQueryHelper.SelectQueryColumnDefinition(NameColumn, NameColumn)],
				[
					new SelectQueryHelper.SelectQueryFilterDefinition(PackageNameFilter, packageName,
						SelectQueryHelper.TextDataValueType)
				],
				rowCount: InventoryRowCount));
		return DistinctNames((response?.Rows ?? []).Select(row => row.Name));
	}

	private static IReadOnlyList<string> DistinctNames(IEnumerable<string> names) => names
		.Where(name => !string.IsNullOrWhiteSpace(name))
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
		.ToArray();

	private static IReadOnlyList<string> Listed(IReadOnlyList<string> names) =>
		names.Count <= NamesToListed ? names : names.Take(NamesToListed).ToArray();

	#endregion

	#region Classes: Private

	private sealed class PackageSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageRowDto> Rows { get; init; } = [];
	}

	internal sealed class PackageRowDto {
		[JsonPropertyName("Name")]
		public string Name { get; init; }

		[JsonPropertyName("UId")]
		public string UId { get; init; }

		[JsonPropertyName("Maintainer")]
		public string Maintainer { get; init; }

		[JsonPropertyName("InstallType")]
		public int? InstallType { get; init; }
	}

	private sealed class DependencySelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<DependencyRowDto> Rows { get; init; } = [];
	}

	private sealed class DependencyRowDto {
		[JsonPropertyName("SysPackage.Name")]
		public string DependentPackageName { get; init; }
	}

	private sealed class NameSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<NameRowDto> Rows { get; init; } = [];
	}

	private sealed class NameRowDto {
		[JsonPropertyName("Name")]
		public string Name { get; init; }
	}

	private sealed class MaintainerSettingSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<MaintainerSettingRowDto> Rows { get; init; } = [];
	}

	private sealed class MaintainerSettingRowDto {
		[JsonPropertyName("TextValue")]
		public string TextValue { get; init; }
	}

	#endregion
}
