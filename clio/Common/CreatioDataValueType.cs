using System;
using System.Collections.Generic;
using System.Globalization;

namespace Clio.Common;

/// <summary>
/// Classification of a Creatio data value type for filter/business-rule purposes.
/// </summary>
public enum CreatioDataValueKind {
	Guid,
	Text,
	Numeric,
	DateTime,
	Lookup,
	Boolean,
	Enum,
	/// <summary>Binary, object, collection and other types that cannot be used as a scalar filter value.</summary>
	NonFilterable
}

/// <summary>Canonical metadata for a single Creatio data value type.</summary>
/// <param name="Name">The platform SERVER enum spelling (<c>Float2</c>), used by every read surface except <c>get-app-info</c>.</param>
/// <param name="DisplayName">
/// The platform CLIENT enum spelling (<c>FLOAT2</c>, from <c>sysenums.js</c>), which only <c>get-app-info</c>
/// emits. Verified against a 10.0 stand: 35 of the 49 values match the client member name character for
/// character, and the other 14 differ in case or an underscore (<c>Guid</c>/<c>GUID</c>,
/// <c>DateTime</c>/<c>DATE_TIME</c>, <c>Color</c>/<c>COLOR</c>). The vocabulary is therefore MIXED — do not
/// "normalize" one of those 14 rows to match its neighbours, because the inconsistency is the shipped output.
/// </param>
public sealed record CreatioDataValueTypeInfo(
	int Code,
	string Name,
	CreatioDataValueKind Kind,
	Guid? UId,
	string DisplayName);

/// <summary>
/// Single source of truth for Creatio data value type CODES and for the two READ vocabularies
/// (<see cref="CreatioDataValueTypeInfo.Name"/>, <see cref="CreatioDataValueTypeInfo.DisplayName"/>), plus
/// kind classification and UId lookup. The table holds 49 rows — codes 0, 1 and 4-50; codes 2 and 3 are
/// absent because the platform enum does not define them, verified against a 10.0 stand's <c>sysenums.js</c>.
/// </summary>
/// <remarks>
/// This does NOT own the WRITE vocabulary. <c>EntitySchemaDesignerSupport.SupportedDataValueTypes</c> and
/// <c>GetFriendlyTypeName</c> are a deliberately separate, write-scoped table with its own spellings
/// (<c>decimal2</c>, <c>Currency2</c>), and they must stay separate: <c>AreColumnTypesEquivalent</c> depends
/// on their divergences from the request vocabulary to decide that an unchanged column is unchanged, so
/// folding them in here would put schema read-modify-write idempotency at risk. The two tables are bridged
/// by aliases, guarded by the round-trip tests in <c>EntitySchemaDesignerSupportTests</c>.
/// </remarks>
public static class CreatioDataValueType {

	private const string CurrencyUId = "{969093E2-2B4E-463B-883A-3D3B8C61F0CD}";

