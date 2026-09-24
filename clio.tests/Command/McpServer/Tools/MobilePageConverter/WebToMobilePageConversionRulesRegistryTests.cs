using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// ENG-96589 — the bundled conversion RULES may only name mobile properties the registry declares.
/// <para>
/// This is the invariant the ticket was opened over, checked from the side nothing else checks. The prune
/// removes an undeclared property the PAGE carried; it deliberately never touches one a rule declared
/// (<c>DeclaredByRule</c> is a hard <c>continue</c>), because a rules author is stating a mobile fact and
/// the converter is not entitled to overrule it. That exemption is what let
/// <c>iconPosition</c> / <c>scrollable</c> / <c>bodyBackgroundColor</c> ship for as long as they did: the
/// rules file injected them itself, so no amount of prune coverage could have caught them.
/// </para>
/// <para>
/// Both failure directions are silent. On the declaredElements path the undeclared key is written into the
/// mobile page and the runtime ignores it — or, for a layout property, mis-renders. On the merge path the
/// same name written by the PAGE is removed, so the rules file and the converter disagree about one
/// element while the response still looks self-consistent.
/// </para>
/// </summary>
/// <remarks>
/// Scope is what can be checked STATICALLY: <see cref="TemplateMappingRule.DeclaredElements"/>, which names
/// its mobile <c>type</c> outright, and a <c>componentPropertyOverrides</c> entry whose filters name exactly
/// one type. An override spanning several types, or none, is skipped rather than guessed at — the same
/// fail-open discipline the prune applies to a type the registry does not describe. A
/// <c>viewConfigTemplates</c> entry stamps values onto whatever element its filters match at conversion
/// time, so it has no statically known target; widening this fixture to cover it needs a conversion, not a
/// lookup.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class WebToMobilePageConversionRulesRegistryTests {

	[Test]
	[Description("Every property the bundled rules DECLARE on a mobile element is declared by that element's type in the pinned registry. The prune exempts rule-declared values on purpose, so this is the only guard standing between a rules edit and a property mobile ignores — the exact defect ENG-96589 was opened for, on the exact path that hid it.")]
	public void BundledRules_DeclaredElementValues_ShouldBeDeclaredByTheirMobileType() {
		// Arrange
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();
		ComponentCatalogState catalog = LiveMobileCatalog();
		List<string> undeclared = [];
		int checkedPairs = 0;

		// Act
		foreach (DeclaredElementRule declared in rules.Templates.SelectMany(t => t.DeclaredElements)) {
			if (declared.Type is null || !catalog.Lookup.TryGetValue(declared.Type, out ComponentRegistryEntry entry)) {
				// Fail OPEN exactly like the prune: a type the registry does not describe cannot answer the
				// question, and inventing an answer here would make a registry regression look like a rules bug.
				continue;
			}
			IReadOnlySet<string> allowed = DeclaredNames(entry, catalog);
			foreach (string property in declared.Values.Keys) {
				checkedPairs++;
				if (!allowed.Contains(property)) {
					undeclared.Add($"{declared.Name} ({declared.Type}) declares '{property}'");
				}
			}
		}

		// Assert
		checkedPairs.Should().BeGreaterThan(0,
			because: "a run that checked no pair would pass without reading the rules file at all — the bundled "
				+ "rules do declare element values, and a zero here means the model or the fixture stopped lining "
				+ "up. Do not over-read a green: today the bundled rules carry exactly TWO declared values, both "
				+ "crt.TabPanel.isScrollable, because the three crt.TabContainer declarations carry only a "
				+ "captionResource. This fixture's job is to catch the NEXT value a rules author adds");
		undeclared.Should().BeEmpty(
			because: "the prune skips a rule-declared value by design, so nothing downstream removes one of these "
				+ "and nothing else reports it: the property is written into the mobile page and the runtime either "
				+ "ignores it or mis-renders. " + string.Join("; ", undeclared));
	}

	[Test]
	[Description("The mobile type of every bundled declaredElement is described by the registry. The test above fails OPEN on an unknown type, so without this one a producer rename would silently empty it out and both tests would stay green while the rules named a component that no longer exists.")]
	public void BundledRules_DeclaredElementTypes_ShouldExistInTheRegistry() {
		// Arrange
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();
		ComponentCatalogState catalog = LiveMobileCatalog();

		// Act
		string[] declaredTypes = rules.Templates
			.SelectMany(t => t.DeclaredElements)
			.Select(d => d.Type)
			.Where(type => !string.IsNullOrWhiteSpace(type))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		// Assert
		declaredTypes.Should().NotBeEmpty(
			because: "the bundled rules declare elements, and an empty set would make the assertion below vacuous");
		declaredTypes.Should().OnlyContain(type => catalog.Lookup.ContainsKey(type),
			because: "a declared element is INSERTED into the mobile page, so a type the registry does not describe "
				+ "is one the conversion cannot place and the value check above would skip in silence");
	}

	[Test]
	[Description("Every property a componentPropertyOverrides entry READS or WRITES is declared by the mobile type its filters name. This is the rule class the prune is COUPLED to: ApplyComponentPropertyOverrides selects by reading the element's post-prune values, and an ABSENT property never matches — so an override that filtered on an undeclared key would be silently disabled by the prune, with no normalizations entry and no skip record. It is latent only while every bundled override filters on `type`, which the prune never removes; this test is what keeps the next rules edit from making it live.")]
	public void BundledRules_ComponentPropertyOverrides_ShouldOnlyNameDeclaredProperties() {
		// Arrange
		WebToMobilePageConversionRules rules = WebToMobilePageConversionRulesCatalog.LoadBundled();
		ComponentCatalogState catalog = LiveMobileCatalog();
		List<string> undeclared = [];
		int checkedPairs = 0;

		// Act
		foreach (ComponentPropertyOverrideRule rule in rules.ComponentPropertyOverrides ?? []) {
			// Only a filter set that names ONE type has a statically known target. A rule whose filters name
			// several types, or none, is skipped rather than guessed at — the same fail-open discipline the
			// prune itself applies to a type the registry does not describe.
			string[] types = (rule.Filters ?? [])
				.Select(filter => filter.Type)
				.Where(type => !string.IsNullOrWhiteSpace(type))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (types.Length != 1 || !catalog.Lookup.TryGetValue(types[0], out ComponentRegistryEntry entry)) {
				continue;
			}
			IReadOnlySet<string> allowed = DeclaredNames(entry, catalog);
			IEnumerable<string> named = (rule.Values?.Keys ?? [])
				.Concat((rule.Filters ?? []).SelectMany(filter => filter.Values?.Keys ?? []));
			foreach (string property in named) {
				checkedPairs++;
				if (!allowed.Contains(property)) {
					undeclared.Add($"override on {types[0]} names '{property}'");
				}
			}
		}

		// Assert
		checkedPairs.Should().BeGreaterThan(0,
			because: "a run that checked nothing would pass without reading the overrides at all, and the bundled "
				+ "rules do carry single-type overrides today");
		undeclared.Should().BeEmpty(
			because: "a WRITTEN undeclared key reaches the mobile page through a path the prune exempts, and a "
				+ "FILTERED one can be removed by the prune before the filter runs — which disables the override "
				+ "silently, reporting neither a normalization nor a skip. " + string.Join("; ", undeclared));
	}

	// ---------------------------------------------------------------- helpers

	/// <summary>
	/// The same union the prune enforces: a component's own inputs and outputs, plus the surface every
	/// component inherits from the registry root. Rebuilt here rather than reached for through the analysis
	/// service because this fixture asserts the RULES against the registry, not the converter.
	/// </summary>
	private static IReadOnlySet<string> DeclaredNames(ComponentRegistryEntry entry, ComponentCatalogState catalog) {
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string key in entry.Inputs?.Keys ?? []) {
			names.Add(key);
		}
		foreach (string key in entry.Outputs?.Keys ?? []) {
			names.Add(key);
		}
		foreach (string key in entry.Properties?.Keys ?? []) {
			names.Add(key);
		}
		foreach (string key in catalog.GlobalReferences?.BaseInputs?.Keys ?? []) {
			names.Add(key);
		}
		return names;
	}

	private static ComponentCatalogState LiveMobileCatalog() {
		string path = Path.Combine(
			TestContext.CurrentContext.TestDirectory, "Command", "McpServer", "Fixtures",
			"MobileComponentRegistry.live-snapshot.json");
		using FileStream stream = File.OpenRead(path);
		return ComponentInfoCatalog.LoadFromStream(stream);
	}
}
