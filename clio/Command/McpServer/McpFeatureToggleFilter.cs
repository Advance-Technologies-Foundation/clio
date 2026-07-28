using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer;

/// <summary>
/// Pure helper that narrows the MCP types discovered in an assembly down to the subset whose
/// feature flags are currently enabled, so gated-off tools, resources, and prompts are never
/// registered with the MCP server.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors the type enumeration performed by the <c>ModelContextProtocol</c> SDK's
/// <c>WithToolsFromAssembly</c> / <c>WithResourcesFromAssembly</c> / <c>WithPromptsFromAssembly</c>
/// methods (it selects <c>assembly.GetTypes()</c> carrying the given marker attribute, including
/// non-public types and honouring attribute inheritance via <c>inherit: true</c>) and then applies a
/// feature-toggle predicate. When nothing is gated, the returned set is therefore identical to the
/// full set the SDK would have registered.
/// </para>
/// <para>
/// The SDK does NOT exclude abstract or open-generic marker-carrying types from its assembly scan:
/// <c>WithTools(IEnumerable&lt;Type&gt;)</c> iterates each selected type's methods and registers only
/// the ones marked <c>[McpServerTool]</c>. <c>BaseTool&lt;T&gt;</c> carries <c>[McpServerToolType]</c>
/// but declares no <c>[McpServerTool]</c> method, so it is enumerated by both the SDK and this helper
/// yet contributes zero registered tools. To preserve exact parity with the SDK this helper
/// deliberately does NOT filter out abstract/open-generic types.
/// </para>
/// <para>
/// The feature rule itself is intentionally delegated to the caller-supplied <c>isEnabled</c>
/// predicate (backed by <see cref="IFeatureToggleService.IsEnabled(Type)"/>) so this helper does
/// not duplicate the toggle logic shared with the CLI parser and help renderer.
/// </para>
/// </remarks>
public static class McpFeatureToggleFilter
{
	/// <summary>
	/// Returns the MCP types in <paramref name="assembly"/> that carry
	/// <paramref name="mcpMarkerAttributeType"/> and whose feature flag is currently enabled.
	/// </summary>
	/// <param name="assembly">The assembly to scan for MCP types.</param>
	/// <param name="mcpMarkerAttributeType">
	/// The MCP marker attribute that identifies the kind of type to select (for example
	/// <c>McpServerToolTypeAttribute</c>, <c>McpServerResourceTypeAttribute</c>, or
	/// <c>McpServerPromptTypeAttribute</c>).
	/// </param>
	/// <param name="isEnabled">
	/// The feature predicate. A type is included only when this returns <c>true</c> for it.
	/// Types without a <see cref="FeatureToggleAttribute"/> are expected to always return
	/// <c>true</c>; gated types only when their flag is on.
	/// </param>
	/// <returns>
	/// The enabled subset, preserving the assembly's type order. Equals the full marker-attributed
	/// set when nothing is gated off.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown when any of <paramref name="assembly"/>, <paramref name="mcpMarkerAttributeType"/>, or
	/// <paramref name="isEnabled"/> is <c>null</c>.
	/// </exception>
	public static Type[] GetEnabledTypes(
		Assembly assembly,
		Type mcpMarkerAttributeType,
		Func<Type, bool> isEnabled) {
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(mcpMarkerAttributeType);
		ArgumentNullException.ThrowIfNull(isEnabled);

		return GetAttributedTypes(assembly, mcpMarkerAttributeType)
			.Where(isEnabled)
			.ToArray();
	}

	/// <summary>
	/// Returns all types in <paramref name="assembly"/> that carry
	/// <paramref name="mcpMarkerAttributeType"/>, replicating the SDK's discovery rule (all types,
	/// including non-public, honouring attribute inheritance) WITHOUT applying any feature gate.
	/// </summary>
	/// <param name="assembly">The assembly to scan.</param>
	/// <param name="mcpMarkerAttributeType">The MCP marker attribute to select by.</param>
	/// <returns>The full marker-attributed set, in the assembly's type order.</returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="assembly"/> or <paramref name="mcpMarkerAttributeType"/> is
	/// <c>null</c>.
	/// </exception>
	public static Type[] GetAttributedTypes(Assembly assembly, Type mcpMarkerAttributeType) {
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(mcpMarkerAttributeType);

		return assembly.GetTypes()
			.Where(type => Attribute.IsDefined(type, mcpMarkerAttributeType, inherit: true))
			.ToArray();
	}

