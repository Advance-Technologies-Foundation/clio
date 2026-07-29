using System;
using System.Linq;
using System.Text;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CreatioModel;

namespace Clio.Command;

/// <summary>
/// Turns the platform <c>UsePanelIconBackground</c> feature off for everyone so a custom shell background
/// actually shows. While the feature is on the panel renders its own icon background, which overrides the
/// background image a <c>set-background-image</c> flow applied — so applying a background must also disable it.
/// The off-state is written as an <c>AdminUnitFeatureState</c> row for the platform-stable "All employees"
/// role (the same All-Users unit the branding sys-setting bindings key on), which the background binding
/// then ships with the package so the target site inherits the same off-state.
/// </summary>
public interface IPanelIconBackgroundFeatureManager {

	/// <summary>
	/// Disables <c>UsePanelIconBackground</c> for the All-Users role and clears the feature cache. The
	/// off-state is written through the platform's feature-toggling entities — the same path <c>set-feature</c>
	/// uses — so the persisted <c>Feature</c> definition row is materialized and the resulting All-Users
	/// <c>AdminUnitFeatureState</c> row is the one the runtime actually joins against. Idempotent: a row that
	/// is already off is left untouched. Throws when the platform rejects a write.
	/// </summary>
	void DisableForAllUsers();
}

/// <inheritdoc />
public sealed class PanelIconBackgroundFeatureManager : IPanelIconBackgroundFeatureManager {

	/// <summary>The feature code that gates the panel's own icon background.</summary>
	internal const string FeatureCode = "UsePanelIconBackground";

	private static readonly Guid AllUsersAdminUnitId = new("a29a3ba5-4b0d-de11-9a51-005056c00008");

	private readonly IDataProvider _dataProvider;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	/// <summary>Initializes a new instance of the <see cref="PanelIconBackgroundFeatureManager"/> class.</summary>
	public PanelIconBackgroundFeatureManager(
		IDataProvider dataProvider,
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		_dataProvider = dataProvider;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc />
	public void DisableForAllUsers() {
		IAppDataContext context = AppDataContextFactory.GetAppDataContext(_dataProvider);

		AppFeature feature = context.Models<AppFeature>().ToList().FirstOrDefault(f => f.Code == FeatureCode);
		if (feature is null || feature.Id == Guid.Empty) {
			feature = context.CreateModel<AppFeature>();
			feature.Code = FeatureCode;
			feature.Name = FeatureCode;
			ThrowIfSaveFailed(context.Save(), $"creating the {FeatureCode} feature definition");
		}

		AdminUnitFeatureState existing = context.Models<AdminUnitFeatureState>()
			.FirstOrDefault(s => s.FeatureId == feature.Id && s.AdminUnitId == AllUsersAdminUnitId);
		if (existing is null) {
			AppFeatureState state = context.CreateModel<AppFeatureState>();
			state.FeatureId = feature.Id;
			state.AdminUnitId = AllUsersAdminUnitId;
			state.FeatureState = false;
			ThrowIfSaveFailed(context.Save(), $"turning the {FeatureCode} feature off for all users");
		} else if (existing.FeatureState) {
			AppFeatureState state = context.Models<AppFeatureState>().FirstOrDefault(s => s.Id == existing.Id);
			if (state is null) {
				throw new InvalidOperationException(
					$"The All-Users state row of the {FeatureCode} feature ({existing.Id}) could not be re-read " +
					"through AppFeatureState, so the feature was not turned off.");
			}
			state.FeatureState = false;
			ThrowIfSaveFailed(context.Save(), $"turning the {FeatureCode} feature off for all users");
		}

		ClearCache();
	}

	private static void ThrowIfSaveFailed(ISaveResult result, string action) {
		if (result?.Success != true) {
			throw new InvalidOperationException(
				$"The DataService rejected {action}: {result?.ErrorMessage ?? "unknown error"}");
		}
	}

	private void ClearCache() {
		string base64FeatureName = Convert.ToBase64String(Encoding.UTF8.GetBytes(FeatureCode));
		string url =
			$"{_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers)}/{base64FeatureName}";
		string response = _applicationClient.ExecuteGetRequest(url);
		_logger.WriteInfo(response);
	}
}