	private static readonly IReadOnlyList<CreatioDataValueTypeInfo> All = [
		new(0, "Guid", CreatioDataValueKind.Guid, new("{23018567-A13C-4320-8687-FD6F9E3699BD}"), "Guid"),
		new(1, "Text", CreatioDataValueKind.Text, new("{8B3F29BB-EA14-4CE5-A5C5-293A929B6BA2}"), "Text"),
		new(4, "Integer", CreatioDataValueKind.Numeric, new("{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}"), "Integer"),
		new(5, "Float", CreatioDataValueKind.Numeric, null, "Float"),
		new(6, "Money", CreatioDataValueKind.Numeric, new(CurrencyUId), "Money"),
		new(7, "DateTime", CreatioDataValueKind.DateTime, new("{D21E9EF4-C064-4012-B286-FA1A8171DA44}"), "DateTime"),
		new(8, "Date", CreatioDataValueKind.DateTime, null, "Date"),
		new(9, "Time", CreatioDataValueKind.DateTime, null, "Time"),
		new(10, "Lookup", CreatioDataValueKind.Lookup, new("{B295071F-7EA9-4E62-8D1A-919BF3732FF2}"), "Lookup"),
		new(11, "Enum", CreatioDataValueKind.Enum, null, "Enum"),
		new(12, "Boolean", CreatioDataValueKind.Boolean, new("{90B65BF8-0FFC-4141-8779-2420877AF907}"), "Boolean"),
		new(13, "Blob", CreatioDataValueKind.NonFilterable, null, "Blob"),
		new(14, "Image", CreatioDataValueKind.NonFilterable, null, "Image"),
		new(15, "CustomObject", CreatioDataValueKind.NonFilterable, null, "CUSTOM_OBJECT"),
		new(16, "ImageLookup", CreatioDataValueKind.NonFilterable, null, "IMAGELOOKUP"),
		new(17, "Collection", CreatioDataValueKind.NonFilterable, null, "COLLECTION"),
		new(18, "Color", CreatioDataValueKind.Text, null, "Color"),
		new(19, "LocalizableString", CreatioDataValueKind.Text, null, "LOCALIZABLE_STRING"),
		new(20, "Entity", CreatioDataValueKind.NonFilterable, null, "ENTITY"),
		new(21, "EntityCollection", CreatioDataValueKind.NonFilterable, null, "ENTITY_COLLECTION"),
		new(22, "EntityColumnMappingCollection", CreatioDataValueKind.NonFilterable, null, "ENTITY_COLUMN_MAPPING_COLLECTION"),
		new(23, "HashText", CreatioDataValueKind.Text, null, "HASH_TEXT"),
		new(24, "SecureText", CreatioDataValueKind.Text, new("{3509B9DD-2C90-4540-B82E-8F6AE85D8248}"), "SECURE_TEXT"),
		new(25, "File", CreatioDataValueKind.NonFilterable, null, "FILE"),
		new(26, "Mapping", CreatioDataValueKind.NonFilterable, null, "MAPPING"),
		new(27, "ShortText", CreatioDataValueKind.Text, new("{325A73B8-0F47-44A0-8412-7606F78003AC}"), "SHORT_TEXT"),
		new(28, "MediumText", CreatioDataValueKind.Text, new("{DDB3A1EE-07E8-4D62-B7A9-D0E618B00FBD}"), "MEDIUM_TEXT"),
		new(29, "MaxSizeText", CreatioDataValueKind.Text, new("{C0F04627-4620-4BC0-84E5-9419DC8516B1}"), "MAXSIZE_TEXT"),
		new(30, "LongText", CreatioDataValueKind.Text, new("{5CA35F10-A101-4C67-A96A-383DA6AFACFC}"), "LONG_TEXT"),
		new(31, "Float1", CreatioDataValueKind.Numeric, new("{07BA84CE-0BF7-44B4-9F2C-7B15032EB98C}"), "FLOAT1"),
		new(32, "Float2", CreatioDataValueKind.Numeric, new("{5CC8060D-6D10-4773-89FC-8C12D6F659A6}"), "FLOAT2"),
		new(33, "Float3", CreatioDataValueKind.Numeric, new("{3F62414E-6C25-4182-BCEF-A73C9E396F31}"), "FLOAT3"),
		new(34, "Float4", CreatioDataValueKind.Numeric, new("{FF22E049-4D16-46EE-A529-92D8808932DC}"), "FLOAT4"),
		new(35, "LocalizableParameterValuesList", CreatioDataValueKind.NonFilterable, null, "LOCALIZABLE_PARAMETER_VALUES_LIST"),
		new(36, "MetadataText", CreatioDataValueKind.Text, null, "METADATA_TEXT"),
		new(37, "StageIndicator", CreatioDataValueKind.NonFilterable, null, "STAGE_INDICATOR"),
		new(38, "ObjectList", CreatioDataValueKind.NonFilterable, null, "OBJECT_LIST"),
		new(39, "CompositeObjectList", CreatioDataValueKind.NonFilterable, null, "COMPOSITE_OBJECT_LIST"),
		new(40, "Float8", CreatioDataValueKind.Numeric, new("{A4AAF398-3531-4A0D-9D75-A587F5B5B59E}"), "FLOAT8"),
		new(41, "FileLocator", CreatioDataValueKind.NonFilterable, null, "FILE_LOCATOR"),
		new(42, "PhoneText", CreatioDataValueKind.Text, new("{26CBA63C-DAF1-4F36-B2EA-73C0D675D90C}"), "PHONE_TEXT"),
		new(43, "RichText", CreatioDataValueKind.Text, new("{79BCCFFA-8C8B-4863-B376-A69D2244182B}"), "RICH_TEXT"),
		new(44, "WebText", CreatioDataValueKind.Text, new("{26CBA64C-DAF1-4F36-B2EA-73C0D695D90C}"), "WEB_TEXT"),
		new(45, "EmailText", CreatioDataValueKind.Text, new("{66CBA64C-DAF1-4F36-B8EA-73C0D695D90C}"), "EMAIL_TEXT"),
		new(46, "CompositeObject", CreatioDataValueKind.NonFilterable, null, "COMPOSITE_OBJECT"),
		new(47, "Float0", CreatioDataValueKind.Numeric, new("{57EE4C31-5EC4-45FA-B95D-3A2868AA89A8}"), "FLOAT0"),
		new(48, "Money0", CreatioDataValueKind.Numeric, new(CurrencyUId), "MONEY0"),
		new(49, "Money1", CreatioDataValueKind.Numeric, new(CurrencyUId), "MONEY1"),
		new(50, "Money3", CreatioDataValueKind.Numeric, new(CurrencyUId), "MONEY3")
	];

