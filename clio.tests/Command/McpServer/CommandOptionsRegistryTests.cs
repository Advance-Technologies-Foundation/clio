using System;
using CommandLine;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class CommandOptionsRegistryTests {

	[Verb("fake-alpha", Aliases = ["fa", "alpha"], HelpText = "fake alpha verb")]
	private sealed class FakeAlphaOptions { }

	[Verb("fake-beta", HelpText = "fake beta verb")]
	private sealed class FakeBetaOptions { }

	[Verb("fake-alpha", HelpText = "colliding verb name")]
	private sealed class CollidingNameOptions { }

	[Verb("fake-gamma", Aliases = ["fa"], HelpText = "colliding alias")]
	private sealed class CollidingAliasOptions { }

	[Verb("fake-delta", Aliases = ["fake-beta"], HelpText = "alias shadows another verb's canonical name")]
	private sealed class AliasShadowingBetaOptions { }

	private sealed class NoVerbOptions { }

	[Test]
	[Category("Unit")]
	[Description("Maps the canonical verb name to its options type using the same [Verb] source the CLI parser reflects over.")]
	public void TryResolveOptionsType_ShouldReturnType_WhenCanonicalVerbNameMatches() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions), typeof(FakeBetaOptions)]);

		// Act
		bool resolved = registry.TryResolveOptionsType("fake-alpha", out Type optionsType);

		// Assert
		resolved.Should().BeTrue(because: "the canonical verb name is registered");
		optionsType.Should().Be(typeof(FakeAlphaOptions),
			because: "the canonical verb name must map to its declaring options type");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps every declared alias to the same options type as the canonical verb.")]
	public void TryResolveOptionsType_ShouldReturnType_WhenAliasMatches() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions)]);

		// Act
		bool firstAliasResolved = registry.TryResolveOptionsType("fa", out Type firstAliasType);
		bool secondAliasResolved = registry.TryResolveOptionsType("alpha", out Type secondAliasType);

		// Assert
		firstAliasResolved.Should().BeTrue(because: "alias 'fa' is declared on the verb");
		firstAliasType.Should().Be(typeof(FakeAlphaOptions),
			because: "an alias must resolve to the same options type as its canonical verb");
		secondAliasResolved.Should().BeTrue(because: "alias 'alpha' is declared on the verb");
		secondAliasType.Should().Be(typeof(FakeAlphaOptions),
			because: "every alias maps to the declaring options type");
	}

	[Test]
	[Category("Unit")]
	[Description("Lookups are case-insensitive on the verb/alias token.")]
	public void TryResolveOptionsType_ShouldReturnType_WhenVerbCasingDiffers() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions)]);

		// Act
		bool resolved = registry.TryResolveOptionsType("FAKE-ALPHA", out Type optionsType);

		// Assert
		resolved.Should().BeTrue(because: "verb resolution is case-insensitive");
		optionsType.Should().Be(typeof(FakeAlphaOptions),
			because: "casing must not affect the resolved options type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a miss (no throw) for an unknown command so it can be used for control flow.")]
	public void TryResolveOptionsType_ShouldReturnFalse_WhenCommandIsUnknown() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions)]);

		// Act
		bool resolved = registry.TryResolveOptionsType("does-not-exist", out Type optionsType);

		// Assert
		resolved.Should().BeFalse(because: "an unknown command must produce a miss, not a throw");
		optionsType.Should().BeNull(because: "no type is resolved for an unknown command");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a miss for a null or whitespace command without throwing.")]
	public void TryResolveOptionsType_ShouldReturnFalse_WhenCommandIsNullOrWhitespace() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions)]);

		// Act
		bool nullResolved = registry.TryResolveOptionsType(null, out Type nullType);
		bool blankResolved = registry.TryResolveOptionsType("   ", out Type blankType);

		// Assert
		nullResolved.Should().BeFalse(because: "a null command cannot resolve");
		nullType.Should().BeNull(because: "no type is resolved for a null command");
		blankResolved.Should().BeFalse(because: "a whitespace command cannot resolve");
		blankType.Should().BeNull(because: "no type is resolved for a whitespace command");
	}

	[Test]
	[Category("Unit")]
	[Description("Ignores options types that carry no [Verb] attribute.")]
	public void TryResolveOptionsType_ShouldIgnoreType_WhenNoVerbAttributePresent() {
		// Arrange
		CommandOptionsRegistry registry = new([typeof(NoVerbOptions), typeof(FakeBetaOptions)]);

		// Act
		bool betaResolved = registry.TryResolveOptionsType("fake-beta", out Type betaType);

		// Assert
		betaResolved.Should().BeTrue(because: "verb-carrying types are still registered");
		betaType.Should().Be(typeof(FakeBetaOptions),
			because: "non-verb types are skipped without breaking the rest of the scan");
		registry.KnownCommands.Should().ContainSingle(because: "only one of the supplied types carries a [Verb]");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws at construction when two distinct options types claim the same verb name.")]
	public void Constructor_ShouldThrow_WhenTwoTypesClaimSameVerbName() {
		// Arrange
		Type[] colliding = [typeof(FakeAlphaOptions), typeof(CollidingNameOptions)];

		// Act
		Action act = () => _ = new CommandOptionsRegistry(colliding);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*fake-alpha*",
				because: "a duplicate verb name is a startup configuration error, not silent last-wins");
	}

	[Test]
	[Category("Unit")]
	[Description("Detects an alias collision across distinct types and removes the ambiguous alias so it resolves to a miss (never a silent wrong target).")]
	public void TryResolveOptionsType_ShouldReturnFalse_WhenAliasCollidesAcrossTypes() {
		// Arrange — both fake-alpha and fake-gamma declare alias 'fa'.
		CommandOptionsRegistry registry = new([typeof(FakeAlphaOptions), typeof(CollidingAliasOptions)]);

		// Act
		bool ambiguousResolved = registry.TryResolveOptionsType("fa", out Type ambiguousType);
		bool canonicalAlphaResolved = registry.TryResolveOptionsType("fake-alpha", out Type alphaType);
		bool canonicalGammaResolved = registry.TryResolveOptionsType("fake-gamma", out Type gammaType);

		// Assert
		ambiguousResolved.Should().BeFalse(
			because: "an alias claimed by two distinct verbs cannot deterministically dispatch and must resolve to a miss");
		ambiguousType.Should().BeNull(because: "the ambiguous alias maps to no type");
		canonicalAlphaResolved.Should().BeTrue(because: "the canonical verb name remains usable");
		alphaType.Should().Be(typeof(FakeAlphaOptions), because: "canonical resolution is unaffected by the alias collision");
		canonicalGammaResolved.Should().BeTrue(because: "the other canonical verb name remains usable");
		gammaType.Should().Be(typeof(CollidingAliasOptions), because: "canonical resolution is unaffected by the alias collision");
	}

	[Test]
	[Category("Unit")]
	[Description("A canonical verb name always wins over an alias that claims the same token.")]
	public void TryResolveOptionsType_ShouldPreferCanonicalName_WhenAliasShadowsAnotherVerb() {
		// Arrange — 'fake-beta' is a canonical name; give another verb an alias 'fake-beta'.
		CommandOptionsRegistry registry = new([typeof(FakeBetaOptions), typeof(AliasShadowingBetaOptions)]);

		// Act
		bool resolved = registry.TryResolveOptionsType("fake-beta", out Type optionsType);

		// Assert
		resolved.Should().BeTrue(because: "a canonical verb name is never shadowed by another verb's alias");
		optionsType.Should().Be(typeof(FakeBetaOptions),
			because: "canonical names take precedence over aliases for the same token");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws ArgumentNullException when the options-type set is null.")]
	public void Constructor_ShouldThrow_WhenOptionsTypesAreNull() {
		// Arrange

		// Act
		Action act = () => _ = new CommandOptionsRegistry(null);

		// Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the registry cannot be built from a null type set");
	}

	[Test]
	[Category("Unit")]
	[Description("The production parameterless registry builds without collisions over the real verb set.")]
	public void Constructor_ShouldBuildSuccessfully_WhenUsingProductionVerbSet() {
		// Arrange

		// Act
		Action act = () => _ = new CommandOptionsRegistry();

		// Assert
		act.Should().NotThrow(
			because: "the real CLI verb set must be collision-free, otherwise the parser itself would be ambiguous");
	}
}
