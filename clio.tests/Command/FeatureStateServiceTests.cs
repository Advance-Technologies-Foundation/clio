using System;
using System.Collections.Generic;
using System.Text;
using ATF.Repository;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="IFeatureStateService"/> — the DB-writing path that sets a Creatio feature's
/// state for one admin unit. Each branch is asserted against the public contract (write performed / no write /
/// throw), never through a consuming command: <c>set-background-image</c> deliberately downgrades a failure here
/// to a warning and therefore cannot observe the throw-on-Save-failure contract at all.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class FeatureStateServiceTests {

	private const string FeatureCode = "UsePanelIconBackground";
	private const string ClearCacheUrl = "http://localhost/0/rest/FeatureService/ClearFeaturesCacheForAllUsers";

	private static readonly Guid AdminUnitId = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid FeatureId = Guid.Parse("6b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid StateRowId = Guid.Parse("7c1d2e3f-4a50-4b6c-8d9e-0f1a2b3c4d5e");

	#region Tests: undefined feature

	[Test]
	[Description("Answers false and writes nothing when the environment does not define the feature, so no run ever materializes a definition row carrying an id no other environment shares.")]
	public void SetFeatureState_Should_Not_Write_Anything_When_The_Feature_Is_Not_Defined() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: false, stateRowState: null);
		IMockSavingItem featureInsert = provider.MockSavingItem(nameof(AppFeature), SavingOperation.Insert);
		IMockSavingItem stateInsert = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IFeatureStateService sut = CreateSut(provider, applicationClient);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		featureInsert.ReceivedCount.Should().Be(0,
			because: "a definition created here would carry an environment-random id, which a package binding keying the definition on its Id could not deliver — and with no definition there is no state row for the runtime to evaluate, so the feature is already off");
		stateInsert.ReceivedCount.Should().Be(0,
			because: "a state row cannot reference a definition that was deliberately not created");
		applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>());
	}

	[Test]
	[Description("Defines the feature and then writes the state when defineIfMissing is requested, so set-feature keeps working on a feature the platform has never materialized.")]
	public void SetFeatureState_Should_Define_The_Feature_When_Requested() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: false, stateRowState: null);
		IMockSavingItem featureInsert = provider.MockSavingItem(nameof(AppFeature), SavingOperation.Insert);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: true, defineIfMissing: true);

		// Assert
		featureInsert.ReceivedCount.Should().Be(1,
			because: "a caller whose effect stays on this environment has no package delivery to invalidate, so it may materialize the definition its state row needs");
	}

	[Test]
	[Description("Throws when the platform rejects the Save that defines the feature, so a failed definition is never followed by a state write reported as done.")]
	public void SetFeatureState_Should_Throw_When_The_Definition_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: false, stateRowState: null), "definition rejected");
		IFeatureStateService sut = CreateSut(provider);

		// Act
		Action act = () => sut.SetFeatureState(FeatureCode, AdminUnitId, state: true, defineIfMissing: true);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "swallowing it would leave the caller believing a feature it never defined is now toggled")
			.WithMessage("*definition rejected*");
	}

	#endregion

	#region Tests: state row

	[Test]
	[Description("Creates the admin unit's AdminUnitFeatureState row when the feature has never been toggled for that unit.")]
	public void SetFeatureState_Should_Create_The_State_Row_When_It_Is_Missing() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: null);
		IMockSavingItem stateInsert = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		stateInsert.ReceivedCount.Should().Be(1,
			because: "the runtime joins the feature against a per-admin-unit state row, so a feature with no row for the unit needs one");
	}

	[Test]
	[Description("Writes the requested state on the created row rather than merely creating a row with the platform default.")]
	public void SetFeatureState_Should_Write_The_Requested_State_On_The_Created_Row() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: null);
		IMockSavingItem stateInsert = provider
			.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert)
			.ChangedValueHas("FeatureState", false);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		stateInsert.ReceivedCount.Should().Be(1,
			because: "the caller asked for the feature to be off, and a row created with the platform default would leave it on");
	}

	[Test]
	[Description("Flips an existing row whose state differs from the requested one, instead of inserting a second row for the same admin unit.")]
	public void SetFeatureState_Should_Flip_An_Existing_Row_With_A_Different_State() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: true);
		IMockSavingItem stateUpdate = provider
			.MockSavingItem(nameof(AppFeatureState), SavingOperation.Update)
			.ChangedValueHas("FeatureState", false);
		IMockSavingItem stateInsert = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		stateUpdate.ReceivedCount.Should().Be(1,
			because: "the unit already has a row, so the state change belongs on that row");
		stateInsert.ReceivedCount.Should().Be(0,
			because: "a second row for the same feature and admin unit would make the effective state ambiguous");
	}

	[Test]
	[Description("Writes nothing when the existing row already reads as the requested state, so a re-run is a no-op.")]
	public void SetFeatureState_Should_Not_Write_When_The_Row_Already_Has_The_Requested_State() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: false);
		IMockSavingItem stateUpdate = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Update);
		IMockSavingItem stateInsert = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		stateUpdate.ReceivedCount.Should().Be(0,
			because: "rewriting the same value is a pointless round-trip on every re-run of the consuming command");
		stateInsert.ReceivedCount.Should().Be(0, because: "the row the unit needs already exists");
	}

	[Test]
	[Description("Turns a feature on as well as off, so the service is not an off-only helper of the branding flow that produced it.")]
	public void SetFeatureState_Should_Write_An_On_State_Too() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: false);
		IMockSavingItem stateUpdate = provider
			.MockSavingItem(nameof(AppFeatureState), SavingOperation.Update)
			.ChangedValueHas("FeatureState", true);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: true);

		// Assert
		stateUpdate.ReceivedCount.Should().Be(1,
			because: "the requested state is what reaches the row, in both directions — an off-only helper would leave this row untouched");
	}

	[Test]
	[Description("Throws when the row found through AdminUnitFeatureState cannot be re-read through AppFeatureState, instead of reporting a state change that never happened.")]
	public void SetFeatureState_Should_Throw_When_The_State_Row_Cannot_Be_Reread() {
		// Arrange
		DataProviderMock provider =
			CreateProvider(featureDefined: true, stateRowState: true, rereadableState: false);
		IFeatureStateService sut = CreateSut(provider);

		// Act
		Action act = () => sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the row is reachable for reading but not for writing, so answering true would claim a write that could not be issued")
			.WithMessage($"*{FeatureCode}*");
	}

	#endregion

	#region Tests: Save failures

	[Test]
	[Description("Throws when the platform rejects the Save that writes the state row, naming the feature and the admin unit.")]
	public void SetFeatureState_Should_Throw_When_The_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: true, stateRowState: null), "state rejected");
		IFeatureStateService sut = CreateSut(provider);

		// Act
		Action act = () => sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the state row is the write that actually changes the feature, so its rejection must reach the caller rather than being absorbed silently")
			.WithMessage("*state rejected*");
	}

	[Test]
	[Description("Does not invalidate the feature cache when a Save was rejected, because there is no new state to publish.")]
	public void SetFeatureState_Should_Not_Clear_The_Cache_When_The_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: true, stateRowState: null), "state rejected");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IFeatureStateService sut = CreateSut(provider, applicationClient);

		// Act
		Action act = () => sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		act.Should().Throw<InvalidOperationException>();
		applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>());
	}

	#endregion

	#region Tests: cache invalidation

	[Test]
	[Description("Leaves the feature cache alone when the row already reads as the requested state, so a re-run of a consuming command costs no extra round-trip.")]
	public void SetFeatureState_Should_Not_Clear_The_Cache_When_Nothing_Changed() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: false);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IFeatureStateService sut = CreateSut(provider, applicationClient);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>());
	}

	[Test]
	[Description("Invalidates the feature cache after a successful write, so open sessions pick the new state up.")]
	public void SetFeatureState_Should_Clear_The_Feature_Cache_After_A_Successful_Write() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: true);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string expectedUrl = $"{ClearCacheUrl}/{Convert.ToBase64String(Encoding.UTF8.GetBytes(FeatureCode))}";
		IFeatureStateService sut = CreateSut(provider, applicationClient);

		// Act
		sut.SetFeatureState(FeatureCode, AdminUnitId, state: false);

		// Assert
		applicationClient.Received(1).ExecuteGetRequest(expectedUrl);
	}

	#endregion

	#region Test doubles

	private static IFeatureStateService CreateSut(
		IDataProvider dataProvider, IApplicationClient applicationClient = null) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers).Returns(ClearCacheUrl);
		return new FeatureStateService(
			dataProvider,
			applicationClient ?? Substitute.For<IApplicationClient>(),
			serviceUrlBuilder,
			Substitute.For<ILogger>());
	}

	private static DataProviderMock CreateProvider(
		bool featureDefined, bool? stateRowState, bool rereadableState = true) {
		DataProviderMock provider = new();
		provider.MockItems(nameof(AppFeature)).Returns(featureDefined
			? [
				new Dictionary<string, object> {
					["Id"] = FeatureId,
					["Code"] = FeatureCode,
					["Name"] = FeatureCode
				}
			]
			: []);

		List<Dictionary<string, object>> stateRows = stateRowState is null
			? []
			: [
				new Dictionary<string, object> {
					["Id"] = StateRowId,
					["FeatureId"] = FeatureId,
					["AdminUnitId"] = AdminUnitId,
					["FeatureState"] = stateRowState.Value
				}
			];
		provider.MockItems(nameof(AdminUnitFeatureState)).Returns(stateRows);
		provider.MockItems(nameof(AppFeatureState)).Returns(rereadableState ? stateRows : []);
		return provider;
	}

	private sealed class RejectingSaveDataProvider(DataProviderMock inner, string errorMessage) : IDataProvider {

		public IDefaultValuesResponse GetDefaultValues(string schemaName) => inner.GetDefaultValues(schemaName);

		public IItemsResponse GetItems(ISelectQuery selectQuery) => inner.GetItems(selectQuery);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => new RejectedExecuteResponse(errorMessage);

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => false;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			throw new NotSupportedException("The feature state service never runs a business process.");
	}

	private sealed class RejectedExecuteResponse(string errorMessage) : IExecuteResponse {

		public bool Success => false;

		public List<IExecuteItemResponse> QueryResults => [];

		public string ErrorMessage { get; } = errorMessage;
	}

	#endregion
}
