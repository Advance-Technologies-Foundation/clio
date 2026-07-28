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
	[Description("Returns canonical MCP guidance for the Creatio deploy/provisioning lifecycle: assert-infrastructure -> show-passing-infrastructure -> find-empty-iis-port -> deploy-creatio/deploy-identity, plus build discovery, registration, and cliogate installation.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP deploy lifecycle guide

		       Scope
		       - This guide owns the executable mechanics for provisioning a Creatio instance through clio MCP,
		         adding IdentityService, and getting it ready for design-time and workspace tooling.
		       - For schema/page modeling once an instance is registered, follow `docs://mcp/guides/app-modeling`.
		       - Always read each tool's executable contract through `get-tool-contract` before its first call.

		       Deploy preflight (run in this order)
		       1. `assert-infrastructure` - full sweep across Kubernetes, local infrastructure, and filesystem.
		          Read `status` (pass/partial/fail) and `database-candidates`.
		       2. `show-passing-infrastructure` - narrow to only the choices that are safe to deploy against and read
		          `recommendedDeployment` (and `recommendedByEngine`) for the deploy-creatio argument bundle.
		       3. `find-empty-iis-port` - for a local Creatio deployment, take `firstAvailablePort` as the deploy `sitePort`.
		          `deploy-identity` can call the same IIS port scanner internally when `identitySitePort` is omitted.
		       4. Resolve the build archive - `deploy-creatio` needs an absolute `zipFile` path. `deploy-identity`
		          accepts either a standalone `IdentityService.zip` or the same Creatio distribution bundle when it
		          contains `IdentityService.zip` at the configured `identityArchivePathInBundle` path. When `zipFile`
		          is omitted, `deploy-identity` finds `IdentityService.zip` under the registered environment's
		          `EnvironmentPath`.

		       Deploy
		       - `deploy-creatio` is the most consequential, hardest-to-reverse tool: it drops and recreates the target
		         site. Required args: `siteName`, `zipFile` (absolute build archive path), `sitePort`. Optional:
		         `dbServerName`, `redisServerName` (omit to keep the default Kubernetes deployment path), and
		         `useHttps` (local IIS only). HTTPS is opportunistic: clio uses a pinned or deterministically
		         selected usable LocalMachine/My certificate matching the host, and warns then continues with
		         HTTP when no usable certificate is installed.
		       - Prefer the recommended bundle from `show-passing-infrastructure` and the port from `find-empty-iis-port`.
		       - Do not proceed if assert-infrastructure left the targeted database/Redis sections failing.
		       - Deployments preserve the build database's existing forced-password-change state by default and do
		         not clear it automatically. deploy-creatio does not assign a new Supervisor password.

		       IdentityService
		       - `deploy-identity` deploys IdentityService to IIS for an already registered local Creatio environment,
		         updates `OAuth20IdentityServerUrl`, `OAuth20IdentityServerClientId`, and
		         `OAuth20IdentityServerClientSecret` through the platform sys-settings endpoint, creates a fresh clio
		         OAuth client bound to the existing `Supervisor` user by default, and stores only the returned clio
		         OAuth credentials in local clio appsettings.
		       - `noApp` deploys IdentityService and connects Creatio without creating any OAuth app. In that mode,
		         the command skips local clio credential persistence and `connect/token` verification because no
		         client exists. Do not combine `noApp` with `createTechUser` or `user`.
		       - `createTechUser` is opt-in. Use it only when a new technical user should be created for the fresh
		         OAuth app instead of binding the app to an existing user.
		       - `zipFile` is optional for `deploy-identity`: when omitted, the command finds `IdentityService.zip`
		         under the registered environment's `EnvironmentPath`.
		       - `identitySitePort` is optional for `deploy-identity`: when omitted, the command uses the first free
		         IIS port in range 40001-40100. Use `find-empty-iis-port` only when you need to inspect or override
		         the chosen port before deployment.
		       - Never echo the generated client secret in an MCP response, public room message, or log summary.
		       - The default `configurationMode` is `db-first`, but until direct DB seeding is fully proven the command
		         falls back to the supported REST/sys-settings configuration path.

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
