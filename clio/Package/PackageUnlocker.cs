namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;
	using System.Text.Json.Serialization;
	using ATF.Repository.Providers;
	using Clio.Common;

	#region Interface: IPackageLockManager

	public interface IPackageLockManager
	{

		#region Methods: Public

		void Unlock();
		void Lock();
		void Unlock(IEnumerable<string> packages);
		void Lock(IEnumerable<string> packages);

		#endregion

	}

	#endregion

	#region Class: PackageLockManager

	public class PackageLockManager : IPackageLockManager
	{

		#region Constants: Private

		private const string SplitName = "#OriginalMaintainer:";

		#endregion

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IDataProvider _dataProvider;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;

		private static readonly JsonSerializerOptions JsonOptions = new() {
			PropertyNameCaseInsensitive = true
		};

		#endregion

		#region Constructors: Public

		public PackageLockManager(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory,
			IDataProvider dataProvider,
			IServiceUrlBuilder serviceUrlBuilder) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			dataProvider.CheckArgumentNull(nameof(dataProvider));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_dataProvider = dataProvider;
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		#endregion

		#region Methods: Private

		private IApplicationClient CreateApplicationClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private string GetMaintainerCode() =>
			_dataProvider.GetSysSettingValue<string>("Maintainer") ?? string.Empty;

		private (string Maintainer, string Description) GetPackageInfo(IApplicationClient client, string packageName) {
			object query = new {
				rootSchemaName = "SysPackage",
				operationType = 0,
				allColumns = false,
				isDistinct = false,
				ignoreDisplayValues = false,
				rowCount = 1,
				rowsOffset = -1,
				isPageable = false,
				columns = new {
					items = new Dictionary<string, object>(StringComparer.Ordinal) {
						["Maintainer"] = new { expression = new { expressionType = 0, columnPath = "Maintainer" }, orderDirection = 0, orderPosition = -1, isVisible = true },
						["Description"] = new { expression = new { expressionType = 0, columnPath = "Description" }, orderDirection = 0, orderPosition = -1, isVisible = true }
					}
				},
				filters = new {
					filterType = 6,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					logicalOperation = 0,
					items = new Dictionary<string, object>(StringComparer.Ordinal) {
						["nameFilter"] = new {
							filterType = 1,
							comparisonType = 3,
							isEnabled = true,
							trimDateTimeParameterToDate = false,
							leftExpression = new { expressionType = 0, columnPath = "Name" },
							rightExpression = new { expressionType = 2, parameter = new { value = packageName, dataValueType = 1 } }
						}
					}
				}
			};
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
			string responseJson = client.ExecutePostRequest(url, JsonSerializer.Serialize(query));
			PackageSelectResponse response = JsonSerializer.Deserialize<PackageSelectResponse>(responseJson, JsonOptions);
			PackageRow row = response?.Rows?.FirstOrDefault();
			return (row?.Maintainer ?? string.Empty, row?.Description ?? string.Empty);
		}

		private void UpdatePackage(IApplicationClient client, string packageName, int installType, string maintainer, string description) {
			object body = new {
				rootSchemaName = "SysPackage",
				columnValues = new {
					items = new Dictionary<string, object>(StringComparer.Ordinal) {
						["InstallType"] = new { expressionType = 2, parameter = new { dataValueType = 4, value = installType } },
						["Maintainer"] = new { expressionType = 2, parameter = new { dataValueType = 1, value = maintainer } },
						["Description"] = new { expressionType = 2, parameter = new { dataValueType = 1, value = description } }
					}
				},
				filters = new {
					filterType = 6,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					logicalOperation = 0,
					items = new Dictionary<string, object>(StringComparer.Ordinal) {
						["nameFilter"] = new {
							filterType = 1,
							comparisonType = 3,
							isEnabled = true,
							trimDateTimeParameterToDate = false,
							leftExpression = new { expressionType = 0, columnPath = "Name" },
							rightExpression = new { expressionType = 2, parameter = new { value = packageName, dataValueType = 1 } }
						}
					}
				}
			};
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update);
			client.ExecutePostRequest(url, JsonSerializer.Serialize(body));
		}

		private void UpdateAllByMaintainer(IApplicationClient client, string maintainerCode, int installType, bool excludeCustom = false) {
			Dictionary<string, object> filterItems = new(StringComparer.Ordinal) {
				["maintainerFilter"] = new {
					filterType = 1,
					comparisonType = 3,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					leftExpression = new { expressionType = 0, columnPath = "Maintainer" },
					rightExpression = new { expressionType = 2, parameter = new { value = maintainerCode, dataValueType = 1 } }
				}
			};
			if (excludeCustom) {
				filterItems["notCustomFilter"] = new {
					filterType = 1,
					comparisonType = 4,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					leftExpression = new { expressionType = 0, columnPath = "Name" },
					rightExpression = new { expressionType = 2, parameter = new { value = "Custom", dataValueType = 1 } }
				};
			}
			object body = new {
				rootSchemaName = "SysPackage",
				columnValues = new {
					items = new Dictionary<string, object>(StringComparer.Ordinal) {
						["InstallType"] = new { expressionType = 2, parameter = new { dataValueType = 4, value = installType } }
					}
				},
				filters = new {
					filterType = 6,
					isEnabled = true,
					trimDateTimeParameterToDate = false,
					logicalOperation = 0,
					items = filterItems
				}
			};
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update);
			client.ExecutePostRequest(url, JsonSerializer.Serialize(body));
		}

		#endregion

		#region Methods: Public

		public void Unlock(IEnumerable<string> packages) {
			string maintainerCode = GetMaintainerCode();
			IApplicationClient client = CreateApplicationClient();
			List<string> packageList = packages.ToList();
			if (!packageList.Any()) {
				UpdateAllByMaintainer(client, maintainerCode, installType: 0);
				return;
			}
			foreach (string package in packageList) {
				var (pkgMaintainer, pkgDescription) = GetPackageInfo(client, package);
				string description = pkgDescription.Contains(SplitName)
					? pkgDescription
					: pkgDescription + SplitName + pkgMaintainer;
				UpdatePackage(client, package, 0, maintainerCode, description);
			}
		}

		public void Unlock() => Unlock(Enumerable.Empty<string>());

		public void Lock(IEnumerable<string> packages) {
			string maintainerCode = GetMaintainerCode();
			IApplicationClient client = CreateApplicationClient();
			List<string> packageList = packages.ToList();
			if (!packageList.Any()) {
				UpdateAllByMaintainer(client, maintainerCode, installType: 1, excludeCustom: true);
				return;
			}
			foreach (string package in packageList) {
				var (pkgMaintainer, pkgDescription) = GetPackageInfo(client, package);
				string[] parts = pkgDescription.Split(new[] {SplitName}, StringSplitOptions.None);
				string maintainer = parts.Length > 1 ? parts.Last() : pkgMaintainer;
				string cleanDescription = parts[0];
				UpdatePackage(client, package, 1, maintainer, cleanDescription);
			}
		}

		public void Lock() => Lock(Enumerable.Empty<string>());

		#endregion

		#region Classes: Private

		private sealed class PackageSelectResponse {
			[JsonPropertyName("rows")]
			public List<PackageRow> Rows { get; set; }
		}

		private sealed class PackageRow {
			[JsonPropertyName("Maintainer")]
			public string Maintainer { get; set; }

			[JsonPropertyName("Description")]
			public string Description { get; set; }
		}

		#endregion

	}

	#endregion

}
