namespace Clio.Tests.Command;

/// <summary>
/// The verified shape of <c>AdminUnitFeatureState.FeatureState</c> on each of the two access paths branding uses,
/// shared by the two suites that mock them so neither can drift alone.
/// <para>
/// This exists because a mismatch here already shipped a defect once: the binding read had been written to accept
/// only a JSON Boolean, the test double obligingly answered <c>"false"</c>, every suite stayed green, and on a real
/// environment the panel-icon off-state was never packaged — the platform answers an Integer there. A test double's
/// shape must come from a live <c>rowConfig</c>, never from the C# type of the model that happens to read it.
/// </para>
/// <para>
/// The platform declares this ONE column differently in its two projections over the same row:
/// <list type="bullet">
/// <item>
/// <description>
/// <c>AdminUnitFeatureState</c> — the read projection <c>BrandingBindingService</c> selects from over raw
/// DataService — types it as <b>Integer</b> (<c>dataValueType 4</c>), so the wire carries a JSON number:
/// <see cref="OffOverSelectQuery"/> / <see cref="OnOverSelectQuery"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>AppFeatureState</c> — the writable projection <c>PanelIconBackgroundFeatureManager</c> goes through via
/// ATF.Repository — types it as <b>Boolean</b> (<c>dataValueType 12</c>), and ATF surfaces the column as a CLR
/// <see cref="bool"/> (it coerces the Integer silently, which is exactly why the manager side never noticed the
/// difference): <see cref="OffOverAtfModel"/> / <see cref="OnOverAtfModel"/>.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// Verified against a live environment with <c>clio ds -t select</c>, reading <c>rowConfig.dataValueType</c> for
/// the column in each projection. If either assumption is ever revisited, re-probe a live environment and update
/// BOTH suites together — that is what this shared type is for.
/// </para>
/// </summary>
internal static class BrandingFeatureStateWireShape {

	/// <summary>Turned OFF as <c>AdminUnitFeatureState</c> answers it over raw DataService: the JSON number 0.</summary>
	internal const string OffOverSelectQuery = "0";

	/// <summary>Still ON as <c>AdminUnitFeatureState</c> answers it over raw DataService: the JSON number 1.</summary>
	internal const string OnOverSelectQuery = "1";

	/// <summary>Turned OFF as ATF.Repository surfaces <c>AppFeatureState.FeatureState</c>: a CLR false.</summary>
	internal const bool OffOverAtfModel = false;

	/// <summary>Still ON as ATF.Repository surfaces <c>AppFeatureState.FeatureState</c>: a CLR true.</summary>
	internal const bool OnOverAtfModel = true;

	/// <summary>
	/// Tokens that are NO on/off answer at all. They exist because the binding read has a third outcome besides
	/// on and off: unreadable, which is reported with its own wording ("not readable as an on/off value") because
	/// "still on" would tell the caller to turn a feature off that may already be off. Only the two shapes above
	/// are ever expected from a real environment — these model a platform change, a proxy that rewrites scalars,
	/// or a corrupted row, and every one of them must be refused exactly like a still-on state.
	/// </summary>
	internal static readonly string[] UnreadableOverSelectQuery = ["\"maybe\"", "null", "{}"];
}
