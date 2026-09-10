// ENG-95885 review finding: types that pin the framework-parameter exclusion boundary of
// McpToolArgumentSupport.IsFrameworkOwnedType. They live in their own file because two of them must be
// declared in namespaces the test fixture itself cannot use — one that merely LOOKS like the SDK's, and
// one that is plainly clio's — which a file-scoped namespace declaration cannot express.

namespace ModelContextProtocolLookalike {

	/// <summary>
	/// A perfectly ordinary args record that happens to live in a namespace whose NAME starts with
	/// "ModelContextProtocol". Nothing about it is framework-owned: it is declared in clio.tests and has
	/// no relationship to the MCP SDK. A namespace-prefix exclusion would swallow it and silently drop a
	/// tool's only bindable parameter; the assembly-keyed rule must treat it as bindable.
	/// </summary>
	public sealed record LookalikeNamespaceArgs(string Value);
}

namespace Clio.Tests.Command.McpServer.Boundary {

	/// <summary>
	/// An <c>McpServer</c> subclass declared OUTSIDE the MCP SDK assembly — the framework shape an
	/// assembly-identity check alone cannot see, which is why
	/// <c>McpToolArgumentSupport.IsFrameworkOwnedType</c> also asks
	/// <see cref="System.Type.IsAssignableFrom"/>. Declared <c>abstract</c> on purpose: the boundary test
	/// needs the TYPE, never an instance, and staying abstract keeps this fixture from having to
	/// re-implement the SDK's abstract session surface at every SDK upgrade.
	/// </summary>
	// MCPEXP002: the SDK marks McpServer's protected constructor experimental. Suppressed rather than
	// avoided because deriving from McpServer IS the thing under test - the predicate must exclude an
	// McpServer subclass declared outside the SDK assembly, and there is no other way to obtain such a
	// type. Nothing here is instantiated or shipped: the class stays abstract and only its Type is read.
#pragma warning disable MCPEXP002
	public abstract class HostDefinedMcpServer : ModelContextProtocol.Server.McpServer {
	}
#pragma warning restore MCPEXP002

	/// <summary>
	/// Method signatures whose <c>ParameterInfo</c>s the boundary test feeds to
	/// <c>McpToolArgumentSupport.IsBindableToolParameter</c>. Each one exists solely to carry a parameter
	/// type; none is ever invoked.
	/// </summary>
	public static class BoundaryParameterStubs {

		public static void TakesLookalikeNamespaceArgs(
			ModelContextProtocolLookalike.LookalikeNamespaceArgs args) {
		}

		public static void TakesHostDefinedMcpServer(HostDefinedMcpServer server) {
		}

		public static void TakesSdkServer(ModelContextProtocol.Server.McpServer server) {
		}

		public static void TakesSdkRequestContext(
			ModelContextProtocol.Server.RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams> context) {
		}

		public static void TakesSdkProtocolType(
			ModelContextProtocol.Protocol.CallToolRequestParams parameters) {
		}

		public static void TakesCancellationToken(System.Threading.CancellationToken cancellationToken) {
		}

		public static void TakesServiceProvider(System.IServiceProvider services) {
		}
	}
}
