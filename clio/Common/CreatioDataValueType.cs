using System;
using System.Collections.Generic;

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
public sealed record CreatioDataValueTypeInfo(int Code, string Name, CreatioDataValueKind Kind, Guid? UId);

/// <summary>
/// Single source of truth for Creatio data value types, mirroring the platform ɵDataValueType enum (codes 0-50).
/// Replaces the per-feature numeric→name dictionaries so classification and UId lookups stay consistent.
/// </summary>
public static class CreatioDataValueType {

	private const string CurrencyUId = "{969093E2-2B4E-463B-883A-3D3B8C61F0CD}";

	private static readonly IReadOnlyList<CreatioDataValueTypeInfo> All = [
		new(0, "Guid", CreatioDataValueKind.Guid, new("{23018567-A13C-4320-8687-FD6F9E3699BD}")),
		new(1, "Text", CreatioDataValueKind.Text, new("{8B3F29BB-EA14-4CE5-A5C5-293A929B6BA2}")),
		new(4, "Integer", CreatioDataValueKind.Numeric, new("{6B6B74E2-820D-490E-A017-2B73D4CCF2B0}")),
		new(5, "Float", CreatioDataValueKind.Numeric, null),
		new(6, "Money", CreatioDataValueKind.Numeric, new(CurrencyUId)),
		new(7, "DateTime", CreatioDataValueKind.DateTime, new("{D21E9EF4-C064-4012-B286-FA1A8171DA44}")),
		new(8, "Date", CreatioDataValueKind.DateTime, null),
		new(9, "Time", CreatioDataValueKind.DateTime, null),
		new(10, "Lookup", CreatioDataValueKind.Lookup, new("{B295071F-7EA9-4E62-8D1A-919BF3732FF2}")),
		new(11, "Enum", CreatioDataValueKind.Enum, null),
		new(12, "Boolean", CreatioDataValueKind.Boolean, new("{90B65BF8-0FFC-4141-8779-2420877AF907}")),
		new(13, "Blob", CreatioDataValueKind.NonFilterable, null),
		new(14, "Image", CreatioDataValueKind.NonFilterable, null),
		new(15, "CustomObject", CreatioDataValueKind.NonFilterable, null),
		new(16, "ImageLookup", CreatioDataValueKind.NonFilterable, null),
		new(17, "Collection", CreatioDataValueKind.NonFilterable, null),
		new(18, "Color", CreatioDataValueKind.Text, null),
		new(19, "LocalizableString", CreatioDataValueKind.Text, null),
		new(20, "Entity", CreatioDataValueKind.NonFilterable, null),
		new(21, "EntityCollection", CreatioDataValueKind.NonFilterable, null),
		new(22, "EntityColumnMappingCollection", CreatioDataValueKind.NonFilterable, null),
		new(23, "HashText", CreatioDataValueKind.Text, null),
		new(24, "SecureText", CreatioDataValueKind.Text, new("{3509B9DD-2C90-4540-B82E-8F6AE85D8248}")),
		new(25, "File", CreatioDataValueKind.NonFilterable, null),
		new(26, "Mapping", CreatioDataValueKind.NonFilterable, null),
		new(27, "ShortText", CreatioDataValueKind.Text, new("{325A73B8-0F47-44A0-8412-7606F78003AC}")),
		new(28, "MediumText", CreatioDataValueKind.Text, new("{DDB3A1EE-07E8-4D62-B7A9-D0E618B00FBD}")),
		new(29, "MaxSizeText", CreatioDataValueKind.Text, new("{C0F04627-4620-4BC0-84E5-9419DC8516B1}")),
		new(30, "LongText", CreatioDataValueKind.Text, new("{5CA35F10-A101-4C67-A96A-383DA6AFACFC}")),
		new(31, "Float1", CreatioDataValueKind.Numeric, new("{07BA84CE-0BF7-44B4-9F2C-7B15032EB98C}")),
		new(32, "Float2", CreatioDataValueKind.Numeric, new("{5CC8060D-6D10-4773-89FC-8C12D6F659A6}")),
		new(33, "Float3", CreatioDataValueKind.Numeric, new("{3F62414E-6C25-4182-BCEF-A73C9E396F31}")),
		new(34, "Float4", CreatioDataValueKind.Numeric, new("{FF22E049-4D16-46EE-A529-92D8808932DC}")),
		new(35, "LocalizableParameterValuesList", CreatioDataValueKind.NonFilterable, null),
		new(36, "MetadataText", CreatioDataValueKind.Text, null),
		new(37, "StageIndicator", CreatioDataValueKind.NonFilterable, null),
		new(38, "ObjectList", CreatioDataValueKind.NonFilterable, null),
		new(39, "CompositeObjectList", CreatioDataValueKind.NonFilterable, null),
		new(40, "Float8", CreatioDataValueKind.Numeric, new("{A4AAF398-3531-4A0D-9D75-A587F5B5B59E}")),
		new(41, "FileLocator", CreatioDataValueKind.NonFilterable, null),
		new(42, "PhoneText", CreatioDataValueKind.Text, new("{26CBA63C-DAF1-4F36-B2EA-73C0D675D90C}")),
		new(43, "RichText", CreatioDataValueKind.Text, new("{79BCCFFA-8C8B-4863-B376-A69D2244182B}")),
		new(44, "WebText", CreatioDataValueKind.Text, new("{26CBA64C-DAF1-4F36-B2EA-73C0D695D90C}")),
		new(45, "EmailText", CreatioDataValueKind.Text, new("{66CBA64C-DAF1-4F36-B8EA-73C0D695D90C}")),
		new(46, "CompositeObject", CreatioDataValueKind.NonFilterable, null),
		new(47, "Float0", CreatioDataValueKind.Numeric, new("{57EE4C31-5EC4-45FA-B95D-3A2868AA89A8}")),
		new(48, "Money0", CreatioDataValueKind.Numeric, new(CurrencyUId)),
		new(49, "Money1", CreatioDataValueKind.Numeric, new(CurrencyUId)),
		new(50, "Money3", CreatioDataValueKind.Numeric, new(CurrencyUId))
	];

	private static readonly IReadOnlyDictionary<int, CreatioDataValueTypeInfo> ByCode = BuildByCode();
	private static readonly IReadOnlyDictionary<string, CreatioDataValueTypeInfo> ByName = BuildByName();

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

	public static bool TryGet(int code, out CreatioDataValueTypeInfo info) => ByCode.TryGetValue(code, out info!);

	/// <summary>Returns the canonical type name for a numeric code, or null when the code is unknown.</summary>
	public static string? GetName(int code) => ByCode.TryGetValue(code, out CreatioDataValueTypeInfo? info) ? info.Name : null;

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