	private static readonly IReadOnlyDictionary<int, CreatioDataValueTypeInfo> ByCode = BuildByCode();
	private static readonly IReadOnlyDictionary<string, CreatioDataValueTypeInfo> ByName = BuildByName();
	private static readonly IReadOnlyDictionary<Guid, CreatioDataValueTypeInfo> ByUId = BuildByUId();

	private static Dictionary<int, CreatioDataValueTypeInfo> BuildByCode() {
		Dictionary<int, CreatioDataValueTypeInfo> map = [];
		foreach (CreatioDataValueTypeInfo info in All) {
			map[info.Code] = info;
		}
		return map;
	}

	private static Dictionary<string, CreatioDataValueTypeInfo> BuildByName() {
		Dictionary<string, CreatioDataValueTypeInfo> map = new(StringComparer.OrdinalIgnoreCase);
		foreach (CreatioDataValueTypeInfo info in All) {
			map[info.Name] = info;
		}
		return map;
	}

	private static Dictionary<Guid, CreatioDataValueTypeInfo> BuildByUId() {
		Dictionary<Guid, CreatioDataValueTypeInfo> map = [];
		foreach (CreatioDataValueTypeInfo info in All) {
			if (info.UId is Guid uId) {
				map[uId] = info;
			}
		}
		return map;
	}

	/// <summary>
	/// Every Creatio data value type clio models, in code order. Exposed so the guard tests can enumerate the
	/// real table instead of assuming a code range. Internal until a production caller needs it.
	/// </summary>
	internal static IReadOnlyList<CreatioDataValueTypeInfo> Types => All;

	/// <summary>Gets canonical type metadata for a Creatio data value type code.</summary>
	public static bool TryGet(int code, out CreatioDataValueTypeInfo info) => ByCode.TryGetValue(code, out info!);

	/// <summary>Gets canonical type metadata for a Creatio data value type UId.</summary>
	public static bool TryGet(Guid uId, out CreatioDataValueTypeInfo info) => ByUId.TryGetValue(uId, out info!);

	/// <summary>Returns the canonical type name for a numeric code, or null when the code is unknown.</summary>
	public static string? GetName(int code) => ByCode.TryGetValue(code, out CreatioDataValueTypeInfo? info) ? info.Name : null;

	/// <summary>
	/// Canonical type name for a read surface that must always report something. An unmodelled code degrades
	/// to its ordinal, NEVER to a plausible type name — a fallback that names a real type makes a wrong
	/// answer indistinguishable from a right one (ENG-93202).
	/// </summary>
	public static string GetNameOrOrdinal(int code) => GetName(code) ?? Ordinal(code);

	/// <summary>
	/// The <c>get-app-info</c> display spelling, same ordinal-fallback contract as
	/// <see cref="GetNameOrOrdinal"/>. See <see cref="CreatioDataValueTypeInfo.DisplayName"/> for why this
	/// vocabulary exists and which surfaces may use it.
	/// </summary>
	public static string GetDisplayName(int code) =>
		ByCode.TryGetValue(code, out CreatioDataValueTypeInfo? info) ? info.DisplayName : Ordinal(code);

	// Invariant culture keeps the fallback stable across locales.
	private static string Ordinal(int code) => code.ToString(CultureInfo.InvariantCulture);

	/// <summary>Returns the numeric code for a canonical type name, or null when the name is unknown.</summary>
	public static int? GetCode(string typeName) =>
		ByName.TryGetValue(typeName, out CreatioDataValueTypeInfo? info) ? info.Code : null;

	public static CreatioDataValueKind? GetKind(string typeName) =>
		ByName.TryGetValue(typeName, out CreatioDataValueTypeInfo? info) ? info.Kind : null;

	public static bool IsText(string typeName) => GetKind(typeName) == CreatioDataValueKind.Text;
	public static bool IsNumeric(string typeName) => GetKind(typeName) == CreatioDataValueKind.Numeric;
	public static bool IsDateTime(string typeName) => GetKind(typeName) == CreatioDataValueKind.DateTime;
	public static bool IsLookupOrGuid(string typeName) =>
		GetKind(typeName) is CreatioDataValueKind.Lookup or CreatioDataValueKind.Guid;
	public static bool IsFilterable(string typeName) =>
		ByName.TryGetValue(typeName, out CreatioDataValueTypeInfo? info) && info.Kind != CreatioDataValueKind.NonFilterable;

	/// <summary>Types that the platform rejects for equality/inequality comparisons.</summary>
	public static bool IsUnsupportedForEquality(string typeName) =>
		string.Equals(typeName, "RichText", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(typeName, "Image", StringComparison.OrdinalIgnoreCase);
}
