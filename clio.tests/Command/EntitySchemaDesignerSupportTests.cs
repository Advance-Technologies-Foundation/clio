using System;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using Terrasoft.Core.Entities;
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

	[Description("Parses a Sequence mask 'LN-{0}' into the static prefix so a created record honors the full mask (LN-00001).")]
	[Test]
	public void CreateDefaultValueDto_Should_Extract_Sequence_Prefix_From_Mask() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "LN-{0}",
			SequenceNumberOfChars = 5
		};

		// Act
		EntitySchemaColumnDefValueDto dto = EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		dto.ValueSourceType.Should().Be(EntitySchemaColumnDefSource.Sequence,
			because: "the resolved DTO must keep the Sequence source so the platform applies autonumbering");
		dto.SequencePrefix.Should().Be("LN-",
			because: "the static text before '{0}' in the mask must become the sequence prefix instead of being dropped");
		dto.SequenceNumberOfChars.Should().Be(5,
			because: "the requested sequence width must be preserved alongside the extracted prefix");
	}

	[Description("A Sequence mask that is only the placeholder '{0}' yields no prefix, matching a bare sequence number.")]
	[Test]
	public void CreateDefaultValueDto_Should_Yield_No_Prefix_When_Mask_Is_Only_Placeholder() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "{0}",
			SequenceNumberOfChars = 5
		};

		// Act
		EntitySchemaColumnDefValueDto dto = EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		dto.SequencePrefix.Should().BeNullOrEmpty(
			because: "a mask with no static text before '{0}' must produce a prefix-free sequence default");
		dto.SequenceNumberOfChars.Should().Be(5,
			because: "the sequence width must still be applied when no prefix is present");
	}

	[Description("An explicit sequence-prefix (no mask) still works, preserving backward-compatible configuration.")]
	[Test]
	public void CreateDefaultValueDto_Should_Honor_Explicit_Sequence_Prefix() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			SequencePrefix = "LN-",
			SequenceNumberOfChars = 5
		};

		// Act
		EntitySchemaColumnDefValueDto dto = EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		dto.SequencePrefix.Should().Be("LN-",
			because: "an explicit sequence-prefix remains the supported way to configure the static prefix");
	}

	[TestCase("LN-{0}-END", TestName = "Suffix after placeholder")]
	[TestCase("LN-{0}{0}", TestName = "Repeated placeholder")]
	[Description("A Sequence mask with a suffix or repeated placeholder is rejected instead of silently dropping the unsupported part.")]
	public void CreateDefaultValueDto_Should_Reject_Unsupported_Sequence_Mask(string mask) {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = mask,
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "only a static prefix before a single trailing '{0}' is supported; other masks must fail loudly, not silently")
			.WithMessage("*not supported*");
	}

	[Description("A Sequence mask that omits the '{0}' placeholder is rejected so the caller cannot mistake a literal string for a mask.")]
	[Test]
	public void CreateDefaultValueDto_Should_Reject_Sequence_Mask_Without_Placeholder() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "LN-",
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "a Sequence value that is not a mask must be rejected rather than treated as a full prefix and silently misapplied")
			.WithMessage("*{0}*");
	}

	[Description("Setting both a Sequence mask value and an explicit sequence-prefix is rejected to avoid an ambiguous prefix.")]
	[Test]
	public void CreateDefaultValueDto_Should_Reject_Sequence_Value_And_Prefix_Together() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "LN-{0}",
			SequencePrefix = "XX-",
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "a mask and an explicit prefix are two ways to set the same thing; combining them is ambiguous and must be rejected")
			.WithMessage("*cannot combine*");
	}

	[Description("The two-pass request path (normalize then build DTO) preserves a mask's trailing space verbatim instead of trimming it, so 'INV {0}' numbers records as 'INV 00001'.")]
	[Test]
	public void ResolveThenCreateDefaultValueDto_Should_Preserve_Sequence_Mask_Edge_Whitespace() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "INV {0}",
			SequenceNumberOfChars = 5
		};

		// Act
		EntitySchemaDefaultValueConfig? normalized = EntitySchemaDesignerSupport.ResolveDefaultValueConfig(
			config, null, null, "Column 'UsrName'");
		EntitySchemaColumnDefValueDto dto = EntitySchemaDesignerSupport.CreateDefaultValueDto(
			normalized!, "Column 'UsrName'");

		// Assert
		dto.SequencePrefix.Should().Be("INV ",
			because: "the request path normalizes then builds the DTO, and the mask must be parsed once so the trailing space survives rather than being silently trimmed (ENG-93375)");
		dto.SequenceNumberOfChars.Should().Be(5,
			because: "the sequence width must round-trip through the two-pass request path");
	}

	[Description("Setting value-source on a Sequence default is rejected, since a sequence has no external selector to resolve.")]
	[Test]
	public void CreateDefaultValueDto_Should_Reject_Sequence_With_ValueSource() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			ValueSource = "SomeSetting",
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.CreateDefaultValueDto(config, "Column 'UsrName'");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "value-source belongs to Settings/SystemValue defaults; a Sequence has no external selector and must reject it rather than ignore it")
			.WithMessage("*value-source*");
	}

	[Description("A non-text-like column rejects a Sequence default regardless of the mask, since autonumbering applies only to text columns.")]
	[Test]
	public void ValidateDefaultValueConfig_Should_Reject_Sequence_On_NonText_Column() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "LN-{0}",
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.ValidateDefaultValueConfig(config, 4, "Column 'UsrAmount'");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
			because: "the Sequence source is valid only for text columns, so a mask on a numeric column must still be rejected")
			.WithMessage("*Sequence only for text columns*");
	}

	[Description("A valid Sequence mask on a text column passes validation, so the round-trip configuration is accepted end-to-end.")]
	[Test]
	public void ValidateDefaultValueConfig_Should_Accept_Sequence_Mask_On_Text_Column() {
		// Arrange
		EntitySchemaDefaultValueConfig config = new() {
			Source = "Sequence",
			Value = "LN-{0}",
			SequenceNumberOfChars = 5
		};

		// Act
		Action act = () => EntitySchemaDesignerSupport.ValidateDefaultValueConfig(config, 1, "Column 'UsrName'");

		// Assert
		act.Should().NotThrow(
			because: "a static-prefix mask on a text column is a supported Sequence configuration");
	}
}
