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
	[Description("RequireTitleLocalizations auto-derives en-US from the humanized column name when no map or scalar is supplied (columns add-batch parity).")]
	public void RequireTitleLocalizations_ShouldHumanizeColumnName_WhenNoMapOrScalarSupplied() {
		// Arrange
		const string columnName = "UsrDueDate";

		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			null, null, null, columnName, Context);

		// Assert
		result["en-US"].Should().Be("Due Date",
			because: "the en-US fallback strips the Usr prefix and space-splits PascalCase so a bare column add never fails");
	}

	[Test]
	[Description("RequireTitleLocalizations falls back to the raw column name when humanization yields no boundaries.")]
	public void RequireTitleLocalizations_ShouldUseRawColumnName_WhenHumanizationYieldsSingleToken() {
		// Arrange
		const string columnName = "Code";

		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			null, null, null, columnName, Context);

		// Assert
		result["en-US"].Should().Be("Code",
			because: "a single PascalCase token with no internal boundary stays intact as the en-US fallback");
	}

	[Test]
	[Description("RequireTitleLocalizations prefers the scalar legacy title over the legacy caption and the column name.")]
	public void RequireTitleLocalizations_ShouldPreferScalarTitle_OverCaptionAndColumnName() {
		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			null, "Scalar Title", "Scalar Caption", "UsrDueDate", Context);

		// Assert
		result["en-US"].Should().Be("Scalar Title",
			because: "the precedence is explicit en-US > scalar title > scalar caption > humanized column-name");
	}

	[Test]
	[Description("RequireTitleLocalizations prefers the scalar legacy caption over the column name when no scalar title is supplied.")]
	public void RequireTitleLocalizations_ShouldPreferScalarCaption_OverColumnName() {
		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			null, null, "Scalar Caption", "UsrDueDate", Context);

		// Assert
		result["en-US"].Should().Be("Scalar Caption",
			because: "the scalar caption outranks the humanized column name when no scalar title is present");
	}

	[Test]
	[Description("RequireTitleLocalizations honors an explicit en-US value over every scalar/column-name fallback.")]
	public void RequireTitleLocalizations_ShouldPreferExplicitEnUs_OverAllFallbacks() {
		// Arrange
		Dictionary<string, string> titleLocalizations = new() {
			["en-US"] = "Explicit Caption"
		};

		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			titleLocalizations, "Scalar Title", "Scalar Caption", "UsrDueDate", Context);

		// Assert
		result["en-US"].Should().Be("Explicit Caption",
			because: "an explicit title-localizations.en-US always wins the en-US derivation precedence");
	}

	[Test]
	[Description("RequireTitleLocalizations merges a derived en-US value with a supplied non-default culture (e.g. uk-UA).")]
	public void RequireTitleLocalizations_ShouldMergeDerivedEnUs_WithSuppliedUkUa() {
		// Arrange
		Dictionary<string, string> titleLocalizations = new() {
			["uk-UA"] = "Термін"
		};

		// Act
		IReadOnlyDictionary<string, string> result = EntitySchemaLocalizationContract.RequireTitleLocalizations(
			titleLocalizations, null, null, "UsrDueDate", Context);

		// Assert
		result["en-US"].Should().Be("Due Date",
			because: "a partial map without en-US must have en-US auto-derived from the column name");
		result["uk-UA"].Should().Be("Термін",
			because: "the supplied non-English culture must be preserved alongside the derived en-US value");
	}

	[Test]
	[Description("RequireTitleLocalizations still rejects a derived map whose explicit en-US value is Cyrillic.")]
	public void RequireTitleLocalizations_ShouldThrow_WhenExplicitEnUsCyrillicEvenWithColumnName() {
		// Arrange
		Dictionary<string, string> titleLocalizations = new() {
			["en-US"] = "Заявка"
		};

		// Act
		Action act = () => EntitySchemaLocalizationContract.RequireTitleLocalizations(
			titleLocalizations, null, null, "UsrDueDate", Context);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the script/culture guard must still reject non-English en-US even when a column-name fallback exists")
			.Which.Message.Should().Contain("en-US",
				because: "the error must identify the offending culture key");
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
