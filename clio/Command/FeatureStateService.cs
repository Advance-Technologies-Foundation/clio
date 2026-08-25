using System;
using System.Linq;
using System.Text;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CreatioModel;

namespace Clio.Command;

/// <summary>
/// Writes a Creatio feature's state for one admin unit through the platform's feature-toggling entities.
/// </summary>
public interface IFeatureStateService {

	/// <summary>
	/// Sets <paramref name="featureCode"/> to <paramref name="state"/> for <paramref name="adminUnitId"/> and
	/// invalidates the feature cache, so open sessions pick the new state up. The state is written through the
	/// writable <c>AppFeatureState</c> projection, so the resulting <c>AdminUnitFeatureState</c> row is the one
	/// the runtime joins against. Idempotent: a row that already reads as <paramref name="state"/> is left
	/// untouched, and the cache is invalidated only after a write that actually happened.
	/// <para>
	/// An environment that does not define the feature is left untouched rather than given a definition: a
	/// definition materialized here would carry an id no other environment shares, and with no definition there
	/// is no state row for the runtime to evaluate, so the feature is already off.
	/// </para>
	/// </summary>
	/// <param name="featureCode">Code of the feature to write.</param>
	/// <param name="adminUnitId">Id of the admin unit the state applies to.</param>
	/// <param name="state">The state to write.</param>
	/// <param name="defineIfMissing">
	/// Whether an undefined feature is defined here instead of left alone. Request it only for a caller whose
	/// effect stays on this environment: the definition it creates carries an environment-specific id, so a
	/// caller that also ships the state row in a package must not have one created for it.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the platform rejects the write, and when the row found through the read projection cannot be
	/// re-read through the writable one — both would otherwise report a state change that never happened.
	/// </exception>
	void SetFeatureState(string featureCode, Guid adminUnitId, bool state, bool defineIfMissing = false);
}

/// <inheritdoc />
internal sealed class FeatureStateService : IFeatureStateService {

	private readonly IDataProvider _dataProvider;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public FeatureStateService(
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
	public void SetFeatureState(string featureCode, Guid adminUnitId, bool state, bool defineIfMissing = false) {
		IAppDataContext context = AppDataContextFactory.GetAppDataContext(_dataProvider);

		// Filter server-side. AppFeature.Code is a mapped [SchemaProperty], so ATF pushes the predicate down the
		// same way the AdminUnitFeatureState lookup below does; a .ToList() first would fetch and deserialize
		// every feature row of the environment on every call to find one.
		AppFeature feature = context.Models<AppFeature>().FirstOrDefault(f => f.Code == featureCode);
		if (feature is null || feature.Id == Guid.Empty) {
			if (!defineIfMissing) {
				return;
			}
			feature = context.CreateModel<AppFeature>();
			feature.Code = featureCode;
			feature.Name = featureCode;
			ThrowIfSaveFailed(context.Save(), $"defining the {featureCode} feature");
		}

		string writeAction =
			$"turning the {featureCode} feature {(state ? "on" : "off")} for admin unit '{adminUnitId}'";
		AdminUnitFeatureState existing = context.Models<AdminUnitFeatureState>()
			.FirstOrDefault(s => s.FeatureId == feature.Id && s.AdminUnitId == adminUnitId);
		if (existing is null) {
			AppFeatureState created = context.CreateModel<AppFeatureState>();
			created.FeatureId = feature.Id;
			created.AdminUnitId = adminUnitId;
			created.FeatureState = state;
			ThrowIfSaveFailed(context.Save(), writeAction);
			ClearCache(featureCode);
		} else if (existing.FeatureState != state) {
			AppFeatureState writable = context.Models<AppFeatureState>().FirstOrDefault(s => s.Id == existing.Id);
			if (writable is null) {
				throw new InvalidOperationException(
					$"The state row of the {featureCode} feature for admin unit '{adminUnitId}' ({existing.Id}) " +
					"could not be re-read through AppFeatureState, so the feature state was not changed.");
			}
			writable.FeatureState = state;
			ThrowIfSaveFailed(context.Save(), writeAction);
			ClearCache(featureCode);
		}
	}

	private static void ThrowIfSaveFailed(ISaveResult result, string action) {
		if (result?.Success != true) {
			throw new InvalidOperationException(
				$"The DataService rejected {action}: {result?.ErrorMessage ?? "unknown error"}");
		}
	}

	private void ClearCache(string featureCode) {
		string base64FeatureName = Convert.ToBase64String(Encoding.UTF8.GetBytes(featureCode));
		string url =
			$"{_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers)}/{base64FeatureName}";
		string response = _applicationClient.ExecuteGetRequest(url);
		_logger.WriteInfo(response);
	}
}
