using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit tests for <see cref="SchemaDesignerHelper.ApplySchemaMetadata"/> — the shared caption/description
/// write chokepoint used by create-sql-schema and create-source-code-schema. Verifies the ENG-91044
/// script/culture guard fires here so non-matching-script captions cannot be stored under a Latin-script
/// culture.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SchemaDesignerHelperTests {

	[Test]
	[Description("ApplySchemaMetadata rejects a Cyrillic caption under the Latin-script en-US culture.")]
	public void ApplySchemaMetadata_ShouldThrow_WhenCaptionScriptMismatchesLatinCulture() {
		// Arrange
		JObject schema = new();

		// Act
		Action act = () => SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Заявка", null, "en-US");

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				because: "a Cyrillic caption must not be stored under the Latin-script en-US culture (ENG-91044)")
			.Which.Message.Should().Contain("en-US",
				because: "the error must name the culture whose value is in the wrong script");
	}

	[Test]
	[Description("ApplySchemaMetadata allows a Cyrillic caption under the Cyrillic-script uk-UA culture.")]
	public void ApplySchemaMetadata_ShouldApply_WhenCaptionMatchesCyrillicCulture() {
		// Arrange
		JObject schema = new();

		// Act
		Action act = () => SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Заявка", null, "uk-UA");

		// Assert
		act.Should().NotThrow(
			"because a Cyrillic caption is correct under the uk-UA culture and must not be rejected");
	}

	[Test]
	[Description("ApplySchemaMetadata applies an English caption under en-US and writes the localized caption array.")]
	public void ApplySchemaMetadata_ShouldApply_WhenEnglishCaptionUnderEnUs() {
		// Arrange
		JObject schema = new();

		// Act
		SchemaDesignerHelper.ApplySchemaMetadata(schema, "UsrSchema", "Orders", "Order workspace", "en-US");

		// Assert
		schema["caption"].Should().NotBeNull(
			"because a valid English caption under en-US must be applied to the schema payload");
		schema["name"]!.ToString().Should().Be("UsrSchema",
			"because the schema name is applied verbatim alongside the localized caption");
	}
}
