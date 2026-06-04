using System;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class ThemeArtifactBuilderTests {

	#region Constants: Private

	private const string CssTemplate =
		".<%themeCssClass%> {\n" +
		"\t--crt-font-family-body: 'Montserrat', sans-serif;\n" +
		"\t--crt-palette-primary-500: #004fd6;\n" +
		"}\n";

	#endregion

	#region Fields: Private

	private ITemplateProvider _templateProvider;
	private ThemeArtifactBuilder _builder;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_templateProvider = Substitute.For<ITemplateProvider>();
		_templateProvider.GetTemplate(Arg.Any<string>()).Returns(CssTemplate);
		_builder = new ThemeArtifactBuilder(_templateProvider);
	}

	[Test]
	[Description("Derives Title Case caption and a UUID id when neither is supplied.")]
	public void DeriveIdentifiers_ShouldDeriveCaptionAndUuidId_WhenOnlyCssClassNameProvided() {
		// Arrange
		const string cssClassName = "acme-dark-theme";

		// Act
		ThemeIdentifiers ids = _builder.DeriveIdentifiers(cssClassName);

		// Assert
		ids.CssClassName.Should().Be("acme-dark-theme", "because the class name is kept verbatim");
		ids.Caption.Should().Be("Acme Dark",
			"because the caption is Title Case of the class name");
		Guid.TryParse(ids.Id, out _).Should().BeTrue("because an absent id defaults to a generated UUID");
	}

	[Test]
	[Description("Explicit caption and id override the derived values.")]
	public void DeriveIdentifiers_ShouldUseExplicitValues_WhenCaptionAndIdProvided() {
		// Act
		ThemeIdentifiers ids = _builder.DeriveIdentifiers("acme-dark-theme", "Acme Dark Mode", "AcmeDark");

		// Assert
		ids.Caption.Should().Be("Acme Dark Mode", "because an explicit caption overrides derivation");
		ids.Id.Should().Be("AcmeDark", "because an explicit id overrides the generated UUID");
	}

	[Test]
	[Description("Validation passes for identifiers that satisfy the theme contract.")]
	public void Validate_ShouldNotThrow_WhenIdentifiersAreValid() {
		// Arrange
		ThemeIdentifiers ids = new("AcmeDark", "Acme Dark", "acme-dark-theme");

		// Act
		Action act = () => _builder.Validate(ids);

		// Assert
		act.Should().NotThrow("because all values match the theme contract");
	}

	[TestCase("1-bad")]
	[TestCase("bad class")]
	[TestCase("-leading-dash")]
	[Description("Validation rejects a cssClassName that violates the required pattern.")]
	public void Validate_ShouldThrow_WhenCssClassNameInvalid(string cssClassName) {
		// Arrange
		ThemeIdentifiers ids = new("AcmeDark", "Acme Dark", cssClassName);

		// Act
		Action act = () => _builder.Validate(ids);

		// Assert
		act.Should().Throw<ArgumentException>(
				"because cssClassName must match ^[A-Za-z][A-Za-z0-9_-]*$")
			.WithMessage("*cssClassName*");
	}

	[Test]
	[Description("Validation rejects an id with characters outside the allowed pattern.")]
	public void Validate_ShouldThrow_WhenIdInvalid() {
		// Arrange
		ThemeIdentifiers ids = new("bad id!", "Acme Dark", "acme-dark-theme");

		// Act
		Action act = () => _builder.Validate(ids);

		// Assert
		act.Should().Throw<ArgumentException>("because id must match ^[A-Za-z0-9_-]+$")
			.WithMessage("*id*");
	}

	[Test]
	[Description("Validation rejects a caption longer than 250 characters.")]
	public void Validate_ShouldThrow_WhenCaptionTooLong() {
		// Arrange
		ThemeIdentifiers ids = new("AcmeDark", new string('x', 251), "acme-dark-theme");

		// Act
		Action act = () => _builder.Validate(ids);

		// Assert
		act.Should().Throw<ArgumentException>("because caption is limited to 250 characters");
	}

	[Test]
	[Description("theme.json contains exactly id, caption and cssClassName.")]
	public void BuildThemeJson_ShouldContainExactlyContractFields() {
		// Arrange
		ThemeIdentifiers ids = new("AcmeDark", "Acme Dark", "acme-dark-theme");

		// Act
		JsonObject json = JsonNode.Parse(_builder.BuildThemeJson(ids)).AsObject();

		// Assert
		json["id"]!.GetValue<string>().Should().Be("AcmeDark", "because the id is persisted verbatim");
		json["caption"]!.GetValue<string>().Should().Be("Acme Dark", "because the caption is persisted verbatim");
		json["cssClassName"]!.GetValue<string>().Should().Be("acme-dark-theme",
			"because the class name is persisted verbatim");
		json.Count.Should().Be(3, "because theme.json holds exactly id, caption and cssClassName");
	}

	[Test]
	[Description("theme.css is scoped under the theme class with the placeholder substituted.")]
	public void BuildThemeCss_ShouldScopeUnderCssClassName() {
		// Arrange
		ThemeIdentifiers ids = new("AcmeDark", "Acme Dark", "acme-dark-theme");

		// Act
		string css = _builder.BuildThemeCss(ids);

		// Assert
		css.Should().Contain(".acme-dark-theme {", "because the baseline is scoped under the theme class")
			.And.NotContain("<%themeCssClass%>", "because the placeholder must be fully substituted");
	}

	[Test]
	[Description("Collapses adjacent '-'/'_' separators so the derived caption has no double spaces.")]
	public void DeriveIdentifiers_ShouldCollapseRepeatedSeparators_InCaption() {
		// Act
		ThemeIdentifiers ids = _builder.DeriveIdentifiers("acme_-dark-theme");

		// Assert
		ids.Caption.Should().Be("Acme Dark",
			"because adjacent separators must collapse to a single space, not produce a double space");
	}

	#endregion

}
