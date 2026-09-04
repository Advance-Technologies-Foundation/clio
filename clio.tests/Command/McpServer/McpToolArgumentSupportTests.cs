using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Tests.Command.McpServer.Boundary;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-95885. Pins the shared argument-support helpers, in particular the rejection-only environment
/// alias set: a non-canonical spelling must always produce a rename HINT and never a silent binding, so
/// the accepted field set stays exactly the canonical kebab-case one.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolArgumentSupportTests
{
	private static IReadOnlyDictionary<string, JsonElement> Overflow(params string[] keys) {
		Dictionary<string, JsonElement> bag = new(System.StringComparer.Ordinal);
		foreach (string key in keys) {
			bag[key] = JsonSerializer.SerializeToElement("value");
		}
		return bag;
	}

	[Test]
	[Category("Unit")]
	[Description("The bare 'environment' spelling — the one an agent writes when thinking of the CLI -e/--environment flag — maps to a rejection-only rename hint, not a silent binding (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldRenameBareEnvironment_ToEnvironmentName() {
		// Arrange
		IReadOnlyDictionary<string, JsonElement> overflow = Overflow("environment");

		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			overflow, McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().NotBeNull(
			because: "an overflow key that is a known alias must produce a corrective message, not pass silently");
		error.Should().Contain("'environment' -> 'environment-name'",
			because: "the bare 'environment' spelling is a rejection-only alias that must be reported as a rename");
	}

	[TestCase("environmentName")]
	[TestCase("environment_name")]
	[TestCase("environment")]
	[Category("Unit")]
	[Description("Every known environment-name misspelling maps to the canonical kebab-case name as a rejection-only rename (ENG-95885).")]
	public void EnvironmentNameAliases_ShouldMapEveryKnownMisspelling_ToCanonical(string misspelling) {
		// Act
		bool mapped = McpToolArgumentSupport.EnvironmentNameAliases.TryGetValue(misspelling, out string? canonical);

		// Assert
		mapped.Should().BeTrue(because: $"'{misspelling}' is a spelling an agent emits and must be recognized");
		canonical.Should().Be("environment-name",
			because: "all environment aliases resolve to the single canonical kebab-case field name");
	}

	[Test]
	[Category("Unit")]
	[Description("A genuinely unknown overflow key is reported in the 'Unknown args' branch with the supplied valid-field hint, not silently accepted (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldListGenuinelyUnknownKey_WithValidFieldHint() {
		// Arrange
		IReadOnlyDictionary<string, JsonElement> overflow = Overflow("totally-made-up");

		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			overflow, McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().NotBeNull(because: "an unknown overflow key must still produce a corrective message");
		error.Should().Contain("Unknown args: 'totally-made-up'",
			because: "a key that matches no alias is reported as unknown rather than renamed");
		error.Should().Contain("Valid: environment-name.",
			because: "the caller-supplied valid-field hint must be appended so the agent can self-correct");
	}

	[Test]
	[Category("Unit")]
	[Description("A clean call with no overflow returns null so it passes straight through (ENG-95885).")]
	public void BuildLegacyAliasError_ShouldReturnNull_WhenNoOverflow() {
		// Act
		string? error = McpToolArgumentSupport.BuildLegacyAliasError(
			Overflow(), McpToolArgumentSupport.EnvironmentNameAliases, renameSuffix: ".", unknownHint: "Valid: environment-name.");

		// Assert
		error.Should().BeNull(because: "no unbound fields means nothing to flag and the call must not be disturbed");
	}

	private static ParameterInfo Parameter(string stubName) =>
		typeof(BoundaryParameterStubs)
			.GetMethod(stubName, BindingFlags.Public | BindingFlags.Static)!
			.GetParameters()[0];

	// --- Framework-parameter exclusion boundary (ENG-95885 review finding) ---
	//
	// IsBindableToolParameter decides how many parameters a tool exposes to callers, and
	// TryGetSingleCompositeParameter - the trigger gate shared by the flat-argument normalizer and
	// ClioRunTool - is built directly on that count. Widen or narrow this predicate and the normalizer
	// starts rewriting a payload it must not touch (or stops rewriting one it must), so the boundary is
	// pinned here rather than left to survive the next MCP SDK upgrade on trust.

	[TestCase("TakesSdkServer")]
	[TestCase("TakesSdkRequestContext")]
	[TestCase("TakesSdkProtocolType")]
	[TestCase("TakesCancellationToken")]
	[TestCase("TakesServiceProvider")]
	[Category("Unit")]
	[Description("Every parameter type the MCP SDK injects - its server, its request context, any other type from its assembly - plus the two BCL context types, is excluded from the bindable count, so it can never be mistaken for a tool's caller-supplied args record (ENG-95885).")]
	public void IsBindableToolParameter_ShouldExcludeFrameworkOwnedParameter(string stubName) {
		// Act
		bool bindable = McpToolArgumentSupport.IsBindableToolParameter(Parameter(stubName));

		// Assert
		bindable.Should().BeFalse(
			because: $"'{stubName}' carries a framework-injected parameter, which the SDK never binds from "
				+ "the caller's arguments object");
	}

	[Test]
	[Category("Unit")]
	[Description("An McpServer subclass declared OUTSIDE the MCP SDK assembly is still excluded. This is the boundary an assembly-identity check alone cannot see, and the one a namespace-prefix check silently got WRONG - it would have counted a host-defined server as a bindable args parameter (ENG-95885 review finding).")]
	public void IsBindableToolParameter_ShouldExcludeMcpServerSubclass_DeclaredOutsideTheSdkAssembly() {
		// Arrange
		ParameterInfo parameter = Parameter(nameof(BoundaryParameterStubs.TakesHostDefinedMcpServer));

		// Act
		bool bindable = McpToolArgumentSupport.IsBindableToolParameter(parameter);

		// Assert
		parameter.ParameterType.Assembly.Should().NotBeSameAs(
			typeof(ModelContextProtocol.Server.McpServer).Assembly,
			because: "the fixture must actually live outside the SDK assembly, or this test proves nothing");
		parameter.ParameterType.Namespace.Should().NotStartWith("ModelContextProtocol",
			because: "the fixture must also sit outside the SDK namespace, or the old prefix check would "
				+ "have caught it and the regression would be invisible here");
		bindable.Should().BeFalse(
			because: "assignability to McpServer is what makes a parameter framework-owned, not where it "
				+ "happens to be declared");
	}

	[Test]
	[Category("Unit")]
	[Description("A clio-owned args record whose namespace merely BEGINS with 'ModelContextProtocol' stays bindable. The retired Namespace.StartsWith check excluded it, which would have dropped a tool's only bindable parameter and silently disabled flat-argument normalization for that tool (ENG-95885 review finding).")]
	public void IsBindableToolParameter_ShouldStayBindable_ForLookalikeNamespaceArgsRecord() {
		// Arrange
		ParameterInfo parameter = Parameter(nameof(BoundaryParameterStubs.TakesLookalikeNamespaceArgs));

		// Act
		bool bindable = McpToolArgumentSupport.IsBindableToolParameter(parameter);

		// Assert
		parameter.ParameterType.Namespace.Should().StartWith("ModelContextProtocol",
			because: "the fixture only tests the retired prefix rule if its namespace really does start "
				+ "with those characters");
		bindable.Should().BeTrue(
			because: "a namespace NAME says nothing about ownership - this record is declared in clio.tests "
				+ "and is a caller-supplied args contract");
	}

	[Test]
	[Category("Unit")]
	[Description("The lookalike-namespace record still satisfies the shared single-composite trigger gate, so the boundary is pinned where it actually matters: at the predicate the flat-argument normalizer and ClioRunTool both consult (ENG-95885 review finding).")]
	public void TryGetSingleCompositeParameter_ShouldHold_ForLookalikeNamespaceArgsRecord() {
		// Arrange
		MethodInfo method = typeof(BoundaryParameterStubs)
			.GetMethod(nameof(BoundaryParameterStubs.TakesLookalikeNamespaceArgs),
				BindingFlags.Public | BindingFlags.Static)!;

		// Act
		bool single = McpToolArgumentSupport.TryGetSingleCompositeParameter(
			method, out ParameterInfo? parameter);

		// Assert
		single.Should().BeTrue(
			because: "exactly one bindable composite parameter is the shape a flat payload is unambiguous for");
		parameter!.ParameterType.Should().Be<ModelContextProtocolLookalike.LookalikeNamespaceArgs>(
			because: "the composite parameter reported back must be the args record itself");
	}

	[Test]
	[Category("Unit")]
	[Description("The trigger gate does NOT hold for a method whose only parameter is framework-owned, because excluding it leaves zero bindable parameters rather than one (ENG-95885).")]
	public void TryGetSingleCompositeParameter_ShouldNotHold_WhenTheOnlyParameterIsFrameworkOwned() {
		// Arrange
		MethodInfo method = typeof(BoundaryParameterStubs)
			.GetMethod(nameof(BoundaryParameterStubs.TakesHostDefinedMcpServer),
				BindingFlags.Public | BindingFlags.Static)!;

		// Act
		bool single = McpToolArgumentSupport.TryGetSingleCompositeParameter(
			method, out ParameterInfo? parameter);

		// Assert
		single.Should().BeFalse(
			because: "a framework-only signature exposes no caller-supplied argument object to normalize");
		parameter.Should().BeNull(because: "no composite parameter may be reported when the gate does not hold");
	}

	[Test]
	[Category("Unit")]
	[Description("IsFrameworkOwnedType and IsBindableToolParameter cannot disagree: the parameter-level predicate is exactly the negation of the type-level rule, so a future caller may use either without changing the bindable set (ENG-95885).")]
	public void IsBindableToolParameter_ShouldBeTheNegation_OfIsFrameworkOwnedType() {
		// Arrange
		string[] stubNames = [
			nameof(BoundaryParameterStubs.TakesLookalikeNamespaceArgs),
			nameof(BoundaryParameterStubs.TakesHostDefinedMcpServer),
			nameof(BoundaryParameterStubs.TakesSdkServer),
			nameof(BoundaryParameterStubs.TakesSdkRequestContext),
			nameof(BoundaryParameterStubs.TakesSdkProtocolType),
			nameof(BoundaryParameterStubs.TakesCancellationToken),
			nameof(BoundaryParameterStubs.TakesServiceProvider)
		];

		// Act & Assert
		foreach (string stubName in stubNames) {
			ParameterInfo parameter = Parameter(stubName);
			McpToolArgumentSupport.IsBindableToolParameter(parameter).Should().Be(
				!McpToolArgumentSupport.IsFrameworkOwnedType(parameter.ParameterType),
				because: $"'{stubName}' must get the same verdict from both entry points");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Both predicates reject null instead of throwing a NullReferenceException deeper in the reflection walk (ENG-95885).")]
	public void FrameworkOwnershipPredicates_ShouldRejectNull() {
		// Act
		Action bindableWithNull = () => McpToolArgumentSupport.IsBindableToolParameter(null!);
		Action frameworkOwnedWithNull = () => McpToolArgumentSupport.IsFrameworkOwnedType(null!);

		// Assert
		bindableWithNull.Should().Throw<ArgumentNullException>(
			because: "a null ParameterInfo is a caller bug and must be named as one at the boundary");
		frameworkOwnedWithNull.Should().Throw<ArgumentNullException>(
			because: "a null Type is a caller bug and must be named as one at the boundary");
	}
}
