namespace Clio.Tests.Command;

/// <summary>
/// The shape <c>AdminUnitFeatureState.FeatureState</c> takes on the wire, for the test doubles that answer it.
/// <para>
/// The platform types this one column differently in its two projections over the same row:
/// <c>AdminUnitFeatureState</c>, which the package delivery selects from over raw DataService, declares it
/// <b>Integer</b> (<c>dataValueType 4</c>), so the wire carries a JSON number; the writable
/// <c>AppFeatureState</c> declares it <b>Boolean</b> (<c>12</c>), and ATF surfaces it as a CLR
/// <see cref="bool"/>. Neither is visible from the C# models, which both declare <see cref="bool"/>.
/// </para>
/// <para>
/// Verified against a live environment with <c>clio ds -t select</c>, reading <c>rowConfig.dataValueType</c> for
/// the column in each projection. Re-probe a live environment before revisiting either assumption.
/// </para>
/// </summary>
internal static class BrandingFeatureStateWireShape {

	/// <summary>Turned OFF as <c>AdminUnitFeatureState</c> answers it over raw DataService: the JSON number 0.</summary>
	internal const string OffOverSelectQuery = "0";

	/// <summary>Still ON as <c>AdminUnitFeatureState</c> answers it over raw DataService: the JSON number 1.</summary>
	internal const string OnOverSelectQuery = "1";
}
