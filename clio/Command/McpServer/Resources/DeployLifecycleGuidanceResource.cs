using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for the Creatio deploy/provisioning lifecycle through clio MCP:
/// infrastructure preflight, build discovery, deployment, registration, and gate installation.
/// </summary>
[McpServerResourceType]
public sealed class DeployLifecycleGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/deploy-lifecycle";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for the deploy/provisioning lifecycle through clio MCP.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "deploy-lifecycle-guidance")]
	[Description("Returns canonical MCP guidance for the Creatio deploy/provisioning lifecycle: assert-infrastructure -> show-passing-infrastructure -> find-empty-iis-port -> deploy-creatio, plus build discovery, registration, and cliogate installation.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP deploy lifecycle guide

		       Scope
		       - This guide owns the executable mechanics for provisioning a Creatio instance through clio MCP
		         and getting it ready for design-time and workspace tooling.
		       - For schema/page modeling once an instance is registered, follow `docs://mcp/guides/app-modeling`.
		       - Always read each tool's executable contract through `get-tool-contract` before its first call.

		       Deploy preflight (run in this order)
		       1. `assert-infrastructure` - full sweep across Kubernetes, local infrastructure, and filesystem.
		          Read `status` (pass/partial/fail) and `database-candidates`.
		       2. `show-passing-infrastructure` - narrow to only the choices that are safe to deploy against and read
		          `recommendedDeployment` (and `recommendedByEngine`) for the deploy-creatio argument bundle.
		       3. `find-empty-iis-port` - for a local IIS deployment, take `firstAvailablePort` as the deploy `site-port`.
		       4. Resolve the build archive - `deploy-creatio` needs an absolute `zip-file` path. Use the configured
		          `creatio-products` build folder to pick the desired version deterministically.

		       Deploy
		       - `deploy-creatio` is the most consequential, hardest-to-reverse tool: it drops and recreates the target
		         site. Required args: `site-name`, `zip-file` (absolute build archive path), `site-port`. Optional:
		         `db-server-name`, `redis-server-name` (omit to keep the default Kubernetes deployment path).
		       - Prefer the recommended bundle from `show-passing-infrastructure` and the port from `find-empty-iis-port`.
		       - Do not proceed if assert-infrastructure left the targeted database/Redis sections failing.

		       Post-deploy readiness
		       1. `reg-web-app` - register the freshly deployed instance as a named clio environment.
		       2. `install-gate` - install the cliogate package into the new environment. Downstream workspace and
		          package tooling (`restore-workspace`, `push-workspace`, `unlock-package`, ...) depend on it. If a
		          gate-dependent tool fails with "you need to install the cliogate package version ... or higher",
		          run `install-gate -e <environment>` and retry.
		       3. `restore-workspace` / `push-workspace` - move packages between the environment and a local workspace.

		       Failure policy
		       - If `assert-infrastructure` returns fail with no passing database candidates, stop with a blocker and
		         report the failing sections rather than guessing a deployment target.
		       - If `deploy-creatio` returns a non-zero `exit-code`, persist `execution-log-messages` and stop.
		       - If the configured `creatio-products` folder is missing or empty, fix the path (or place a build
		         there) before retrying deploy-creatio.
		       """
	};
}
