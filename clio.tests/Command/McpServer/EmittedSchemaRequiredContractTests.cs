using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
			// method's parameter text, which legitimately opens by describing an optional inner field.
			if (schema.IsRoot) {
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
