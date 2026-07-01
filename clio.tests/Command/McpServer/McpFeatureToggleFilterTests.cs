using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpFeatureToggleFilterTests
{
	// Synthetic marker attribute that stands in for an MCP type marker (e.g. McpServerToolTypeAttribute)
	// so the helper can be exercised without depending on real shipping tool/resource/prompt classes.
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	private sealed class FakeMcpMarkerAttribute : Attribute { }

	[FakeMcpMarker]
	private sealed class UngatedMarkedType { }

	[FakeMcpMarker]
	[FeatureToggle("synthetic-feature")]
	private sealed class GatedMarkedType { }

	[FeatureToggle("synthetic-feature")]
	private sealed class GatedButUnmarkedType { }

	private sealed class PlainType { }

	[Test]
	[Category("Unit")]
	[Description("Includes a marked type that carries no feature toggle attribute regardless of the predicate.")]
	public void GetEnabledTypes_ShouldIncludeUngatedMarkedType_WhenNoFeatureTogglePresent() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		// Predicate that disables everything it can, to prove ungated types are still kept by the predicate contract.
		bool IsEnabled(Type type) => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is null;

		// Act
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), IsEnabled);

		// Assert
		enabled.Should().Contain(typeof(UngatedMarkedType),
			because: "a marked type without a feature toggle is always available");
	}

	[Test]
	[Category("Unit")]
	[Description("Excludes a marked type whose feature flag is reported off by the predicate.")]
	public void GetEnabledTypes_ShouldExcludeGatedMarkedType_WhenPredicateReportsDisabled() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		// Disable any type that carries a FeatureToggleAttribute (feature off).
		bool IsEnabled(Type type) => type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false) is null;

		// Act
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), IsEnabled);

		// Assert
		enabled.Should().NotContain(typeof(GatedMarkedType),
			because: "a marked type whose feature flag is off must not be registered with the MCP server");
	}

	[Test]
	[Category("Unit")]
	[Description("Includes a marked gated type when the predicate reports its feature flag as on.")]
	public void GetEnabledTypes_ShouldIncludeGatedMarkedType_WhenPredicateReportsEnabled() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		// Enable everything (feature on).
		bool IsEnabled(Type type) => true;

		// Act
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), IsEnabled);

		// Assert
		enabled.Should().Contain(typeof(GatedMarkedType),
			because: "a marked gated type must be registered when its feature flag is on");
	}

	[Test]
	[Category("Unit")]
	[Description("Never selects a type that lacks the requested MCP marker attribute even when its feature is on.")]
	public void GetEnabledTypes_ShouldExcludeUnmarkedType_WhenMarkerAttributeIsAbsent() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		bool IsEnabled(Type type) => true;

		// Act
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), IsEnabled);

		// Assert
		enabled.Should().NotContain(typeof(GatedButUnmarkedType),
			because: "the filter only selects types carrying the requested MCP marker attribute");
		enabled.Should().NotContain(typeof(PlainType),
			because: "a type with neither the marker nor a feature toggle is not an MCP type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the full marker-attributed set when the predicate enables every type.")]
	public void GetEnabledTypes_ShouldReturnFullMarkedSet_WhenPredicateEnablesAll() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		Type[] expected = McpFeatureToggleFilter.GetAttributedTypes(assembly, typeof(FakeMcpMarkerAttribute));

		// Act
		Type[] enabled = McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), _ => true);

		// Assert
		enabled.Should().BeEquivalentTo(expected,
			because: "with nothing gated off the enabled set must equal the full marker-attributed set");
	}

	[Test]
	[Description("Rejects a null assembly argument.")]
	[Category("Unit")]
	public void GetEnabledTypes_ShouldThrow_WhenAssemblyIsNull() {
		// Arrange
		Action act = () => McpFeatureToggleFilter.GetEnabledTypes(null, typeof(FakeMcpMarkerAttribute), _ => true);

		// Act & Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the assembly to scan is required");
	}

	[Test]
	[Description("Rejects a null marker attribute argument.")]
	[Category("Unit")]
	public void GetEnabledTypes_ShouldThrow_WhenMarkerAttributeIsNull() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		Action act = () => McpFeatureToggleFilter.GetEnabledTypes(assembly, null, _ => true);

		// Act & Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the MCP marker attribute to select by is required");
	}

	[Test]
	[Description("Rejects a null predicate argument.")]
	[Category("Unit")]
	public void GetEnabledTypes_ShouldThrow_WhenPredicateIsNull() {
		// Arrange
		Assembly assembly = typeof(McpFeatureToggleFilterTests).Assembly;
		Action act = () => McpFeatureToggleFilter.GetEnabledTypes(assembly, typeof(FakeMcpMarkerAttribute), null);

		// Act & Assert
		act.Should().Throw<ArgumentNullException>(
			because: "the feature predicate is required");
	}

	[Test]
	[Category("Unit")]
	[Description("The shared production registration seam matches the SDK's *FromAssembly scanners for resources/prompts and registers a non-empty lazy SUBSET of tools via the correct IEnumerable<Type> overload (never the zero-registering generic overload).")]
	public void RegisterEnabledPrimitives_ShouldMatchSdkResourcesPromptsAndRegisterLazyToolSubset_WhenNothingGated() {
		// This is intentionally NOT a re-implementation of the helper's own enumeration rule (that would
		// only prove self-consistency), and it exercises the EXACT production registration path: the same
		// McpFeatureToggleFilter.RegisterEnabledPrimitives seam BindingsModule calls. That matters because
		// the SDK exposes both an IEnumerable<Type> overload and a generic With*<TType>(TType target, ...)
		// overload; passing a Type[] silently binds to the generic one and registers ZERO primitives, so a
		// test that hand-built a different call shape could pass while production registered nothing.
		// It drives the REAL ModelContextProtocol SDK two ways over the clio assembly: (a) the production
		// seam with nothing gated, and (b) the SDK's own WithToolsFromAssembly/WithResourcesFromAssembly/
		// WithPromptsFromAssembly. Each registers one DI ServiceDescriptor per discovered MCP primitive
		// (McpServerTool/McpServerResource/McpServerPrompt), so comparing those descriptor counts proves
		// the gated registration matches the pre-feature SDK baseline. Inert marker types such as the
		// abstract open-generic BaseTool<T> (which carries [McpServerToolType] but declares no
		// [McpServerTool] method) contribute zero primitives on BOTH sides, so they cannot cause drift.
		// True end-to-end protocol parity (advertised tool NAMES through the running server) is covered by
		// the clio.mcp.e2e suite; this unit guard pins the registration-set parity cheaply.

		// Arrange
		Assembly clioAssembly = typeof(McpFeatureToggleFilter).Assembly;

		ServiceCollection productionServices = new();
		IMcpServerBuilder productionBuilder = productionServices.AddMcpServer();
		ServiceCollection sdkServices = new();
		IMcpServerBuilder sdkBuilder = sdkServices.AddMcpServer();

		// Act
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			productionBuilder, clioAssembly, _ => true, JsonSerializerOptions.Default);
		sdkBuilder
			.WithResourcesFromAssembly(clioAssembly)
			.WithToolsFromAssembly(clioAssembly, JsonSerializerOptions.Default)
			.WithPromptsFromAssembly(clioAssembly, JsonSerializerOptions.Default);

		int productionToolCount = CountPrimitives<McpServerTool>(productionServices);
		int sdkToolCount = CountPrimitives<McpServerTool>(sdkServices);
		int productionResourceCount = CountPrimitives<McpServerResource>(productionServices);
		int sdkResourceCount = CountPrimitives<McpServerResource>(sdkServices);
		int productionPromptCount = CountPrimitives<McpServerPrompt>(productionServices);
		int sdkPromptCount = CountPrimitives<McpServerPrompt>(sdkServices);

		// Assert
		sdkToolCount.Should().BeGreaterThan(0,
			because: "the clio assembly ships MCP tools, so the SDK baseline must register a non-empty tool set");
		sdkResourceCount.Should().BeGreaterThan(0,
			because: "the clio assembly ships MCP resources, so the SDK baseline must register a non-empty resource set");
		sdkPromptCount.Should().BeGreaterThan(0,
			because: "the clio assembly ships MCP prompts, so the SDK baseline must register a non-empty prompt set");
		productionToolCount.Should().BeGreaterThan(0,
			because: "the production seam must use the IEnumerable<Type> SDK overload — a Type[] would bind to the generic With*<T> overload and register zero tools");
		productionToolCount.Should().BeLessThan(sdkToolCount,
			because: "the production tool surface is the lazy profile, a strict subset of the SDK's full from-assembly tool scan");
		productionResourceCount.Should().Be(sdkResourceCount,
			because: "with nothing gated, the production registration seam must register the same MCP resources the SDK's own from-assembly scanner does");
		productionPromptCount.Should().Be(sdkPromptCount,
			because: "with nothing gated, the production registration seam must register the same MCP prompts the SDK's own from-assembly scanner does");
	}

	// Counts how many MCP primitives of type TPrimitive were registered as DI singletons. Each
	// [McpServerTool]/[McpServerResource]/[McpServerPrompt] member the SDK discovers becomes exactly one
	// such ServiceDescriptor.
	private static int CountPrimitives<TPrimitive>(IServiceCollection services) =>
		services.Count(descriptor => descriptor.ServiceType == typeof(TPrimitive));
}
