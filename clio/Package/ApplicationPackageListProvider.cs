using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CreatioModel;

namespace Clio.Package
{

	#region Class: ApplicationPackageListProvider

	public class ApplicationPackageListProvider : IApplicationPackageListProvider
	{

		#region Fields: Private

		private readonly IDataProvider _dataProvider;

		private static readonly JsonSerializerOptions JsonOptions = new()
		{
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString
		};

		#endregion

		#region Constructors: Public

		public ApplicationPackageListProvider(IDataProvider dataProvider) {
			dataProvider.CheckArgumentNull(nameof(dataProvider));
			_dataProvider = dataProvider;
		}

		#endregion

		#region Methods: Private

		private static bool ParseIsCustomer(string scriptData) {
			if (string.IsNullOrWhiteSpace(scriptData) || scriptData.Trim() == "{}")
			{
				return false;
			}
			try
			{
				var options = JsonSerializer.Deserialize<FilterOptions>(scriptData, JsonOptions);
				return options is { IsCustomer: true };
			}
			catch (JsonException)
			{
				return false;
			}
		}

		private static PackageInfo CreatePackageInfo(SysPackage p) {
			PackageDescriptor descriptor = new() {
				Name = p.Name ?? string.Empty,
				UId = p.UId,
				Maintainer = p.Maintainer ?? string.Empty,
				PackageVersion = p.Version ?? string.Empty
			};
			return new PackageInfo(descriptor, string.Empty, []);
		}

		#endregion

		#region Methods: Public

		public IEnumerable<PackageInfo> GetPackages() => GetPackages("{}");

		public IEnumerable<PackageInfo> GetPackages(string scriptData) {
			bool customerOnly = ParseIsCustomer(scriptData);
			IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
			IQueryable<SysPackage> query = ctx.Models<SysPackage>();
			if (customerOnly)
			{
				query = query.Where(p => p.InstallType == 0);
			}
			List<SysPackage> packages = query.ToList();
			return packages.Select(CreatePackageInfo);
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

		#endregion

	}

	#endregion

}
