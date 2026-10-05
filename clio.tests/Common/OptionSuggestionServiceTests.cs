using Clio.Command;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class OptionSuggestionServiceTests : BaseClioModuleTests {
	private IOptionSuggestionService _sut;

	public override void Setup() {
		base.Setup();
		_sut = Container.GetRequiredService<IOptionSuggestionService>();
	}

	[Test]
	[Description("Prefers an option declared on the verb's own options class over an inherited option that matches equally well.")]
	public void SuggestOption_PrefersOwnOption_OverInheritedOptionWithSameTokenOverlap() {
		// Arrange
		string unknownToken = "name";

		// Act
		string suggestion = _sut.SuggestOption(typeof(UpdateEntitySchemaOptions), unknownToken);

		// Assert
		suggestion.Should().Be("schema-name",
			because: "'schema-name' is declared by update-entity-schema while 'db-name' is an inherited connection option");
	}

	[Test]
	[Description("Suggests an inherited option for a near typo when no own option is close.")]
	public void SuggestOption_ReturnsInheritedOption_ForNearTypo() {
		// Arrange
		string unknownToken = "enviroment";

		// Act
		string suggestion = _sut.SuggestOption(typeof(UpdateEntitySchemaOptions), unknownToken);

		// Assert
		suggestion.Should().Be("environment",
			because: "a one-letter typo of an inherited option is still the nearest visible option");
	}

	[Test]
	[Description("Never suggests a hidden backward-compatibility alias option.")]
	public void SuggestOption_SkipsHiddenOptions() {
		// Arrange
		string unknownToken = "package-nam";

		// Act
		string suggestion = _sut.SuggestOption(typeof(UpdateEntitySchemaOptions), unknownToken);

		// Assert
		suggestion.Should().Be("package",
			because: "the hidden alias 'package-name' is not part of the documented option surface, so the visible option it aliases is suggested");
	}

	[Test]
	[Description("Returns no suggestion when nothing is close to the requested name.")]
	public void SuggestName_ReturnsNull_WhenNothingIsClose() {
		// Arrange
		string[] knownNames = ["action", "column-name", "title"];

		// Act
		string suggestion = _sut.SuggestName("zzzzzz", knownNames);

		// Assert
		suggestion.Should().BeNull(because: "an unrelated name must not produce a misleading suggestion");
	}

	[Test]
	[Description("Suggests the known name closest to a misspelled one.")]
	public void SuggestName_ReturnsClosestKnownName() {
		// Arrange
		string[] knownNames = ["action", "column-name", "new-name", "title"];

		// Act
		string suggestion = _sut.SuggestName("colum-name", knownNames);

		// Assert
		suggestion.Should().Be("column-name", because: "'colum-name' is one letter away from 'column-name'");
	}
}
