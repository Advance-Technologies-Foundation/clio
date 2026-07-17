using System;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class EntitySchemaDesignerSupportTests {
	[TestCase("Binary", 13)]
	[TestCase("Blob", 13)]
	[TestCase("Image", 14)]
	[TestCase("File", 25)]
	[Description("Resolves Binary, Image, File, and Blob alias type names through the shared entity-schema type registry.")]
	public void TryResolveDataValueType_Should_Resolve_BinaryLike_Type_Names(string typeName, int expectedValue) {
		// Arrange

		// Act
		bool resolved = EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType);

		// Assert
		resolved.Should().BeTrue(because: "supported binary-like type names should resolve through the shared type registry");
		dataValueType.Should().Be(expectedValue,
			because: "resolved binary-like type names should map to the expected runtime data value type");
	}
	
	[TestCase("SecureText", 24)]
	[TestCase("secureText", 24)]
	[TestCase("Encrypted", 24)]
	[TestCase("encrypted", 24)]
	[TestCase("Password", 24)]
	[TestCase("password", 24)]
	[Description("Resolves SecureText and its aliases (Encrypted, Password) through the shared entity-schema type registry.")]
	public void TryResolveDataValueType_Should_Resolve_SecureText_Type_Names(string typeName, int expectedValue) {
		// Arrange

		// Act
		bool resolved = EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType);

		// Assert
		resolved.Should().BeTrue(because: "SecureText and its aliases should resolve through the shared type registry");
		dataValueType.Should().Be(expectedValue,
			because: "SecureText type names should map to the expected runtime data value type 24");
	}

		[TestCase("Email", 45)]
		[TestCase("email", 45)]
		[TestCase("EmailAddress", 45)]
		[Description("Resolves Email and EmailAddress aliases through the shared entity-schema type registry.")]
		public void TryResolveDataValueType_Should_Resolve_Email_Type_Names(string typeName, int expectedValue) {
		// Arrange

		// Act
		bool resolved = EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType);

		// Assert
			resolved.Should().BeTrue(because: "Email and EmailAddress aliases should resolve through the shared type registry");
		dataValueType.Should().Be(expectedValue,
			because: "Email type names should map to the expected runtime data value type 45");
	}

	[TestCase(13, "Binary")]
	[TestCase(14, "Image")]
	[TestCase(16, "ImageLookup")]
	[TestCase(24, "SecureText")]
	[TestCase(25, "File")]
	[TestCase(45, "Email")]
	[Description("Formats binary-like, image lookup, and SecureText runtime type ids into readable names for shared schema readback.")]
	public void GetFriendlyTypeName_Should_Format_BinaryLike_Runtime_Types(int dataValueType, string expectedName) {
		// Arrange

		// Act
		string typeName = EntitySchemaDesignerSupport.GetFriendlyTypeName(dataValueType);

		// Assert
		typeName.Should().Be(expectedName,
			because: "schema readback should normalize supported runtime ids into stable human-readable type names");
	}

	[TestCase("ImageLookup", 16)]
	[TestCase("ImageLink", 16)]
	[TestCase("Image link", 16)]
	[Description("Resolves the canonical ImageLookup name, the ImageLink alias, and the 'Image link' display form through the shared entity-schema type registry.")]
	public void TryResolveDataValueType_Should_Resolve_ImageLookup_Type_Names(string typeName, int expectedValue) {
		// Arrange

		// Act
		bool resolved = EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType);

		// Assert
		resolved.Should().BeTrue(
			because: "ImageLookup and its ImageLink alias should resolve so crt.ImageInput fields can be modeled");
		dataValueType.Should().Be(expectedValue,
			because: "ImageLookup type names should map to the platform 'Image link' data value type 16");
	}

	[Description("Distinguishes the ImageLookup ('Image link') type from the binary Image type for crt.ImageInput modeling.")]
	[Test]
	public void IsImageLookupDataValueType_Should_Identify_Only_ImageLookup() {
		// Arrange

		// Act
		bool imageLookupIsImageLookup = EntitySchemaDesignerSupport.IsImageLookupDataValueType(16);
		bool binaryImageIsImageLookup = EntitySchemaDesignerSupport.IsImageLookupDataValueType(14);

		// Assert
		imageLookupIsImageLookup.Should().BeTrue(
			because: "code 16 is the ImageLookup type that crt.ImageInput binds to");
		binaryImageIsImageLookup.Should().BeFalse(
			because: "the binary Image type (code 14) must not be treated as ImageLookup");
	}

	[TestCase("General", 0)]
	[TestCase("Advanced", 1)]
	[TestCase("None", 2)]
	[TestCase("advanced", 1)]
	[TestCase("  none  ", 2)]
	[Description("Parses the friendly UsageType names case-insensitively (and trimmed) to their backend ordinals.")]
	public void TryParseUsageType_ShouldReturnOrdinal_WhenNameIsRecognized(string name, int expectedOrdinal) {
		// Arrange

		// Act
		bool parsed = EntitySchemaDesignerSupport.TryParseUsageType(name, out int ordinal);

		// Assert
		parsed.Should().BeTrue(because: "General, Advanced, and None are the recognized usage type names");
		ordinal.Should().Be(expectedOrdinal,
			because: "the friendly name must map to the backend EntitySchemaColumnUsageType ordinal");
	}

	[TestCase("Foo")]
	[TestCase("2")]
	[TestCase("")]
	[TestCase(null)]
	[Description("Rejects unrecognized, numeric, empty, and null UsageType inputs so callers can raise a friendly error.")]
	public void TryParseUsageType_ShouldReturnFalse_WhenNameIsUnrecognized(string name) {
		// Arrange

		// Act
		bool parsed = EntitySchemaDesignerSupport.TryParseUsageType(name, out int ordinal);

		// Assert
		parsed.Should().BeFalse(because: "only General/Advanced/None friendly names are accepted, not raw ints or junk");
		ordinal.Should().Be(0, because: "the out ordinal must be the default when parsing fails");
	}

	[TestCase(0, "General")]
	[TestCase(1, "Advanced")]
	[TestCase(2, "None")]
	[TestCase(99, "99")]
	[Description("Maps UsageType ordinals to friendly names and falls back to the raw ordinal for unexpected values.")]
	public void GetFriendlyUsageType_ShouldReturnFriendlyName_WhenOrdinalIsKnown(int ordinal, string expectedName) {
		// Arrange

		// Act
		string name = EntitySchemaDesignerSupport.GetFriendlyUsageType(ordinal);

		// Assert
		name.Should().Be(expectedName,
			because: "the read path surfaces UsageType as a friendly, round-trippable name (or the raw ordinal when unknown)");
	}

	[Description("Builds the implicit SysImage reference schema that every ImageLookup column points at.")]
	[Test]
	public void CreateSysImageReferenceSchema_Should_Reference_SysImage_Schema() {
		// Arrange

		// Act
		EntityDesignSchemaDto reference = EntitySchemaDesignerSupport.CreateSysImageReferenceSchema();

		// Assert
		reference.Name.Should().Be("SysImage",
			because: "ImageLookup columns reference the platform SysImage image-storage schema by name");
		reference.UId.Should().Be(new Guid("93986bfe-2dbd-46bc-9bf9-d03dfefbf3b8"),
			because: "the SysImage reference UId must match the platform schema so the server persists the link");
	}

	[TestCase("Color", 18)]
	[TestCase("color", 18)]
	[Description("Resolves the named Color token (case-insensitive) to the platform Color data value type 18.")]
	public void TryResolveDataValueType_Should_Resolve_Color_Type_Name(string typeName, int expectedValue) {
		// Arrange

		// Act
		bool resolved = EntitySchemaDesignerSupport.TryResolveDataValueType(typeName, out int dataValueType);

		// Assert
		resolved.Should().BeTrue(
			because: "the named Color token should resolve through the shared type registry so Color columns can be modeled");
		dataValueType.Should().Be(expectedValue,
			because: "the Color token should map to the platform Color data value type 18");
	}

	[Description("Formats the Color runtime type id (18) into the named Color token for schema readback.")]
	[Test]
	public void GetFriendlyTypeName_Should_Format_Color_As_Named_Token() {
		// Arrange

		// Act
		string typeName = EntitySchemaDesignerSupport.GetFriendlyTypeName(18);

		// Assert
		typeName.Should().Be("Color",
			because: "readback must report data value type 18 as the named Color token, not the raw number");
	}

	[Description("Confirms Color (18) is not classified as text-like, so text-only options never apply to a Color column.")]
	[Test]
	public void IsTextLikeDataValueType_Should_Return_False_For_Color() {
		// Arrange

		// Act
		bool colorIsTextLike = EntitySchemaDesignerSupport.IsTextLikeDataValueType(18);
		bool colorIsBinaryLike = EntitySchemaDesignerSupport.IsBinaryLikeDataValueType(18);
		bool colorIsDateTimeLike = EntitySchemaDesignerSupport.IsDateTimeLikeDataValueType(18);

		// Assert
		colorIsTextLike.Should().BeFalse(
			because: "Color derives from text server-side but must not be text-like here, or multiline/accent/format-validated/masked would wrongly apply");
		colorIsBinaryLike.Should().BeFalse(because: "Color is not a binary-like type");
		colorIsDateTimeLike.Should().BeFalse(because: "Color is not a date/time type");
	}
}
