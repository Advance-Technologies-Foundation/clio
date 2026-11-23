using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;

namespace Clio.Package
{

	#region Class: ApplicationPackageListProvider

	public class ApplicationPackageListProvider : IApplicationPackageListProvider
	{

		#region Fields: Private

		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IApplicationClient _applicationClient;

		#endregion

		#region Constructors: Public

		public ApplicationPackageListProvider(IApplicationClient applicationClient,
				IServiceUrlBuilder serviceUrlBuilder) {
			applicationClient.CheckArgumentNull(nameof(applicationClient));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		public ApplicationPackageListProvider(IApplicationClient applicationClient, IJsonConverter jsonConverter,
				IServiceUrlBuilder serviceUrlBuilder)
			: this(applicationClient, serviceUrlBuilder) {
		}

		public ApplicationPackageListProvider() {
		}

		public ApplicationPackageListProvider(IJsonConverter jsonConverter) {
		}

		#endregion

		#region Methods: Private

		private static readonly IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> PackageColumns =
		[
			new("Name", "Name"),
			new("UId", "UId"),
			new("Maintainer", "Maintainer"),
			new("Version", "Version")
		];

		/// <summary>
		/// The package-type columns: <c>Type</c> (0 ordinary, 1 assembly package) and <c>ProjectPath</c>. DataService
		/// answers a column the schema does not have with <c>success:true</c>, rows without that key and the column in
		/// <c>notFoundColumns</c> (it never refuses the query), so an environment without them still lists its packages
		/// and reports the type as unknown.
		/// </summary>
		private const string TypeColumn = "Type";

		private const string ProjectPathColumn = "ProjectPath";

		private static readonly IReadOnlyList<SelectQueryHelper.SelectQueryColumnDefinition> PackageColumnsWithType =
		[
			.. PackageColumns,
			new(TypeColumn, TypeColumn),
			new(ProjectPathColumn, ProjectPathColumn)
		];

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString
		};

		private static object BuildSysPackageQuery(IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> filters) =>
			SelectQueryHelper.BuildSelectQuery("SysPackage", PackageColumnsWithType, filters);

		private static IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> ParseFilters(string scriptData) {
			if (string.IsNullOrWhiteSpace(scriptData) || scriptData.Trim() == "{}")
			{
				return [];
			}
			try
			{
				var options = JsonSerializer.Deserialize<FilterOptions>(scriptData, JsonOptions);
				if (options is { IsCustomer: true })
				{
					return
					[
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"InstallType", 0, SelectQueryHelper.IntDataValueType)
					];
				}
			}
			catch (JsonException)
			{
			}
			return [];
		}

		private static PackageInfo CreatePackageInfo(SysPackageRowDto row, bool typeColumnsExist) {
			PackageDescriptor descriptor = new() {
				Name = row.Name ?? string.Empty,
				UId = Guid.TryParse(row.UId, out Guid uid) ? uid : Guid.Empty,
				Maintainer = row.Maintainer ?? string.Empty,
				PackageVersion = row.Version ?? string.Empty,
				Type = row.Type == (int)PackageType.Assembly ? PackageType.Assembly : PackageType.General,
				ProjectPath = row.ProjectPath ?? string.Empty
			};
			return new PackageInfo(descriptor, string.Empty, []) {
				IsTypeKnown = typeColumnsExist
					&& row.Type is (int)PackageType.General or (int)PackageType.Assembly
			};
		}

		#endregion

		#region Methods: Public

		public IEnumerable<PackageInfo> GetPackages() => GetPackages("{}");

		public IEnumerable<PackageInfo> GetPackages(string scriptData) =>
			GetPackages(scriptData, Timeout.Infinite);

		/// <inheritdoc cref="IApplicationPackageListProvider.GetPackages(string, int)"/>
		public IEnumerable<PackageInfo> GetPackages(string scriptData, int requestTimeoutMs) {
			IReadOnlyList<SelectQueryHelper.SelectQueryFilterDefinition> filters = ParseFilters(scriptData);
			SysPackageSelectQueryResponseDto response =
				SelectQueryHelper.ExecuteSelectQuery<SysPackageSelectQueryResponseDto>(
					_applicationClient, _serviceUrlBuilder, BuildSysPackageQuery(filters), requestTimeoutMs);
			string[] notFound = response.NotFoundColumns ?? [];
			bool typeColumnsExist = !notFound.Contains(TypeColumn, StringComparer.OrdinalIgnoreCase)
				&& !notFound.Contains(ProjectPathColumn, StringComparer.OrdinalIgnoreCase);
			return (response.Rows ?? []).Select(row => CreatePackageInfo(row, typeColumnsExist));
		}

		#endregion

		#region Inner Types

		private sealed class FilterOptions
		{
			[JsonPropertyName("isCustomer")]
			[JsonConverter(typeof(BoolOrStringConverter))]
			public bool IsCustomer { get; set; }
		}

		private sealed class BoolOrStringConverter : JsonConverter<bool>
		{
			public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
				reader.TokenType switch {
					JsonTokenType.True => true,
					JsonTokenType.False => false,
					JsonTokenType.String => bool.TryParse(reader.GetString(), out bool v) && v,
					JsonTokenType.Number => reader.GetInt32() != 0,
					_ => false
				};

			public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
				writer.WriteBooleanValue(value);
		}

		private sealed class SysPackageSelectQueryResponseDto : SelectQueryHelper.SelectQueryResponseBaseDto
		{
			[JsonPropertyName("rows")]
			public List<SysPackageRowDto> Rows { get; set; } = [];

			[JsonPropertyName("notFoundColumns")]
			public string[]? NotFoundColumns { get; set; }
		}

		private sealed class SysPackageRowDto
		{
			[JsonPropertyName("Name")]
			public string? Name { get; set; }

			[JsonPropertyName("UId")]
			public string? UId { get; set; }

			[JsonPropertyName("Maintainer")]
			public string? Maintainer { get; set; }

			[JsonPropertyName("Version")]
			public string? Version { get; set; }

			[JsonPropertyName("Type")]
			[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
			public int? Type { get; set; }

			[JsonPropertyName("ProjectPath")]
			public string? ProjectPath { get; set; }
		}

		#endregion

	}

	#endregion

}
