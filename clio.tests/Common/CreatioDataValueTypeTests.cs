using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class CreatioDataValueTypeTests {
	[TestCase("6b6b74e2-820d-490e-a017-2b73d4ccf2b0", "Integer")]
	[TestCase("d21e9ef4-c064-4012-b286-fa1a8171da44", "DateTime")]
	[Description("Resolves canonical Creatio data value type metadata by UId.")]
	public void TryGet_ShouldReturnCanonicalType_WhenUIdIsKnown(string uId, string expectedName) {
		// Arrange
		Guid value = Guid.Parse(uId);

		// Act
		bool found = CreatioDataValueType.TryGet(value, out CreatioDataValueTypeInfo info);

		// Assert
		found.Should().BeTrue(because: "known Creatio type UIds must be available to semantic consumers");
		info.Name.Should().Be(expectedName, because: "the canonical registry owns the type name mapping");
	}

	[Test]
	[Description("The registry models exactly the platform's 49 data value type codes, with unique canonical names, so a dropped or duplicated row cannot silently change what a read surface reports.")]
	public void Types_ShouldModelEveryPlatformCode_WithUniqueNames() {
		// Arrange — the platform enum defines 49 members: codes 0, 1 and 4-50 (verified against a 10.0 stand's
		// sysenums.js; 2 and 3 are not defined). Pinning the row SET matters because the older guard only
		// checked that PRESENT rows carried both spellings: a deleted row was undetectable, and a read surface
		// answers a missing row with a raw ordinal.
		int[] expectedCodes = [0, 1, .. Enumerable.Range(4, 47)];
		IReadOnlyList<CreatioDataValueTypeInfo> types = CreatioDataValueType.Types;

		// Act
		int[] actualCodes = [.. types.Select(type => type.Code).OrderBy(code => code)];

		// Assert
		actualCodes.Should().Equal(expectedCodes,
			because: "the registry must model every platform data value type code and no invented one");
		// OrdinalIgnoreCase, not the default comparer: ByName is keyed case-insensitively, so two rows whose
		// names differ only in case are distinct to an ordinal check yet collide last-wins in the index.
		types.Select(type => type.Name).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveSameCount(types,
			because: "BuildByName is keyed OrdinalIgnoreCase and is last-wins, so two rows whose names differ "
				+ "only in case would make GetCode, GetKind and IsNumeric resolve that name to whichever row "
				+ "happens to be declared last");
		types.Should().AllSatisfy(type => type.DisplayName.Should().NotBeNullOrWhiteSpace(
			because: $"code {type.Code} must carry a display spelling or get-app-info degrades it to an ordinal"));
	}

	// Transcribed from the 49-entry table ApplicationInfoService owned at HEAD. A deliberately
	// independent copy: a typo in the registry DisplayName column fails here instead of silently
	// rewriting get-app-info output.
	private static readonly object[] HistoricalDisplayNames = [
		new object[] { 0, "Guid" },
		new object[] { 1, "Text" },
		new object[] { 4, "Integer" },
		new object[] { 5, "Float" },
		new object[] { 6, "Money" },
		new object[] { 7, "DateTime" },
		new object[] { 8, "Date" },
		new object[] { 9, "Time" },
		new object[] { 10, "Lookup" },
		new object[] { 11, "Enum" },
		new object[] { 12, "Boolean" },
		new object[] { 13, "Blob" },
		new object[] { 14, "Image" },
		new object[] { 15, "CUSTOM_OBJECT" },
		new object[] { 16, "IMAGELOOKUP" },
		new object[] { 17, "COLLECTION" },
		new object[] { 18, "Color" },
		new object[] { 19, "LOCALIZABLE_STRING" },
		new object[] { 20, "ENTITY" },
		new object[] { 21, "ENTITY_COLLECTION" },
		new object[] { 22, "ENTITY_COLUMN_MAPPING_COLLECTION" },
		new object[] { 23, "HASH_TEXT" },
		new object[] { 24, "SECURE_TEXT" },
		new object[] { 25, "FILE" },
		new object[] { 26, "MAPPING" },
		new object[] { 27, "SHORT_TEXT" },
		new object[] { 28, "MEDIUM_TEXT" },
		new object[] { 29, "MAXSIZE_TEXT" },
		new object[] { 30, "LONG_TEXT" },
		new object[] { 31, "FLOAT1" },
		new object[] { 32, "FLOAT2" },
		new object[] { 33, "FLOAT3" },
		new object[] { 34, "FLOAT4" },
		new object[] { 35, "LOCALIZABLE_PARAMETER_VALUES_LIST" },
		new object[] { 36, "METADATA_TEXT" },
		new object[] { 37, "STAGE_INDICATOR" },
		new object[] { 38, "OBJECT_LIST" },
		new object[] { 39, "COMPOSITE_OBJECT_LIST" },
		new object[] { 40, "FLOAT8" },
		new object[] { 41, "FILE_LOCATOR" },
		new object[] { 42, "PHONE_TEXT" },
		new object[] { 43, "RICH_TEXT" },
		new object[] { 44, "WEB_TEXT" },
		new object[] { 45, "EMAIL_TEXT" },
		new object[] { 46, "COMPOSITE_OBJECT" },
		new object[] { 47, "FLOAT0" },
		new object[] { 48, "MONEY0" },
		new object[] { 49, "MONEY1" },
		new object[] { 50, "MONEY3" }
	];

	[TestCaseSource(nameof(HistoricalDisplayNames))]
	[Description("Every data value type reports the exact display spelling get-app-info emitted before the duplicate type table was removed.")]
	public void GetDisplayName_ShouldMatchHistoricalSpelling_ForEveryModelledCode(int code, string expectedName) {
		// Arrange

		// Act
		string name = CreatioDataValueType.GetDisplayName(code);

		// Assert
		name.Should().Be(expectedName,
			because: "get-app-info output must stay byte-identical for every code, not only for a hand-picked sample");
	}

	// The nine scales that ENG-93202 reported as "Text" — every one of them was absent from the Data Forge map.
	[TestCase(31, "Float1")]
	[TestCase(32, "Float2")]
	[TestCase(33, "Float3")]
	[TestCase(34, "Float4")]
	[TestCase(40, "Float8")]
	[TestCase(47, "Float0")]
	[TestCase(48, "Money0")]
	[TestCase(49, "Money1")]
	[TestCase(50, "Money3")]
	// The remaining codes the Data Forge map lacked.
	[TestCase(14, "Image")]
	[TestCase(15, "CustomObject")]
	[TestCase(16, "ImageLookup")]
	[TestCase(17, "Collection")]
	[TestCase(19, "LocalizableString")]
	[TestCase(20, "Entity")]
	[TestCase(21, "EntityCollection")]
	[TestCase(22, "EntityColumnMappingCollection")]
	[TestCase(25, "File")]
	[TestCase(26, "Mapping")]
	[TestCase(35, "LocalizableParameterValuesList")]
	[TestCase(36, "MetadataText")]
	[TestCase(37, "StageIndicator")]
	[TestCase(38, "ObjectList")]
	[TestCase(39, "CompositeObjectList")]
	[TestCase(41, "FileLocator")]
	[TestCase(46, "CompositeObject")]
	// Codes that already resolved before the fix must keep resolving.
	[TestCase(1, "Text")]
	[TestCase(5, "Float")]
	[TestCase(8, "Date")]
	[TestCase(9, "Time")]
	[TestCase(11, "Enum")]
	[TestCase(23, "HashText")]
	[Description("Resolves the canonical read name for every data value type, including the decimal and money scales that used to be reported as Text.")]
	public void GetNameOrOrdinal_ShouldReturnCanonicalName_ForModelledCode(int code, string expectedName) {
		// Arrange

		// Act
		string name = CreatioDataValueType.GetNameOrOrdinal(code);

		// Assert
		name.Should().Be(expectedName,
			because: "the canonical registry owns the read-surface name for every modelled data value type");
	}

	[Test]
	[Description("An unmodelled data value type degrades to its ordinal rather than to a plausible type name, so a wrong answer cannot be mistaken for a right one.")]
	public void Resolvers_ShouldDegradeToOrdinal_WhenCodeIsNotModelled() {
		// Arrange — the ENG-93202 regression: the old Data Forge map answered "Text" for every unmapped code,
		// and because "Text" is a real type the caller had no way to tell the answer was a fallback.
		const int unmodelledCode = 999;

		// Act
		string canonicalName = CreatioDataValueType.GetNameOrOrdinal(unmodelledCode);
		string displayName = CreatioDataValueType.GetDisplayName(unmodelledCode);

		// Assert
		canonicalName.Should().NotBe("Text",
			because: "a plausible type name as a fallback is the defect ENG-93202 reported, not a safe default");
		canonicalName.Should().Be("999",
			because: "an unmodelled code must name the ordinal clio failed to model");
		displayName.Should().Be("999",
			because: "both read vocabularies share the ordinal-fallback contract");
		CreatioDataValueType.GetName(unmodelledCode).Should().BeNull(
			because: "the nullable lookup must stay honest about an unknown code for callers that can handle it");
	}

	[TestCase("Float2")]
	[TestCase("FLOAT2")]
	[TestCase("Money0")]
	[TestCase("Float1")]
	[Description("A decimal or money name emitted by a read surface is still classified as numeric by the kind classifier, so a caller does not treat a numeric column as text.")]
	public void IsNumeric_ShouldClassifyEmittedDecimalName_AsNumeric(string emittedName) {
		// Arrange — this is the property that decided the canonical vocabulary over the designer's friendly
		// vocabulary, whose decimal names (Decimal1, Currency0) are absent from the canonical name index.

		// Act
		bool numeric = CreatioDataValueType.IsNumeric(emittedName);

		// Assert
		numeric.Should().BeTrue(
			because: "the name a read surface emits must remain resolvable by the kind classifier, otherwise "
				+ "a numeric column is filtered as a lexicographic string comparison (ENG-93202)");
	}
}
