using System;
using System.Collections.Generic;
using System.Text;
using ATF.Repository;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command.Branding;
using Clio.Command;
using Clio.Common;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <see cref="IPanelIconBackgroundFeatureManager"/> — the DB-writing path that turns
/// <c>UsePanelIconBackground</c> off for the All-Users role so a delivered shell background is not hidden by the
/// panel's own icon background. Each of its branches is asserted against the public contract (write performed /
/// no write / throw), never through <c>set-background-image</c>, which deliberately downgrades a failure here to
/// a warning and therefore cannot observe the throw-on-Save-failure contract at all.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PanelIconBackgroundFeatureManagerTests {

	private const string FeatureCode = "UsePanelIconBackground";
	private const string ClearCacheUrl = "http://localhost/0/rest/FeatureService/ClearFeaturesCacheForAllUsers";

	private static readonly Guid AllUsersAdminUnitId = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private static readonly Guid FeatureId = Guid.Parse("6b1c2d3e-4f50-4a6b-8c9d-0e1f2a3b4c5d");
	private static readonly Guid StateRowId = Guid.Parse("7c1d2e3f-4a50-4b6c-8d9e-0f1a2b3c4d5e");

	#region Tests: feature definition

	[Test]
	[Description("Creates the persisted Feature definition row when the environment does not have one yet, so the All-Users state row it then writes has a Feature reference to point at.")]
	public void DisableForAllUsers_Should_Create_The_Feature_Definition_When_It_Is_Missing() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: false, stateRowState: null);
		IMockSavingItem featureInsert = provider.MockSavingItem(nameof(AppFeature), SavingOperation.Insert);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		featureInsert.ReceivedCount.Should().Be(1,
			because: "a feature that the platform has never materialized has no persisted row, and the off-state row cannot reference a definition that does not exist");
	}

	[Test]
	[Description("Does not recreate the Feature definition when the environment already has one, so a re-run does not add a duplicate definition row.")]
	public void DisableForAllUsers_Should_Not_Recreate_The_Feature_Definition_When_It_Exists() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: null);
		IMockSavingItem featureInsert = provider.MockSavingItem(nameof(AppFeature), SavingOperation.Insert);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		featureInsert.ReceivedCount.Should().Be(0,
			because: "the definition is looked up by code first, and inserting a second row for an existing code would leave the environment with two definitions of the same feature");
	}

	#endregion

	#region Tests: All-Users state row

	[Test]
	[Description("Creates the All-Users AdminUnitFeatureState row when the feature has never been toggled for that role.")]
	public void DisableForAllUsers_Should_Create_The_All_Users_State_Row_When_It_Is_Missing() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: null);
		IMockSavingItem stateInsert = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		stateInsert.ReceivedCount.Should().Be(1,
			because: "the off-state is expressed as an All-Users state row, so a feature with no such row needs one written before the background can show");
	}

	[Test]
	[Description("Writes FeatureState = false on the created All-Users row rather than merely creating a row with the platform default.")]
	public void DisableForAllUsers_Should_Write_The_Off_State_On_The_Created_Row() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: null);
		IMockSavingItem stateInsert = provider
			.MockSavingItem(nameof(AppFeatureState), SavingOperation.Insert)
			.ChangedValueHas("FeatureState", false);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		stateInsert.ReceivedCount.Should().Be(1,
			because: "a created row whose FeatureState was left at its default would not turn the feature off, which is the only reason this method exists");
	}

	[Test]
	[Description("Flips an existing All-Users row that is still on to off, instead of inserting a second row for the same role.")]
	public void DisableForAllUsers_Should_Flip_An_Existing_On_Row_To_Off() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: true);
		IMockSavingItem stateUpdate = provider
			.MockSavingItem(nameof(AppFeatureState), SavingOperation.Update)
			.ChangedValueHas("FeatureState", false);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		stateUpdate.ReceivedCount.Should().Be(1,
			because: "the All-Users role already has a state row, so the feature is turned off by updating that row — a second row for the same (feature, role) pair would be ambiguous to the runtime");
	}

	[Test]
	[Description("Leaves an All-Users row that is already off untouched, so a re-run of set-background-image writes nothing.")]
	public void DisableForAllUsers_Should_Not_Write_When_The_Row_Is_Already_Off() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: false);
		IMockSavingItem stateUpdate = provider.MockSavingItem(nameof(AppFeatureState), SavingOperation.Update);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		sut.DisableForAllUsers();

		// Assert
		stateUpdate.ReceivedCount.Should().Be(0,
			because: "the method is documented as idempotent, and re-writing an already-correct row would make every background re-apply look like a configuration change");
	}

	[Test]
	[Description("Throws when the state row found through AdminUnitFeatureState cannot be re-read through AppFeatureState, instead of reporting a turn-off that never happened.")]
	public void DisableForAllUsers_Should_Throw_When_The_State_Row_Cannot_Be_Reread() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: true, rereadableState: false);
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		Action act = () => sut.DisableForAllUsers();

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the writable AppFeatureState projection is the only way to flip the row, so a read-only view of it means the feature is still on and the caller must not be told otherwise")
			.WithMessage($"*{FeatureCode}*");
	}

	#endregion

	#region Tests: Save failures

	[Test]
	[Description("Throws when the platform rejects the Save that creates the Feature definition, so a failed write is never reported as a turn-off.")]
	public void DisableForAllUsers_Should_Throw_When_The_Definition_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: false, stateRowState: null), "definition rejected");
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		Action act = () => sut.DisableForAllUsers();

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the advertised contract is that a rejected write is loud; swallowing it would let the background bind an off-state that was never written")
			.WithMessage("*definition rejected*");
	}

	[Test]
	[Description("Throws when the platform rejects the Save that writes the All-Users off-state row, naming the rejected action.")]
	public void DisableForAllUsers_Should_Throw_When_The_State_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: true, stateRowState: null), "state rejected");
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider);

		// Act
		Action act = () => sut.DisableForAllUsers();

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the off-state row is the write that actually turns the feature off, so its rejection must reach the caller rather than being absorbed silently")
			.WithMessage("*state rejected*");
	}

	[Test]
	[Description("Does not clear the feature cache when a Save was rejected, because there is no new state to publish.")]
	public void DisableForAllUsers_Should_Not_Clear_The_Cache_When_A_Save_Is_Rejected() {
		// Arrange
		IDataProvider provider = new RejectingSaveDataProvider(
			CreateProvider(featureDefined: true, stateRowState: null), "state rejected");
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider, applicationClient);

		// Act
		Action act = () => sut.DisableForAllUsers();

		// Assert
		act.Should().Throw<InvalidOperationException>();
		applicationClient.DidNotReceive().ExecuteGetRequest(Arg.Any<string>());
	}

	#endregion

	#region Tests: cache invalidation

	[Test]
	[Description("Clears the feature cache for all users after a successful write, so open sessions pick the new state up.")]
	public void DisableForAllUsers_Should_Clear_The_Feature_Cache_After_A_Successful_Write() {
		// Arrange
		DataProviderMock provider = CreateProvider(featureDefined: true, stateRowState: true);
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		string expectedUrl = $"{ClearCacheUrl}/{Convert.ToBase64String(Encoding.UTF8.GetBytes(FeatureCode))}";
		IPanelIconBackgroundFeatureManager sut = CreateSut(provider, applicationClient);

		// Act
		sut.DisableForAllUsers();

		// Assert
		applicationClient.Received(1).ExecuteGetRequest(expectedUrl);
	}

	#endregion

	#region Test doubles

	private static IPanelIconBackgroundFeatureManager CreateSut(
		IDataProvider dataProvider, IApplicationClient applicationClient = null) {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers).Returns(ClearCacheUrl);
		return new PanelIconBackgroundFeatureManager(
			dataProvider,
			applicationClient ?? Substitute.For<IApplicationClient>(),
			serviceUrlBuilder,
			Substitute.For<ILogger>());
	}

	/// <summary>
	/// Builds a fake environment: whether the persisted <c>Feature</c> definition exists, what the All-Users
	/// <c>AdminUnitFeatureState</c> row reads as (null models no row at all), and whether that row is reachable
	/// through the writable <c>AppFeatureState</c> projection.
	/// </summary>
	/// <remarks>
	/// <paramref name="stateRowState"/> is a CLR <see cref="bool"/> because that is how ATF.Repository surfaces
	/// <c>FeatureState</c> on this access path — see <see cref="BrandingFeatureStateWireShape"/>. The SAME platform
	/// column reaches <c>BrandingBindingService</c> as a JSON Integer over raw DataService, which is mocked in
	/// <c>BrandingBindingServiceTests</c>. The two shapes are both correct for their own layer; if either is ever
	/// revisited, re-probe a live environment and update both suites together.
	/// </remarks>
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
					["AdminUnitId"] = AllUsersAdminUnitId,
					["FeatureState"] = stateRowState.Value
				}
			];
		provider.MockItems(nameof(AdminUnitFeatureState)).Returns(stateRows);
		provider.MockItems(nameof(AppFeatureState)).Returns(rereadableState ? stateRows : []);
		return provider;
	}

	/// <summary>
	/// Wraps a <see cref="DataProviderMock"/> and makes every batch write fail. <see cref="DataProviderMock"/>
	/// has no rejected-save mode of its own, and the throw-on-rejection contract is exactly what this manager
	/// adds over <c>FeatureCommand</c>, so it needs a provider that answers reads normally and refuses writes.
	/// </summary>
	private sealed class RejectingSaveDataProvider(DataProviderMock inner, string errorMessage) : IDataProvider {

		public IDefaultValuesResponse GetDefaultValues(string schemaName) => inner.GetDefaultValues(schemaName);

		public IItemsResponse GetItems(ISelectQuery selectQuery) => inner.GetItems(selectQuery);

		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => new RejectedExecuteResponse(errorMessage);

		public T GetSysSettingValue<T>(string sysSettingCode) => default;

		public bool GetFeatureEnabled(string featureCode) => false;

		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) =>
			throw new NotSupportedException("The panel-icon feature manager never runs a business process.");
	}

	/// <summary>A rejected batch-write answer, as the platform returns when it refuses a Save.</summary>
	private sealed class RejectedExecuteResponse(string errorMessage) : IExecuteResponse {

		public bool Success => false;

		public List<IExecuteItemResponse> QueryResults => [];

		public string ErrorMessage { get; } = errorMessage;
	}

	#endregion
}