	/// <summary>
	/// Registers the feature-enabled MCP tool, resource, and prompt types from <paramref name="assembly"/>
	/// with <paramref name="builder"/>, applying the feature gate via <paramref name="isEnabled"/>.
	/// </summary>
	/// <remarks>
	/// This is the SINGLE registration seam: both the composition root (<c>BindingsModule</c>) and the
	/// parity regression test call it, so the call shape cannot drift. The enabled type lists are passed
	/// to the SDK through the <see cref="IEnumerable{Type}"/> overloads of
	/// <c>WithTools</c>/<c>WithResources</c>/<c>WithPrompts</c>. This is load-bearing: the SDK also
	/// exposes generic <c>With*&lt;TType&gt;(TType target, ...)</c> overloads, and passing a
	/// <see cref="Array"/> of <see cref="Type"/> binds to the GENERIC overload (which scans the array
	/// object's own methods and registers nothing). Keeping the parameter type as
	/// <see cref="IEnumerable{Type}"/> forces the intended assembly-discovery overload.
	/// </remarks>
	/// <param name="builder">The MCP server builder to register the enabled primitives with.</param>
	/// <param name="assembly">The assembly to scan for MCP tool/resource/prompt types.</param>
	/// <param name="isEnabled">The feature predicate gating each discovered type.</param>
	/// <param name="serializerOptions">The serializer options governing tool/prompt parameter marshalling.</param>
	/// <returns>The same <paramref name="builder"/>, for chaining.</returns>
	/// <remarks>
	/// The registered tool set is always the lazy-mode profile (<see cref="SelectToolTypes"/>): the core
	/// flat types plus the <c>clio-run</c> / <c>clio-run-destructive</c> executors and
	/// <c>get-tool-contract</c>. The long-tail flat schemas never sit in <c>tools/list</c>; they stay
	/// reachable via the executors and discoverable via <c>get-tool-contract</c>. Resources and prompts
	/// are registered in full.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="builder"/>, <paramref name="assembly"/>, or <paramref name="isEnabled"/> is <c>null</c>.
	/// </exception>
	public static IMcpServerBuilder RegisterEnabledPrimitives(
		IMcpServerBuilder builder,
		Assembly assembly,
		Func<Type, bool> isEnabled,
		JsonSerializerOptions serializerOptions) {
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(isEnabled);

		IEnumerable<Type> enabledResourceTypes = GetEnabledTypes(
			assembly, typeof(McpServerResourceTypeAttribute), isEnabled);
		IEnumerable<Type> enabledToolTypes = SelectToolTypes(GetEnabledTypes(
			assembly, typeof(McpServerToolTypeAttribute), isEnabled));
		IEnumerable<Type> enabledPromptTypes = GetEnabledTypes(
			assembly, typeof(McpServerPromptTypeAttribute), isEnabled);

		return builder
			.WithResources(enabledResourceTypes)
			.WithTools(enabledToolTypes, serializerOptions)
			.WithPrompts(enabledPromptTypes, serializerOptions);
	}

	/// <summary>
	/// Selects which feature-enabled tool TYPES are registered flat in <c>tools/list</c>: the lazy-mode
	/// profile. This is the only tool surface clio's MCP server exposes.
	/// </summary>
	/// <remarks>
	/// The registered set is the intersection of the enabled types with the core profile
	/// (<see cref="McpCoreToolProfile.CoreToolTypes"/>) unioned with the always-on executor / contract
	/// types (<see cref="McpCoreToolProfile.AlwaysOnLazyToolTypes"/>): <c>clio-run</c> +
	/// <c>clio-run-destructive</c> + <c>get-tool-contract</c>. The long-tail flat schemas never sit in
	/// <c>tools/list</c> but stay reachable via the executors and discoverable via
	/// <c>get-tool-contract</c>. A core/executor type that is itself feature-gated off is still excluded
	/// (the intersection with <paramref name="enabledToolTypes"/> preserves per-type gating).
	/// </remarks>
	/// <param name="enabledToolTypes">The feature-enabled tool-type set (per-type gates already applied).</param>
	/// <returns>The tool-type set to register flat.</returns>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="enabledToolTypes"/> is <c>null</c>.</exception>
	public static IEnumerable<Type> SelectToolTypes(IEnumerable<Type> enabledToolTypes) {
		ArgumentNullException.ThrowIfNull(enabledToolTypes);
		HashSet<Type> lazySet = new(McpCoreToolProfile.CoreToolTypes);
		lazySet.UnionWith(McpCoreToolProfile.AlwaysOnLazyToolTypes);
		return enabledToolTypes.Where(lazySet.Contains).ToArray();
	}
}
