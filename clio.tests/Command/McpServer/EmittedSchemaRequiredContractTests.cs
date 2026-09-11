using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Registry-WIDE guards over the emitted <c>required</c> arrays of every registered MCP tool.
/// </summary>
/// <remarks>
/// This defect class has now landed three times (PR #984, ENG-93347, issue #965), always as one more
/// per-tool oversight, so the guard is deliberately written once over the whole catalog instead of once
/// per tool. The MCP SDK puts a positional record parameter into <c>required</c> whenever it has no
/// default value — nullability and <c>[Required]</c> are both irrelevant — while the STJ binder binds a
/// missing nullable parameter to <c>null</c> and never consults <c>required</c>. Nothing in-process
/// therefore fails when the two disagree: only a STRICT client sees it, by refusing to send a payload the
/// tool documentation says is valid, with no server-side log at all.
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class EmittedSchemaRequiredContractTests {

	private const string EnvironmentName = "environment-name";
	private const string Uri = "uri";
	private const string Login = "login";
	private const string Password = "password";

	private static readonly string[] ConnectionFieldNames = [EnvironmentName, Uri, Login, Password];

	[Test]
	[Category("Unit")]
	[Description("No registered MCP tool lists a connection field as required in an object schema that advertises BOTH environment-name and uri, because the runtime accepts either connection path and demanding both spellings makes a documented environment-name-only payload fail client-side validation (issue #965).")]
	public void RegisteredTools_Should_NotRequireConnectionFields_WhenBothConnectionPathsAreAdvertised() {
		// Arrange
		IReadOnlyList<EmittedObjectSchema> schemas = EmittedSchemaProbe.EnumerateRequiredBearingSchemas();

		// Act
		List<string> violations = [];
		foreach (EmittedObjectSchema schema in schemas) {
			// Only an object offering BOTH paths can be over-constrained: where a tool advertises just
			// environment-name there is no alternative, so requiring it states the truth.
			if (!EmittedSchemaProbe.Advertises(schema.Schema, EnvironmentName) ||
				!EmittedSchemaProbe.Advertises(schema.Schema, Uri)) {
				continue;
			}
			violations.AddRange(EmittedSchemaProbe.RequiredNames(schema.Schema)
				.Where(ConnectionFieldNames.Contains)
				.Select(name => $"{schema.ToolName} {schema.Path} requires '{name}'"));
		}

		// Assert
		violations.Should().BeEmpty(
			because: "a tool that offers both a registered environment-name and the explicit uri fallback " +
				"accepts either one, so neither may be advertised as unconditionally mandatory");

		// Anti-vacuity: the predicate must actually select something, otherwise the assertion above would
		// pass over an empty set the day the connection args stop being advertised at all.
		schemas.Count(schema =>
			EmittedSchemaProbe.Advertises(schema.Schema, EnvironmentName) &&
			EmittedSchemaProbe.Advertises(schema.Schema, Uri))
			.Should().BeGreaterThan(0,
				because: "the catalog must still contain tools offering both connection paths for this guard to mean anything");
	}

	[Test]
	[Category("Unit")]
	[Description("No registered MCP tool lists a field whose own description begins with 'Optional' as required, because a contract that calls a field optional and a schema that demands it cannot both be true (issue #965).")]
	public void RegisteredTools_Should_NotRequireFields_DocumentedAsOptional() {
		// Arrange
		IReadOnlyList<EmittedObjectSchema> schemas = EmittedSchemaProbe.EnumerateRequiredBearingSchemas();

		// Act
		List<string> violations = [];
		foreach (EmittedObjectSchema schema in schemas) {
			// The root schema's single `args` wrapper is genuinely mandatory; its description is the tool
			// method's parameter text, which legitimately opens by describing an optional inner field. Only
			// THAT synthetic shape is exempt: a method-parameter tool puts its real user-facing fields on
			// the root, so skipping every root would exempt the whole family that carried this defect in
			// ENG-93347 (link-from-repository-*), which is precisely what this registry-wide guard exists
			// to cover.
			if (schema.IsRoot && EmittedSchemaProbe.IsSingleArgsWrapper(schema.Schema)) {
				continue;
			}
			violations.AddRange(EmittedSchemaProbe.RequiredNames(schema.Schema)
				.Where(name => EmittedSchemaProbe.PropertyDescription(schema.Schema, name)
					.TrimStart()
					.StartsWith("Optional", System.StringComparison.OrdinalIgnoreCase))
				.Select(name => $"{schema.ToolName} {schema.Path} requires '{name}' while describing it as optional"));
		}

		// Assert — no allow-list is needed: the deliberately-required nullable fields (the four
		// merge-creatio-artifact contents and validate-page's body) describe themselves as required, so
		// they never match this predicate and keep returning their typed {success:false} instead of an SDK
		// binding error.
		violations.Should().BeEmpty(
			because: "a field the contract text calls optional must not be one a strict client is forced to send");

		// Anti-vacuity, in two parts: the walk must reach schemas at all, and — since narrowing the root
		// skip is the whole point of the second condition — it must reach method-parameter roots too,
		// otherwise the guard would silently go back to covering only the nested `args` objects.
		schemas.Should().NotBeEmpty(
			because: "a guard that scans nothing cannot fail, so the catalog must yield required-bearing schemas");
		schemas.Count(schema => schema.IsRoot && !EmittedSchemaProbe.IsSingleArgsWrapper(schema.Schema))
			.Should().BeGreaterThan(0,
				because: "method-parameter tools put their real fields on the root, and this guard must " +
					"still be scanning them");
	}

	[Test]
	[Category("Unit")]
	[TestCase("update-page", new[] { "schema-name" })]
	[TestCase("list-pages", new string[0])]
	[TestCase("get-page-hierarchy", new[] { "schema-name" })]
	[TestCase("update-schema", new[] { "schema-name" })]
	[TestCase("update-sql-schema", new[] { "schema-name" })]
	[TestCase("update-client-unit-schema", new[] { "schema-name" })]
	[Description("Each record relaxed by issue #965 whose optional fields are NOT worded 'Optional…' is pinned to its exact emitted required set, because the registry-wide description heuristic cannot see wordings such as 'If true, validate without saving' or 'Filter by package name' and would let those relaxations be reverted while staying green.")]
	public void RelaxedRecords_Should_RequireOnlyTheirIdentityFields_InEmittedInputSchema(
		string toolName, string[] expectedRequired) {
		// Arrange & Act
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema(toolName);
		JsonElement argsSchema = EmittedSchemaProbe.EffectiveArgumentSchema(schema.RootElement);

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEquivalentTo(expectedRequired,
			because: $"'{toolName}' identifies its target by these fields alone — every dry-run flag, " +
				"filter, limit and connection argument is optional, and a strict client must not be forced " +
				"to send them");
	}

	[Test]
	[Category("Unit")]
	[Description("reg-web-app advertises environment-name and uri without gaining the connection any-of, because there uri is the application BEING REGISTERED rather than a fallback route to an existing one, and offering a credential-only branch would advertise a payload the tool rejects with exit code 1 (issue #965, PR #1396 review).")]
	public void RegistryDerivedContract_Should_OmitConnectionAlternative_ForEnvironmentRegistrationTools() {
		// Arrange
		McpToolInvokerRegistry registry = EmittedSchemaProbe.BuildProductionRegistry();

		// Act
		bool built = McpToolRegistrySchemaContract.TryBuild(registry, "reg-web-app", out ToolContractDefinition contract);

		// Assert
		built.Should().BeTrue(because: "reg-web-app must be registered for its contract to be derivable");
		string[] advertised = [.. contract.InputSchema.Properties.Select(field => field.Name)];
		advertised.Should().Contain([EnvironmentName, Uri],
			because: "this is the second negative control precisely BECAUSE it advertises both names the " +
				"any-of heuristic keys on");
		advertised.Should().Contain("active-environment",
			because: "the environment-registration surface is what tells the heuristic these two names are " +
				"the record being written, not a way of reaching an environment");
		contract.InputSchema.AnyOf.Should().BeNull(
			because: "reg-web-app rejects a uri/login/password payload that carries no environment-name, " +
				"active-environment or add-from-iis, so advertising it as a complete alternative would " +
				"send an agent into a guaranteed exit-code-1 failure with credentials attached");
	}

	/// <summary>
	/// Tools whose curated <c>required</c> deliberately says MORE than the emitted schema does, with the
	/// reason. The list is exhaustive and deliberately tiny: every other disagreement is a defect.
	/// </summary>
	private static readonly Dictionary<string, string> CuratedStricterThanEmittedByDesign = new() {
		["get-guidance"] = "'name' is genuinely mandatory, but the record parameter is nullable so that a " +
			"legacy-alias payload (topic/guide/article via [JsonExtensionData]) reaches the tool and comes " +
			"back as a typed {success:false, availableGuides:[…]} answer instead of an SDK binding error. " +
			"The curated contract states the requirement the caller has to satisfy; the emitted schema " +
			"states what the binder accepts."
	};

	[Test]
	[Category("Unit")]
	[Description("Every resident tool's curated required set matches the required set its emitted schema advertises, because the two surfaces are read by the same agent — get-tool-contract to plan the call and tools/list to validate it — and a disagreement makes one of them a documented lie (issue #965).")]
	public void CuratedContracts_Should_AgreeWithEmittedSchema_ForResidentTools() {
		// Arrange
		McpToolInvokerRegistry registry = EmittedSchemaProbe.BuildProductionRegistry();
		// Scope is the RESIDENT surface on purpose: those are the tools whose emitted schema a strict
		// client actually validates a payload against, because they ship in tools/list. A long-tail tool is
		// dispatched through clio-run, so it is clio-run's schema — not its own — that gates the payload.
		string[] curatedResidentTools = [.. registry.ToolNames
			.Where(McpCoreToolProfile.IsResident)
			.Where(ToolContractCatalog.CuratedToolNames.Contains)
			.OrderBy(name => name, StringComparer.Ordinal)];

		// Act
		List<string> disagreements = [];
		foreach (string toolName in curatedResidentTools) {
			ToolContractDefinition curated = ToolContractCatalog
				.GetContracts([toolName], registry).Tools.Single();
			using JsonDocument emitted = EmittedSchemaProbe.EmittedInputSchema(toolName);
			string[] emittedRequired = [.. EmittedSchemaProbe
				.RequiredNames(EmittedSchemaProbe.EffectiveArgumentSchema(emitted.RootElement))
				.OrderBy(name => name, StringComparer.Ordinal)];
			string[] curatedRequired = [.. (curated.InputSchema.Required ?? [])
				.OrderBy(name => name, StringComparer.Ordinal)];
			if (emittedRequired.SequenceEqual(curatedRequired) ||
				CuratedStricterThanEmittedByDesign.ContainsKey(toolName)) {
				continue;
			}
			disagreements.Add(
				$"{toolName}: emitted=[{string.Join(",", emittedRequired)}] curated=[{string.Join(",", curatedRequired)}]");
		}

		// Assert
		disagreements.Should().BeEmpty(
			because: "this is the comparison that was missing: the emitted-schema guards above pass while a " +
				"curated contract still demands a field the tool's own description tells the caller to omit, " +
				"which is how get-entity-schema-properties kept asking for package-name and denying every " +
				"agent the merged, all-packages column view");
		curatedResidentTools.Length.Should().BeGreaterThan(1,
			because: "the comparison must cover a real population of resident curated tools, not an empty one");
	}

	[Test]
	[Category("Unit")]
	[Description("get-entity-schema-properties advertises package-name as optional on BOTH surfaces, because omitting it is the documented way to read the merged schema with the columns of every package (issue #965).")]
	public void GetEntitySchemaProperties_Should_TreatPackageNameAsOptional_OnBothSurfaces() {
		// Arrange
		McpToolInvokerRegistry registry = EmittedSchemaProbe.BuildProductionRegistry();
		const string toolName = "get-entity-schema-properties";

		// Act
		ToolContractDefinition curated = ToolContractCatalog.GetContracts([toolName], registry).Tools.Single();
		using JsonDocument emitted = EmittedSchemaProbe.EmittedInputSchema(toolName);
		JsonElement argsSchema = EmittedSchemaProbe.EffectiveArgumentSchema(emitted.RootElement);

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEquivalentTo(
			[EnvironmentName, "schema-name"],
			because: "the merged read needs an environment and a schema name and nothing else");
		(curated.InputSchema.Required ?? []).Should().BeEquivalentTo(
			[EnvironmentName, "schema-name"],
			because: "the curated contract is what an agent plans the call from, so it must not demand the " +
				"package-name that switches the tool from 49 own columns to 0");
		curated.InputSchema.Properties.Should().Contain(field => field.Name == "package-name",
			because: "the single-package-layer read stays available, it is simply no longer mandatory");
	}

	[Test]
	[Category("Unit")]
	[Description("A registry-derived get-tool-contract envelope states the environment-name OR uri/login/password alternative through the same any-of the curated contracts use, so an empty required array is not the only thing telling a caller how to connect (issue #965).")]
	public void RegistryDerivedContract_Should_AdvertiseConnectionAlternative_ForToolsOfferingBothPaths() {
		// Arrange
		McpToolInvokerRegistry registry = EmittedSchemaProbe.BuildProductionRegistry();
		string[] toolsOfferingBothPaths = ["list-page-templates", "create-page", "get-related-page-addon",
			"create-related-page-addon"];

		foreach (string toolName in toolsOfferingBothPaths) {
			// Act
			bool built = McpToolRegistrySchemaContract.TryBuild(registry, toolName, out ToolContractDefinition contract);

			// Assert
			built.Should().BeTrue(because: $"'{toolName}' must be registered for its contract to be derivable");
			contract.InputSchema.AnyOf.Should().BeEquivalentTo(
				new[] { new[] { EnvironmentName }, new[] { Uri, Login, Password } },
				because: "the derived contract must state the same connection alternative the 75 curated " +
					$"contracts state, so '{toolName}' does not look like it needs no connection at all");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("A tool that offers only environment-name gets no connection any-of, so the alternative is advertised where it exists and nowhere else (issue #965).")]
	public void RegistryDerivedContract_Should_OmitConnectionAlternative_ForToolsOfferingOnlyEnvironmentName() {
		// Arrange
		McpToolInvokerRegistry registry = EmittedSchemaProbe.BuildProductionRegistry();

		// Act
		bool built = McpToolRegistrySchemaContract.TryBuild(registry, "compile-status", out ToolContractDefinition contract);

		// Assert
		built.Should().BeTrue(because: "compile-status must be registered for its contract to be derivable");
		contract.InputSchema.Properties.Select(field => field.Name).Should().NotContain(Uri,
			because: "compile-status is the control case: it advertises no explicit-uri fallback");
		(contract.InputSchema.Required ?? []).Should().Contain(EnvironmentName,
			because: "with no alternative connection path, environment-name genuinely IS mandatory here — " +
				"which is why this guard relaxes only the tools that offer a second path");
		contract.InputSchema.AnyOf.Should().BeNull(
			because: "advertising an alternative a tool does not offer would send callers down a path its " +
				"schema rejects");
	}

	[Test]
	[Category("Unit")]
	[Description("list-page-templates requires nothing at all, because its schema-type filter and every connection field are optional (issue #965).")]
	public void ListPageTemplates_Should_RequireNothing_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema("list-page-templates");
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEmpty(
			because: "an environment-name-only call is the documented way to list templates, so demanding " +
				"schema-type plus the whole uri/login/password triple contradicts the tool's own contract");
		EmittedSchemaProbe.Advertises(argsSchema, "schema-type").Should().BeTrue(
			because: "relaxing the field must not remove it from the advertised argument surface");
	}

	[Test]
	[Category("Unit")]
	[Description("create-page requires only the three fields that genuinely identify the new page, not its optional captions or any connection field (issue #965).")]
	public void CreatePage_Should_RequireOnlyPageIdentity_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema("create-page");
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEquivalentTo(
			["schema-name", "template", "package-name"],
			because: "a page cannot be created without a name, a template and a target package, and " +
				"everything else — caption, description, entity-schema-name and the connection args — is " +
				"documented as optional");
	}

	[Test]
	[Category("Unit")]
	[Description("get-related-page-addon requires only the entity and package it reads, leaving the connection args optional (issue #965).")]
	public void GetRelatedPageAddon_Should_RequireOnlyReadTarget_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema("get-related-page-addon");
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEquivalentTo(
			["entity-schema-name", "package-name"],
			because: "the read target is mandatory but the connection path is a choice between " +
				"environment-name and the explicit uri triple");
	}

	[Test]
	[Category("Unit")]
	[Description("create-related-page-addon requires only the addon target and its pages, and its nested page entries require nothing, because a page entry may identify itself by page-schema-uid instead of page-schema-name (issue #965).")]
	public void CreateRelatedPageAddon_Should_RequireOnlyWriteTarget_InEmittedInputSchema() {
		// Arrange & Act
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema("create-related-page-addon");
		JsonElement argsSchema = schema.RootElement.GetProperty("properties").GetProperty("args");
		JsonElement pageItemSchema = argsSchema.GetProperty("properties").GetProperty("pages").GetProperty("items");

		// Assert
		EmittedSchemaProbe.RequiredNames(argsSchema).Should().BeEquivalentTo(
			["entity-schema-name", "package-name", "pages"],
			because: "type-column-uid is documented as optional and the connection args are an either-or choice");
		EmittedSchemaProbe.RequiredNames(pageItemSchema).Should().BeEmpty(
			because: "page-schema-name is required only UNLESS page-schema-uid is supplied, so demanding it " +
				"would reject the round-trip shape get-related-page-addon returns");
		EmittedSchemaProbe.Advertises(pageItemSchema, "page-schema-name").Should().BeTrue(
			because: "the name spelling must remain advertised even though it is no longer mandatory");
	}
}
