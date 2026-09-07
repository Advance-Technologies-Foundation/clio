using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Issue #1320: no clio surface could change an existing entity schema's caption. The caption is what a
/// business-process lookup macro (<c>[#Lookup.&lt;Caption&gt;.&lt;Value&gt;#]</c>) resolves against, so two
/// schemas sharing one caption made such a process unsavable with no way out from clio.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SetEntitySchemaPropertiesTitleTests {

	private static SetEntitySchemaPropertiesOptions CreateOptions() =>
		new() { Package = "UsrPkg", SchemaName = "labLanguage" };

	[Test]
	[Description("A title-only invocation validates: the schema caption is a settable schema-level property, so it must not be rejected for lacking --primary-display-column.")]
	public void ValidateOptions_ShouldAcceptTheRequest_WhenOnlyATitleIsSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.Title = "Mention language";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().NotThrow(
			"because the schema caption is a schema-level property this command is expected to set on its own");
	}

	[Test]
	[Description("A --title-localizations JSON object is parsed into the normalized localization map used by the write path.")]
	public void ValidateOptions_ShouldParseTheLocalizationMap_WhenTitleLocalizationsJsonIsSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.TitleLocalizations = "{\"en-US\":\"Mention language\"}";

		// Act
		SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		options.ParsedTitleLocalizations.Should().NotBeNull(
			"because the raw JSON argument must be turned into the map the designer write path consumes");
		options.ParsedTitleLocalizations.Should().ContainKey("en-US",
			"because the supplied culture must survive normalization");
		options.ParsedTitleLocalizations!["en-US"].Should().Be("Mention language",
			"because the caption value must be carried through unchanged");
	}

	[Test]
	[Description("An already-parsed localization map (the MCP path) satisfies the at-least-one-property rule without a raw JSON string.")]
	public void ValidateOptions_ShouldAcceptTheRequest_WhenAParsedLocalizationMapIsSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.ParsedTitleLocalizations = new Dictionary<string, string> { ["en-US"] = "Mention language" };

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().NotThrow(
			"because the MCP tool supplies the localization map directly rather than as a JSON string");
	}

	[Test]
	[Description("With no settable property at all the command still fails, and the message now names the caption options as well so the caller learns the surface exists.")]
	public void ValidateOptions_ShouldNameTheCaptionOptions_WhenNoSettablePropertyIsSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<ArgumentException>(
				"because the command must not silently save a schema it was given nothing to change")
			.WithMessage("*--title*",
				"because the error should tell the caller that the schema caption is settable here");
	}

	[Test]
	[Description("Malformed --title-localizations JSON produces a readable validation error instead of a raw deserialization exception.")]
	public void ValidateOptions_ShouldReportAReadableError_WhenTitleLocalizationsJsonIsMalformed() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.TitleLocalizations = "not-json";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because an unparsable argument must be reported, not swallowed")
			.WithMessage("*title-localizations*",
				"because the message must name the argument the caller has to fix");
	}

	[Test]
	[Description("ValidateOptions_ShouldAcceptTheRequest_WhenTheLocalizationMapOmitsEnUs — the caption is merged per culture, so listing only uk-UA is legitimate: the en-US caption the schema already carries is preserved rather than required in the request.")]
	public void ValidateOptions_ShouldAcceptTheRequest_WhenTheLocalizationMapOmitsEnUs() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.TitleLocalizations = "{\"uk-UA\":\"Мова згадки\"}";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().NotThrow(
			"because unlisted cultures keep their existing caption, so en-US must not be mandatory for a rename");
		options.ParsedTitleLocalizations.Should().ContainKey("uk-UA",
			"because the single supplied culture must survive normalization");
	}

	[Test]
	[Description("ValidateOptions_ShouldRejectTheRequest_WhenAParsedLocalizationMapHasABlankCaption — the MCP surface hands the map over already deserialized, and it must be normalized through the same rules as the CLI JSON string instead of reaching the designer save and failing only at the readback check.")]
	public void ValidateOptions_ShouldRejectTheRequest_WhenAParsedLocalizationMapHasABlankCaption() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.ParsedTitleLocalizations = new Dictionary<string, string> { ["en-US"] = "   " };

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because a blank caption must be rejected before the schema is saved and published")
			.WithMessage("*title-localizations*",
				"because the message must name the argument the caller has to fix");
	}

	[Test]
	[Description("ValidateOptions_ShouldReject_WhenBothTitleAndTitleLocalizationsAreSupplied - the write path applies the map and never the scalar, so accepting both silently dropped --title on a published, destructive write and still reported success.")]
	public void ValidateOptions_ShouldReject_WhenBothTitleAndTitleLocalizationsAreSupplied() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.Title = "Car";
		options.TitleLocalizations = "{\"uk-UA\":\"Автомобіль\"}";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<ArgumentException>(
				"because the caption the caller asked for under the effective culture would never be written")
			.WithMessage("*mutually exclusive*",
				"because the caller has to learn that the two options are alternatives, not additive");
	}

	[Test]
	[Description("ValidateOptions_ShouldReject_WhenTheLocalizationMapKeyIsAnInventedCulture - CultureInfo.GetCultureInfo(name) MANUFACTURES a fallback culture for a well-formed but invented tag on ICU, so \"xx-YY\" used to pass this guard, be saved and published, and fail only at the readback.")]
	public void ValidateOptions_ShouldReject_WhenTheLocalizationMapKeyIsAnInventedCulture() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.TitleLocalizations = "{\"xx-YY\":\"Mention language\"}";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because an invented culture must be refused before the schema is saved and published")
			.WithMessage("*xx-YY*",
				"because the message must name the culture the caller has to fix");
	}

	[Test]
	[Description("ValidateOptions_ShouldReject_WhenAParsedLocalizationMapKeyIsAnInventedCulture - the MCP surface hands the map over already deserialized, so it must reach the same culture-name guard as the CLI JSON string.")]
	public void ValidateOptions_ShouldReject_WhenAParsedLocalizationMapKeyIsAnInventedCulture() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.ParsedTitleLocalizations = new Dictionary<string, string> { ["xx-YY"] = "Mention language" };

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because both surfaces must reject an invented culture rather than only the CLI one")
			.WithMessage("*xx-YY*",
				"because the message must name the culture the caller has to fix");
	}

	[Test]
	[Description("ValidateOptions_ShouldReject_WhenTitleLocalizationsJsonIsAnEmptyObject - an empty object parses cleanly, so without this branch the request reached the save with nothing to write and reported success.")]
	public void ValidateOptions_ShouldReject_WhenTitleLocalizationsJsonIsAnEmptyObject() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.TitleLocalizations = "{}";

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because a caption request that names no culture cannot change anything")
			.WithMessage("*title-localizations*",
				"because the message must name the argument the caller has to fix");
	}

	[Test]
	[Description("ValidateOptions_ShouldReject_WhenAParsedLocalizationMapCaptionIsInTheWrongScript - the ENG-91044 script guard has to run on the MCP map path too, otherwise Cyrillic text lands under en-US on a published write.")]
	public void ValidateOptions_ShouldReject_WhenAParsedLocalizationMapCaptionIsInTheWrongScript() {
		// Arrange
		SetEntitySchemaPropertiesOptions options = CreateOptions();
		options.ParsedTitleLocalizations = new Dictionary<string, string> { ["en-US"] = "Мова згадки" };

		// Act
		Action act = () => SetEntitySchemaPropertiesCommand.ValidateOptions(options);

		// Assert
		act.Should().Throw<EntitySchemaDesignerException>(
				"because a caption whose script contradicts its culture is the ENG-91044 defect this guard exists for")
			.WithMessage("*en-US*",
				"because the message must name the culture whose caption is wrong");
	}
}
