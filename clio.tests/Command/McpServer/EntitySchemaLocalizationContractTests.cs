using System;
using System.Collections.Generic;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Verifies that the entity-schema localization contract — the single chokepoint shared by
/// create-entity-schema, update-entity-schema, modify-entity-schema-column, create-lookup, and
/// sync-schemas — enforces the ENG-91044 script/culture guard, so non-English text can never be
/// stored under the mandatory <c>en-US</c> localization key.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class EntitySchemaLocalizationContractTests {

	private const string Context = "create-entity-schema 'UsrServiceRequest'";

	[Test]
	[Description("RequireTitleLocalizations rejects a title whose only en-US value is Cyrillic.")]
	public void RequireTitleLocalizations_ShouldThrow_WhenEnUsTitleIsCyrillic() {
		// Arrange
		Dictionary<string, string> titleLocalizations = new() {
			["en-US"] = "Заявка"
		};

		// Act
		Action act = () => EntitySchemaLocalizationContract.RequireTitleLocalizations(
			titleLocalizations, null, Context);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the write contract must reject non-English text under the mandatory en-US title key")
			.Which.Message.Should().Contain("en-US",
				because: "the error must identify the culture key whose value is in the wrong script");
	}

	[Test]
	[Description("RequireTitleLocalizations accepts an English en-US title paired with a Cyrillic uk-UA title.")]
	public void RequireTitleLocalizations_ShouldReturnMap_WhenEnglishEnUsAndCyrillicUkUa() {
		// Arrange
		Dictionary<string, string> titleLocalizations = new() {
			["en-US"] = "Service Request",
			["uk-UA"] = "Заявка"
		};

		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			titleLocalizations, null, Context);

		// Assert
		result.Should().ContainKey("en-US",
			because: "a contract-valid title map with matching scripts must be returned unchanged");
		result["uk-UA"].Should().Be("Заявка",
			because: "Cyrillic text is valid under the uk-UA culture key");
	}

	[Test]
	[Description("NormalizeOptionalDescriptionLocalizations rejects a description whose en-US value is Cyrillic.")]
	public void NormalizeOptionalDescriptionLocalizations_ShouldThrow_WhenEnUsDescriptionIsCyrillic() {
		// Arrange
		Dictionary<string, string> descriptionLocalizations = new() {
			["en-US"] = "Опис проблеми"
		};

		// Act
		Action act = () => EntitySchemaLocalizationContract.NormalizeOptionalDescriptionLocalizations(
			descriptionLocalizations, null, Context);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the script/culture guard also covers description localizations on the write path");
	}
}
