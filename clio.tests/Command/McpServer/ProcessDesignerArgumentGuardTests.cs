using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-98566. Family-wide lock-in for the unknown-argument guard on the process-designer MCP tools.
/// <para>
/// The MCP binder copies <c>McpJsonUtilities.DefaultOptions</c> WITHOUT
/// <c>JsonUnmappedMemberHandling.Disallow</c>, so a JSON field matching no <c>[JsonPropertyName]</c> is
/// discarded by System.Text.Json with no error. The flat-argument classifier in <c>McpToolErrorFilter</c>
/// catches that for a FLAT payload only - an already-wrapped <c>{"args":{...}}</c> call is passed through
/// untouched - so the wrapped shape, which is the one the published schema asks for, still loses the key.
/// The remedy is per-tool: a <c>[JsonExtensionData]</c> overflow bag PLUS a check over it.
/// </para>
/// <para>
/// These tests are deliberately reflective, and their scope is exactly that: a NEW process-designer tool
/// cannot be added without DECLARING the guard's parts. They do NOT prove the guard is invoked - deleting
/// the BuildLegacyAliasError call from a tool leaves every assertion here green, because a declaration is
/// not a call. That proposition belongs to
/// <see cref="ProcessDesignerUnknownArgumentRefusalTests"/>, which exercises the other SEVEN tools through
/// their real entry points - validate-process-graph's own behavioural guard tests live beside its other
/// cases in ValidateProcessGraphToolTests. This fixture is the tripwire for the declaration, not the oracle
/// for the behaviour; saying "all eight" here would send the next reader to a file that does not hold the
/// eighth.
/// Keeping the two apart is deliberate - stating the stronger claim here is how a fixture ends up trusted
/// for something it never checked.
/// </para>
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessDesignerArgumentGuardTests {

	private const string ProcessDesignerNamespace = "Clio.Command.McpServer.Tools.ProcessDesigner";

	private static IReadOnlyList<Type> ArgumentRecords() =>
		typeof(ValidateProcessGraphArgs).Assembly
			.GetTypes()
			.Where(type => type.Namespace == ProcessDesignerNamespace
				&& type.Name.EndsWith("Args", StringComparison.Ordinal))
			.OrderBy(type => type.Name, StringComparer.Ordinal)
			.ToList();

	private static IReadOnlyList<Type> ToolTypes() =>
		typeof(ValidateProcessGraphTool).Assembly
			.GetTypes()
			.Where(type => type.Namespace == ProcessDesignerNamespace
				&& type.Name.EndsWith("Tool", StringComparison.Ordinal))
			.OrderBy(type => type.Name, StringComparer.Ordinal)
			.ToList();

	[Test]
	[Category("Unit")]
	[Description("Every process-designer argument record carries a [JsonExtensionData] overflow bag, so a key "
		+ "the binder cannot match survives long enough for the tool to name it. Discovered by reflection "
		+ "rather than listed, so a tool added later is covered without anyone remembering to extend a list.")]
	public void EveryArgumentRecord_ShouldDeclareAnOverflowBag() {
		// Arrange
		IReadOnlyList<Type> records = ArgumentRecords();

		// Act
		List<string> withoutBag = records
			.Where(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.All(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is null))
			.Select(type => type.Name)
			.ToList();

		// Assert
		records.Should().NotBeEmpty(
			because: "the reflection filter must actually find the process-designer argument records, "
				+ "otherwise this test passes vacuously and guards nothing");
		withoutBag.Should().BeEmpty(
			because: "without the bag an unknown key is dropped by the serializer before the tool runs, and "
				+ "the tool answers a caller mistake with a plausible success - the ENG-98566 failure");
	}

	[Test]
	[Category("Unit")]
	[Description("The overflow bag is typed so the shared McpToolArgumentSupport.BuildLegacyAliasError helper "
		+ "can read it. A bag of the wrong element type would compile and capture nothing useful, which is "
		+ "the quiet way to reintroduce the defect while looking guarded.")]
	public void EveryOverflowBag_ShouldBeReadableByTheSharedGuardHelper() {
		// Arrange
		IReadOnlyList<Type> records = ArgumentRecords();

		// Act
		List<string> wrongType = records
			.Select(type => new {
				type.Name,
				Bag = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
					.FirstOrDefault(property => property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
			})
			.Where(entry => entry.Bag is not null
				&& !typeof(IReadOnlyDictionary<string, JsonElement>).IsAssignableFrom(entry.Bag.PropertyType))
			.Select(entry => entry.Name)
			.ToList();

		// Assert
		records.Should().NotBeEmpty(
			because: "an empty population makes the filtered list empty too, so without this the tripwire "
				+ "reports the family as guarded while guarding nothing - review finding 5");
		wrongType.Should().BeEmpty(
			because: "BuildLegacyAliasError takes IReadOnlyDictionary<string, JsonElement>; a bag it cannot "
				+ "accept leaves the tool with a captured key it never reports");
	}

	[Test]
	[Category("Unit")]
	[Description("Every process-designer tool publishes the canonical field list its guard echoes back, so a "
		+ "caller who mis-keys an argument gets the valid names and does not have to guess a second time. "
		+ "This asserts the constant EXISTS and is non-blank; that the tool actually passes it to "
		+ "BuildLegacyAliasError is a behavioural claim, checked in ProcessDesignerUnknownArgumentRefusalTests.")]
	public void EveryTool_ShouldPublishItsCanonicalFieldList() {
		// Arrange
		IReadOnlyList<Type> tools = ToolTypes();

		// Act
		List<string> missing = tools
			.Where(type => type.GetField("ValidArgsHint",
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) is null)
			.Select(type => type.Name)
			.ToList();
		List<string> blank = tools
			.Select(type => new {
				type.Name,
				Field = type.GetField("ValidArgsHint",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			})
			.Where(entry => entry.Field is not null
				&& string.IsNullOrWhiteSpace(entry.Field.GetRawConstantValue() as string))
			.Select(entry => entry.Name)
			.ToList();

		// Assert
		tools.Should().NotBeEmpty(
			because: "the reflection filter must find the process-designer tools for this test to mean anything");
		missing.Should().BeEmpty(
			because: "a tool with a bag but no field list has captured the unknown key without telling the "
				+ "caller what the valid keys are");
		blank.Should().BeEmpty(
			because: "an empty hint is the same as no hint once it reaches the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("Each tool's field list names environment-name. Every process-designer tool is "
		+ "environment-scoped, and environment-name is the argument agents mis-spell most often - it is the "
		+ "one the shared EnvironmentNameAliases map exists for - so omitting it from the hint would leave "
		+ "the commonest mistake unanswered by the very sentence written to answer it.")]
	public void EveryFieldList_ShouldNameEnvironmentName() {
		// Arrange
		IReadOnlyList<Type> tools = ToolTypes();

		// Act
		List<string> withoutEnvironment = tools
			.Select(type => new {
				type.Name,
				Hint = type.GetField("ValidArgsHint",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
					?.GetRawConstantValue() as string
			})
			.Where(entry => entry.Hint is null
				|| !entry.Hint.Contains("environment-name", StringComparison.Ordinal))
			.Select(entry => entry.Name)
			.ToList();

		// Assert
		tools.Should().NotBeEmpty(
			because: "an empty population makes the filtered list empty too, so without this the tripwire "
				+ "reports the family as guarded while guarding nothing - review finding 5");
		withoutEnvironment.Should().BeEmpty(
			because: "a hint that omits the argument most often mis-spelled cannot resolve the commonest call");
	}
	[Test]
	[Category("Unit")]
	[Description("Every process-designer tool declares a non-blank NullArgsError, so a tool added to this "
		+ "folder later cannot omit the decision about a null argument object. The behavioural half cannot be "
		+ "reflective - the seven return shapes differ - so it lives in "
		+ "ProcessDesignerUnknownArgumentRefusalTests; this is the declaration tripwire, the same split as "
		+ "ValidArgsHint above.")]
	public void EveryTool_ShouldDeclareANullArgumentRefusal() {
		// Arrange
		IReadOnlyList<Type> tools = ToolTypes();

		// Act
		List<string> missingOrBlank = tools
			.Select(type => new {
				type.Name,
				Field = type.GetField("NullArgsError",
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			})
			.Where(entry => entry.Field is null
				|| string.IsNullOrWhiteSpace(entry.Field.GetRawConstantValue() as string))
			.Select(entry => entry.Name)
			.ToList();

		// Assert
		tools.Should().NotBeEmpty(
			because: "the reflection filter must find the process-designer tools for this test to mean anything");
		missingOrBlank.Should().BeEmpty(
			because: "a tool with no null-args refusal dereferences the argument object it was never given, "
				+ "which is the S2259 defect SonarCloud raised on three of these files");
	}
}
