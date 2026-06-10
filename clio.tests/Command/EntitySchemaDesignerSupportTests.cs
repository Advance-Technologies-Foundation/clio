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
}
