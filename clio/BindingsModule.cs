#pragma warning disable CLIO001 // This is DI class, warning not applicable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.ApplicationCommand;
using Clio.Command.BusinessRules;
using Clio.Command.ChainItems;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.IdentityServiceDeployment;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Resources;
using Clio.Command.PackageCommand;
using Clio.Command.ProcessModel;
using Clio.Command.RelatedPages;
using Clio.Command.SqlScriptCommand;
using Clio.Command.Theming;
using Clio.Command.TIDE;
using Clio.Command.Update;
using Clio.Common;
using Clio.Common.Assertions;
using Clio.Common.db;
using Clio.Common.DeploymentStrategies;
using Clio.Common.SystemServices;
using Clio.Common.Telemetry;
using Clio.Common.K8;
using Clio.Common.Kubernetes;
using Clio.Common.IIS;
using Clio.Common.Database;
using Clio.Common.ScenarioHandlers;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using Clio.ComposableApplication;
using Clio.Help;
using Clio.Package;
using Clio.Package.NuGet;
using Clio.Project;
using Clio.Project.NuGet;
using Clio.Query;
using Clio.Requests;
using Clio.Requests.Validators;
using Clio.Theming;
using Clio.Utilities;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Workspace;
using Clio.Workspaces;
using Clio.UserEnvironment;
using Clio.YAML;
using Creatio.Client;
using FluentValidation;
using k8s;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using FileSystem = System.IO.Abstractions.FileSystem;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio;

public enum BindingsModuleRegistrationProfile {
	Bootstrap,
	EnvironmentScoped
}

public class BindingsModule {

	#region Fields: Private

	public static string k8sDns = "127.0.0.1";
	private static readonly object BootstrapDiagnosticsSyncRoot = new();
	private static bool _bootstrapDiagnosticsLogged;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public BindingsModule(IFileSystem fileSystem = null){
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Builds the clio dependency-injection container, validating the whole graph at build time
	/// (<c>ValidateOnBuild</c> + <c>ValidateScopes</c>).
	/// </summary>
	/// <param name="settings">
	/// The environment settings to bind, or <c>null</c> to resolve them from the bootstrap profile.
	/// </param>
	/// <param name="additionalRegistrations">
	/// An optional hook invoked after the core registrations so tests (and callers) can add or
	/// override services before the provider is built.
	/// </param>
	/// <param name="profile">
	/// The registration profile. Defaults to <see cref="BindingsModuleRegistrationProfile.Bootstrap"/>
	/// when <paramref name="settings"/> is <c>null</c>, otherwise
	/// <see cref="BindingsModuleRegistrationProfile.EnvironmentScoped"/>.
	/// </param>
	/// <param name="applyBootstrapRepairs">
	/// When <c>true</c> the settings bootstrap service may repair/migrate <c>appsettings.json</c>;
	/// pass <c>false</c> for read-only help/parser builds that must not write settings.
	/// </param>
	/// <param name="registerMcpHost">
	/// When <c>true</c> registers the Model Context Protocol stdio host (<c>AddMcpServer</c> +
	/// transport + request filters + per-primitive schema generation) and the
	/// <see cref="Command.McpServer.McpServerCommand"/> that depends on the resulting
	/// <c>McpServer</c> singleton. Defaults to <c>false</c> (fail-safe): every non-mcp CLI build, the
	/// bootstrap build, and the per-environment <c>ToolCommandResolver</c> builds never resolve
	/// <c>McpServer</c>, so they skip the host registration and its eager schema-generation cost. Set
	/// <c>true</c> ONLY on the single container build from which <see cref="Command.McpServer.McpServerCommand"/>
	/// is resolved. Do NOT derive this from a process-wide static inside this module — it must be
	/// threaded explicitly so a live MCP session's per-environment builds stay gated off.
	/// </param>
	/// <param name="validateGraph">
	/// When <c>true</c> (default) the provider is built with <c>ValidateOnBuild</c> + <c>ValidateScopes</c>
	/// so a scope/lifetime or missing-registration mistake fails fast. Pass <c>false</c> ONLY for the
	/// per-request ephemeral session-container builds on the mcp-http credential-passthrough hot path
	/// (review): the <see cref="BindingsModuleRegistrationProfile.EnvironmentScoped"/> graph SHAPE is
	/// structurally invariant across tenants (only <see cref="EnvironmentSettings"/> values differ), so it
	/// is validated once at host startup via <see cref="ValidateEnvironmentScopedGraph"/> and re-validating
	/// on every rotating-token cache miss is pure startup-grade cost. Never pass <c>false</c> for a build
	/// whose graph shape is not already covered by that one-time startup validation.
	/// </param>
	/// <returns>The built (and, unless <paramref name="validateGraph"/> is <c>false</c>, validated) service provider.</returns>
	public IServiceProvider Register(EnvironmentSettings settings = null,
		Action<IServiceCollection> additionalRegistrations = null,
		BindingsModuleRegistrationProfile? profile = null,
		bool applyBootstrapRepairs = true,
		bool registerMcpHost = false,
		bool validateGraph = true){
		IServiceCollection services = new ServiceCollection();
		ISettingsRepository settingsRepository = RegisterInto(services, settings, profile, applyBootstrapRepairs);
		if (registerMcpHost) {
			services.AddTransient<McpServerCommand>();
			// The durable (forgiving) unmatched-name handler is registered HERE — at the stdio call-site —
			// and deliberately NOT inside the transport-neutral RegisterMcpServer, which the unreleased
			// mcp-http host also calls (McpHttpServerCommand): the forgiving invocation contract is scoped
			// to the stdio transport only (ADR adr-mcp-durable-invocation, D1). The SDK invokes the handler
			// only on a ToolCollection miss, so advertised (resident) tools are never shadowed.
			RegisterMcpServer(services, settingsRepository)
				.WithStdioServerTransport()
				.WithCallToolHandler(static (request, cancellationToken) =>
					request.Services.GetRequiredService<Command.McpServer.IMcpDurableCallToolHandler>()
						.HandleAsync(request, cancellationToken));
			// The invoker registry's constructor reflects every enabled [McpServerToolType] and
			// SDK-builds the full tool map (~165 methods) — far too expensive to rebuild per call, which
			// the assembly-scan transient registration would do (and the unmatched-name path resolves it
			// twice: handler + executor). Pin both the registry and the compatibility catalog as
			// singletons for the host's lifetime; the tool surface is fixed at process start anyway
			// (tools/list is registered once), so a singleton also makes feature-flag reads consistent
			// for the whole session.
			// ORDERING DEPENDENCY: both types are ALSO auto-registered as transients by the reflection
			// interface-scan inside RegisterInto (they implement Clio.* interfaces). These AddSingleton
			// calls win only because RegisterInto ran earlier (line above) — last-registration-wins. If the
			// scan were ever moved after this block, the lifetime would silently revert to transient and
			// rebuild the ~165-tool map on every unmatched-name call. Keep this block after RegisterInto.
			services.AddSingleton<Command.McpServer.Tools.IMcpToolInvokerRegistry,
				Command.McpServer.Tools.McpToolInvokerRegistry>();
			services.AddSingleton<Command.McpServer.IMcpToolCompatibilityCatalog,
				Command.McpServer.McpToolCompatibilityCatalog>();
		}
		additionalRegistrations?.Invoke(services);
		ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
			ValidateOnBuild = validateGraph,
			ValidateScopes = validateGraph
		});
		if (registerMcpHost) {
			// Fail-fast validation of the durable-invocation surface. ValidateOnBuild verifies the DI
			// graph's call sites but does NOT instantiate transients/singletons, so a malformed
			// compatibility catalog (duplicate canonical/alias) or a duplicate MCP tool NAME would
			// otherwise surface only on the first tools/call. Resolving both here makes a malformed
			// surface abort HOST STARTUP instead — the constructors throw on any collision.
			provider.GetRequiredService<Command.McpServer.IMcpToolCompatibilityCatalog>();
			provider.GetRequiredService<Command.McpServer.Tools.IMcpToolInvokerRegistry>();
		}
		return provider;
	}

	/// <summary>
	/// Validates the <see cref="BindingsModuleRegistrationProfile.EnvironmentScoped"/> graph SHAPE once —
	/// builds a representative environment-scoped container with <c>ValidateOnBuild</c> + <c>ValidateScopes</c>
	/// and disposes it. Called once at mcp-http host startup so the per-request ephemeral builds can skip
	/// per-build validation (review) while a scope/lifetime or missing-registration mistake in that profile
	/// still fails fast at startup rather than on the first passthrough request. The representative settings
	/// are non-connecting (a loopback URI); validation reflects over the graph without any Creatio round-trip
	/// because every client is registered lazily.
	/// </summary>
	public static void ValidateEnvironmentScopedGraph() {
		EnvironmentSettings representative = new() { Uri = $"{Uri.UriSchemeHttp}://localhost", IsNetCore = true };
		IServiceProvider probe = new BindingsModule().Register(
			representative, profile: BindingsModuleRegistrationProfile.EnvironmentScoped, validateGraph: true);
		(probe as IDisposable)?.Dispose();
	}

	/// <summary>
	/// Registers all clio services into the supplied <paramref name="services"/> collection without
	/// building the provider. Use <see cref="Register"/> for a self-contained build; call this method
	/// directly when injecting clio's DI graph into an external host (e.g.
	/// <c>WebApplicationBuilder.Services</c> for the HTTP MCP transport).
	/// </summary>
	/// <returns>The <see cref="ISettingsRepository"/> needed by <see cref="RegisterMcpServer"/>.</returns>
	internal ISettingsRepository RegisterInto(
		IServiceCollection services,
		EnvironmentSettings settings = null,
		BindingsModuleRegistrationProfile? profile = null,
		bool applyBootstrapRepairs = true) {
		BindingsModuleRegistrationProfile registrationProfile = profile
			?? (settings is null ? BindingsModuleRegistrationProfile.Bootstrap : BindingsModuleRegistrationProfile.EnvironmentScoped);
		RegisterAssemblyInterfaceTypes(services);
		services.AddTransient(sp => new EntitySchemaColumnResolvers(
			sp.GetRequiredService<IEntitySchemaDefaultValueSourceResolver>(),
			sp.GetRequiredService<ILookupDefaultDisplayValueResolver>(),
			sp.GetRequiredService<IEntitySchemaCaptionCultureResolver>()));
		services.AddSingleton<IWorkspacePathBuilder, WorkspacePathBuilder>();
		services.AddTransient<IVsProjectFactory, VsProjectFactory>();
		services.AddTransient<ICreatioPkgProjectCreator, CreatioPkgProjectCreator>();
		services.AddSingleton<ILogger>(ConsoleLogger.Instance);
		services.AddSingleton<IDbOperationLogContextAccessor, DbOperationLogContextAccessor>();
		services.AddSingleton<IDbOperationLogSessionFactory, DbOperationLogSessionFactory>();
		services.AddTransient<IContainerRegistryCredentialProvider, ContainerRegistryCredentialProvider>();
		services.AddHttpClient();
		services.AddTransient<IRingDistributionService, RingDistributionService>();
		services.AddTransient<RingCommand>();
		services.AddHttpClient<IContainerRegistryPreflightService, ContainerRegistryPreflightService>();
		// Named HttpClient for the component-registry CDN + docs pipelines. Timeout is
		// configured once here so callers never mutate HttpClient.Timeout after construction
		// (avoids `InvalidOperationException` on reused instances and races on a shared
		// mutable property — see code-review #1 on PR #599).
		services.AddHttpClient(ComponentRegistryClient.HttpClientName)
			.ConfigureHttpClient(client => client.Timeout = ComponentRegistryClient.CdnFetchTimeout);
		// Dedicated forms-auth client for browser-session harvesting. UseCookies=false keeps the
		// Set-Cookie response headers readable (the cookie jar would otherwise consume them), and
		// AllowAutoRedirect=false ensures the direct AuthService.svc/Login response is observed
		// rather than a followed login-page redirect.
		services.AddHttpClient(Clio.Common.BrowserSession.CreatioAuthClient.HttpClientName)
			.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(30))
			.ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler {
				UseCookies = false,
				AllowAutoRedirect = false
			});
		// Named HttpClient for background telemetry uploads — same registration-time-only
		// timeout rule as the component-registry client above.
		services.AddHttpClient(TelemetryFlushService.HttpClientName)
			.ConfigureHttpClient(client => client.Timeout = TelemetryFlushService.PostTimeout);

		ISettingsBootstrapService settingsBootstrapService = new SettingsBootstrapService(_fileSystem, applyBootstrapRepairs);
		SettingsBootstrapResult bootstrapResult = settingsBootstrapService.GetResult();
		// RealInteractiveConsole fails closed on redirected stdin (MCP stdio / CI), so this single
		// composition-root binding is correct for both the interactive CLI and the per-environment
		// MCP containers that ToolCommandResolver builds from BindingsModule.
		IInteractiveConsole interactiveConsole = new RealInteractiveConsole();
		services.AddSingleton<IInteractiveConsole>(interactiveConsole);
		SettingsRepository settingsRepository = new(_fileSystem, settingsBootstrapService, interactiveConsole);
		services.AddSingleton<ISettingsBootstrapService>(settingsBootstrapService);
		services.AddSingleton<ISettingsRepository>(settingsRepository);
		LogBootstrapDiagnostics(registrationProfile, bootstrapResult.Report);

		EnvironmentSettings activeSettings = ResolveActiveSettings(settings, registrationProfile, bootstrapResult);

		if (activeSettings is not null) {
			RegisterActiveEnvironmentServices(services, activeSettings);
		}

		services.AddTransient<IKubernetes>(_ => CreateKubernetesClient());

		// NoReauthExecutor is the ONLY DI-resolved IReauthExecutor: it is injected into
		// ApplicationClientFactory to build the credential-passthrough (bearer) client, which must
		// never re-login. It does NOT hijack reauth globally — the login/password + OAuth paths use
		// CreatioClientAdapter's own internal closure-based ReauthExecutor (not resolved from DI).
		services.AddSingleton<IReauthExecutor, NoReauthExecutor>();

		services.AddTransient<IKubernetesClient, KubernetesClient>();
		services.AddTransient<K8ContextValidator>();
		services.AddTransient<IK8ServiceResolver, K8ServiceResolver>();
		services.AddTransient<IK8DatabaseDiscovery, K8DatabaseDiscovery>();
		services.AddTransient<IDatabaseConnectivityChecker, DatabaseConnectivityChecker>();
		services.AddTransient<IDatabaseCapabilityChecker, DatabaseCapabilityChecker>();
		services.AddTransient<IRedisDatabaseSelector, RedisDatabaseSelector>();
		services.AddTransient<K8DatabaseAssertion>();
		services.AddTransient<K8RedisAssertion>();
		services.AddTransient<Common.Assertions.FsPathAssertion>();
		services.AddTransient<Common.Assertions.FsPermissionAssertion>();
		services.AddTransient<ILocalDatabaseAssertion, LocalDatabaseAssertion>();
		services.AddTransient<ILocalRedisAssertion, LocalRedisAssertion>();
		services.AddTransient<k8Commands>();
		services.AddTransient<IInfrastructurePathProvider, InfrastructurePathProvider>();
		services.AddTransient<IDeployCreatioDefaultsResolver, DeployCreatioDefaultsResolver>();
		services.AddTransient<InstallerCommand>();
		services.AddTransient<PinCertificateCommand>();
		services.AddTransient<DeployIdentityCommand>();
		services.AddTransient<IIdentityServiceArchiveResolver, IdentityServiceArchiveResolver>();
		services.AddTransient<IIdentityServiceCreatioClient, IdentityServiceCreatioClient>();
		services.AddTransient<IIdentityServiceRoleGrantService, IdentityServiceRoleGrantService>();
		services.AddTransient<IIdentityServiceSystemUserResolver, IdentityServiceSystemUserResolver>();
		services.AddTransient<IIdentityServiceDeploymentService, IdentityServiceDeploymentService>();
		services.AddSingleton<Command.OAuthAppConfiguration.IIdentityServerUrlResolver, Command.OAuthAppConfiguration.IdentityServerUrlResolver>();
		services.AddTransient<Command.OAuthAppConfiguration.IIdentityServerProbe, Command.OAuthAppConfiguration.IdentityServerProbe>();
		services.AddTransient<Command.OAuthAppConfiguration.GetIdentityServiceConfigCommand>();
		services.AddTransient<Command.OAuthAppConfiguration.ResolveOAuthSystemUserCommand>();
		services.AddTransient<Command.OAuthAppConfiguration.CreateOAuthTechnicalUserCommand>();
		services.AddTransient<Command.OAuthAppConfiguration.CreateServerToServerOAuthAppCommand>();
		services.AddTransient<Command.OAuthAppConfiguration.VerifyOAuthAppCommand>();
		services.AddTransient<IDockerTemplatePathProvider, DockerTemplatePathProvider>();
		services.AddTransient<IBuildDockerImageService, BuildDockerImageService>();
		services.AddHttpClient<ICodeServerArchiveCache, CodeServerArchiveCache>();

		if (_fileSystem is not null) {
			services.AddSingleton(_fileSystem);
		}
		else {
			services.AddTransient<IFileSystem, FileSystem>();
		}

		services.AddTransient<Clio.Command.RecordRights.GetRecordRightsCommand>();
		services.AddTransient<Clio.Command.RecordRights.SetRecordRightsCommand>();
		services.AddTransient<Clio.Common.IFileSystem, Clio.Common.FileSystem>();
		services.AddTransient<IFileSecurityHardening, FileSecurityHardening>();
		services.AddTransient<Clio.Common.BrowserSession.IBrowserSessionCache, Clio.Common.BrowserSession.BrowserSessionCache>();
		services.AddTransient<Clio.Common.BrowserSession.ICreatioAuthClient, Clio.Common.BrowserSession.CreatioAuthClient>();
		services.AddTransient<Clio.Common.BrowserSession.IBrowserSessionService, Clio.Common.BrowserSession.BrowserSessionService>();
		services.AddTransient<Clio.Common.BrowserSession.IChromiumLocator, Clio.Common.BrowserSession.ChromiumLocator>();
		services.AddTransient<Clio.Common.BrowserSession.IAuthenticatedBrowserLauncher, Clio.Common.BrowserSession.AuthenticatedBrowserLauncher>();
		IDeserializer deserializer = new DeserializerBuilder()
			.WithNamingConvention(UnderscoredNamingConvention.Instance)
			.IgnoreUnmatchedProperties()
			.Build();
		ISerializer serializer = new SerializerBuilder()
			.WithNamingConvention(UnderscoredNamingConvention.Instance)
			.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
			.Build();
		services.AddSingleton(deserializer);
		services.AddSingleton(serializer);

		services.AddTransient<IProcessExecutor, ProcessExecutor>();
		services.AddTransient<IDotnetExecutor, DotnetExecutor>();
		services.AddTransient<IPackageUtilities, PackageUtilities>();
		services.AddKeyedTransient<IFollowupUpChainItem, DconfChainItem>(nameof(DconfChainItem));
		services.AddTransient<IFollowUpChain, FollowUpChain>();
		services.AddTransient<FeatureCommand>();
		services.AddTransient<SetFileContentStorageConnectionStringCommand>();
		services.AddTransient<SysSettingsCommand>();
		services.AddTransient<BuildInfoCommand>();
		services.AddTransient<BuildDockerImageCommand>();
		services.AddTransient<InstallSkillsCommand>();
		services.AddTransient<UpdateSkillCommand>();
		services.AddTransient<DeleteSkillCommand>();
		services.AddTransient<BuildThemeCommand>();
		services.AddTransient<PushPackageCommand>();
		services.AddTransient<InstallApplicationCommand>();
		services.AddTransient<IApplicationSectionCreateService, ApplicationSectionCreateService>();
		services.AddTransient<CreateAppSectionCommand>();
		services.AddTransient<IApplicationSectionUpdateService, ApplicationSectionUpdateService>();
		services.AddTransient<UpdateAppSectionCommand>();
		services.AddTransient<IAddonSchemaDesignerClient, AddonSchemaDesignerClient>();
		services.AddTransient<IPageSchemaResolver, PageSchemaResolver>();
		services.AddTransient<IRelatedPageAddonService, RelatedPageAddonService>();
		services.AddTransient<IBusinessRuleAddonService, BusinessRuleAddonService>();
		services.AddTransient<IBusinessRulePackageResolver, BusinessRulePackageResolver>();
		services.AddTransient<IBusinessRuleFormulaValidationService, BusinessRuleFormulaValidationService>();
		services.AddTransient<IBusinessRuleLookupReferenceValidator, BusinessRuleLookupReferenceValidator>();
		services.AddTransient<IBusinessRuleValidator, BusinessRuleValidator>();
		services.AddTransient<IEntityBusinessRuleSchemaProvider, EntityBusinessRuleSchemaProvider>();
		services.AddTransient<IEntityBusinessRuleAttributeProvider, EntityBusinessRuleAttributeProvider>();
		services.AddTransient<IEntityBusinessRuleService, EntityBusinessRuleService>();
		services.AddTransient<IPageBusinessRuleSchemaProvider, PageBusinessRuleSchemaProvider>();
		services.AddTransient<IPageBusinessRuleAttributeProvider, PageBusinessRuleAttributeProvider>();
		services.AddTransient<IPageBusinessRuleElementProvider, PageBusinessRuleElementProvider>();
		services.AddTransient<IPageBusinessRuleValidator, PageBusinessRuleValidator>();
		services.AddTransient<IPageBusinessRuleService, PageBusinessRuleService>();
		services.AddTransient<IFeatureToggleService, FeatureToggleService>();
		services.AddTransient<IApplicationSectionDeleteService, ApplicationSectionDeleteService>();
		services.AddTransient<DeleteAppSectionCommand>();
		services.AddTransient<IListUserTasksService, ListUserTasksService>();
		services.AddTransient<ListUserTasksCommand>();
		services.AddTransient<ICreateBusinessProcessService, CreateBusinessProcessService>();
		services.AddTransient<CreateBusinessProcessCommand>();
		services.AddTransient<IModifyBusinessProcessService, ModifyBusinessProcessService>();
		services.AddTransient<ModifyBusinessProcessCommand>();
		services.AddTransient<IApplicationSectionGetListService, ApplicationSectionGetListService>();
		services.AddTransient<GetAppSectionsCommand>();
		services.AddTransient<IdentityProviderListCommand>();
		services.AddTransient<IdentityProviderUpsertCommand>();
		services.AddTransient<IdentityProviderSetSecretCommand>();
		services.AddTransient<IdentityProviderDeleteCommand>();
		services.AddTransient<IdentityProviderSetDefaultCommand>();
		services.AddTransient<IdentityProviderBindCommand>();
		services.AddTransient<IdentityProviderUnbindCommand>();
		services.AddTransient<IdentityProviderServicesCommand>();
		services.AddTransient<CreateAppCommand>();
		services.AddTransient<GetAppInfoCommand>();
		services.AddTransient<CreateLookupCommand>();
		services.AddTransient<PageListCommand>();
		services.AddTransient<PageGetCommand>();
		services.AddTransient<PageUpdateCommand>();
		// Shared page conflict-baseline + file-output services consumed by both the CLI verbs
		// (get-page / update-page) and the MCP tools (get-page / update-page / sync-pages).
		services.AddTransient<IPageBaselineGuard, PageBaselineGuard>();
		services.AddTransient<IPageFileWriter, PageFileWriter>();
		services.AddTransient<PageCreateCommand>();
		services.AddTransient<CreateRelatedPageAddonCommand>();
		services.AddTransient<GetRelatedPageAddonCommand>();
		services.AddTransient<PageTemplatesListCommand>();
		services.AddTransient<SourceCodeSchemaCreateCommand>();
		services.AddTransient<SourceCodeSchemaUpdateCommand>();
		services.AddTransient<GetSourceCodeSchemaCommand>();
		services.AddTransient<ClientUnitSchemaCreateCommand>();
		services.AddTransient<ClientUnitSchemaUpdateCommand>();
		services.AddTransient<GetClientUnitSchemaCommand>();
		services.AddTransient<GetClassicMigrationBundleCommand>();
		services.AddTransient<ListEntityClientSchemasCommand>();
		services.AddTransient<SqlSchemaCreateCommand>();
		services.AddTransient<SqlSchemaGetCommand>();
		services.AddTransient<SqlSchemaUpdateCommand>();
		services.AddTransient<SqlSchemaInstallCommand>();
		services.AddTransient<ISchemaTemplateCatalog, SchemaTemplateCatalog>();
		services.AddTransient<IPageDesignerHierarchyClient, PageDesignerHierarchyClient>();
		services.AddTransient<IPageSchemaBodyParser, PageSchemaBodyParser>();
		services.AddTransient<IPageJsonDiffApplier, PageJsonDiffApplier>();
		services.AddTransient<IPageJsonPathDiffApplier, PageJsonPathDiffApplier>();
		services.AddTransient<IPageBundleBuilder, PageBundleBuilder>();
		services.AddSingleton<TimeProvider>(TimeProvider.System);
		services.AddSingleton<IComponentRegistryCacheStore, ComponentRegistryCacheStore>();
		services.AddSingleton<IComponentRegistryDocsCacheStore, ComponentRegistryDocsCacheStore>();
		services.AddSingleton<IComponentRegistryClient, ComponentRegistryClient>();
		// Mobile shares the same client implementation but uses a separate cache
		// subdirectory + a different CDN file + its own local-override env var. The
		// cache store is registered as a flavor-specific factory to keep the disk
		// layout web/mobile-isolated.
		services.AddSingleton<IMobileComponentRegistryClient>(sp => new MobileComponentRegistryClient(
			sp.GetRequiredService<IHttpClientFactory>(),
			ComponentRegistryCacheStore.WithSubdirectory(
				sp.GetRequiredService<System.IO.Abstractions.IFileSystem>(),
				sp.GetRequiredService<TimeProvider>(),
				RegistryFlavor.Mobile.CacheSubdirectoryName),
			sp.GetRequiredService<System.IO.Abstractions.IFileSystem>(),
			sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MobileComponentRegistryClient>>()));
		// Requests flavor (Freedom UI request catalog, get-request-info): same transport
		// chain, its own CDN file / cache subdirectory / local-override env var. The
		// envelope differs from components, so parsing goes through RequestInfoCatalog.
		services.AddSingleton<IRequestRegistryClient>(sp => new RequestRegistryClient(
			sp.GetRequiredService<IHttpClientFactory>(),
			ComponentRegistryCacheStore.WithSubdirectory(
				sp.GetRequiredService<System.IO.Abstractions.IFileSystem>(),
				sp.GetRequiredService<TimeProvider>(),
				RegistryFlavor.Requests.CacheSubdirectoryName),
			sp.GetRequiredService<System.IO.Abstractions.IFileSystem>(),
			sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RequestRegistryClient>>()));
		services.AddSingleton<IComponentRegistryDocsClient, ComponentRegistryDocsClient>();
		services.AddSingleton<IComponentInfoCatalog, ComponentInfoCatalog>();
		services.AddSingleton<IMobileComponentInfoCatalog, MobileComponentInfoCatalog>();
		services.AddSingleton<IThemeCssBuilder, ThemeCssBuilder>();
		services.AddSingleton<IThemeTemplateProvider, ThemeTemplateProvider>();
		services.AddSingleton<IThemePaletteAdvisor, ThemePaletteAdvisor>();
		services.AddSingleton<IRequestInfoCatalog, RequestInfoCatalog>();
		// Only the per-environment IPlatformVersionResolverFactory is registered: both the
		// get-component-info MCP tool and the CLI verb resolve the platform version from
		// per-call arguments (environment-name / uri / version), never from an ambient
		// singleton bound to the server's startup environment. A singleton resolver would
		// probe the wrong environment and report a falsely authoritative "environment" tier.
		services.AddSingleton<IPlatformVersionResolverFactory, PlatformVersionResolverFactory>();
		// Profile-culture resolution (ENG-91044): the cache is a singleton so a resolved culture
		// survives across CLI/MCP calls (env-URI-keyed); the factory builds a per-environment
		// resolver per call, sharing that singleton cache.
		services.AddSingleton<ICurrentUserCultureCache, CurrentUserCultureCache>();
		services.AddSingleton<ICurrentUserCultureResolverFactory, CurrentUserCultureResolverFactory>();
		services.AddTransient<GetUserCultureCommand>();
		services.AddTransient<ComponentRegistryRefreshCommand>();
		services.AddTransient<ComponentInfoCommand>();
		
		// MCP Tools
		services.AddTransient<PageListTool>();
		services.AddTransient<ApplicationGetListTool>();
		services.AddTransient<ApplicationGetInfoTool>();
		services.AddTransient<ApplicationCreateTool>();
		services.AddTransient<ApplicationSectionCreateTool>();
		services.AddTransient<ApplicationSectionUpdateTool>();
		services.AddTransient<CreateEntityBusinessRuleTool>();
		services.AddTransient<CreatePageBusinessRuleTool>();
		services.AddTransient<ReadEntityBusinessRuleTool>();
		services.AddTransient<ReadPageBusinessRuleTool>();
		services.AddTransient<UpdateEntityBusinessRuleTool>();
		services.AddTransient<UpdatePageBusinessRuleTool>();
		services.AddTransient<DeleteEntityBusinessRuleTool>();
		services.AddTransient<DeletePageBusinessRuleTool>();
		services.AddTransient<ApplicationSectionDeleteTool>();
		services.AddTransient<ApplicationSectionGetListTool>();
		services.AddTransient<ApplicationDeleteTool>();
		services.AddTransient<ToolContractGetTool>();
		services.AddTransient<ValidateProcessGraphTool>();
		services.AddTransient<DescribeProcessTool>();
		// Singleton: the service is effectively stateless (its only shared mutable state is a static
		// lock), so a single instance is safe and keeps the lifetime consistent with the singleton
		// flusher that depends on it (no captured-dependency lifetime mismatch).
		services.AddSingleton<ITelemetryService>(sp => new TelemetryService(
			sp.GetRequiredService<IFileSystem>(),
			timeProvider: sp.GetRequiredService<TimeProvider>(),
			logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TelemetryService>>()));
		services.AddSingleton<ITelemetryFlushOptionsProvider, TelemetryFlushOptionsProvider>();
		services.AddSingleton<ITelemetryFlushService>(sp => new TelemetryFlushService(
			sp.GetRequiredService<IFileSystem>(),
			sp.GetRequiredService<IHttpClientFactory>(),
			sp.GetRequiredService<ITelemetryService>(),
			sp.GetRequiredService<ITelemetryFlushOptionsProvider>(),
			timeProvider: sp.GetRequiredService<TimeProvider>(),
			logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TelemetryFlushService>>()));
		services.AddSingleton<ITelemetryFlushScheduler>(sp => new TelemetryFlushScheduler(
			sp.GetRequiredService<ITelemetryFlushService>(),
			sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TelemetryFlushScheduler>>()));
		services.AddTransient<GetTelemetryConsentTool>();
		services.AddTransient<SendTelemetryTool>();
		services.AddTransient<WithdrawTelemetryConsentTool>();
		services.AddTransient<PageGetTool>();
		services.AddTransient<PageUpdateTool>();
		services.AddTransient<PageCreateTool>();
		services.AddTransient<CreateRelatedPageAddonTool>();
		services.AddTransient<GetRelatedPageAddonTool>();
		services.AddTransient<PageTemplatesListTool>();
		services.AddTransient<SchemaCreateTool>();
		services.AddTransient<SchemaUpdateTool>();
		services.AddTransient<GetSchemaTool>();
		services.AddTransient<GetProcessSignatureTool>();
		services.AddTransient<ListPrintablesTool>();
		services.AddTransient<ClientUnitSchemaCreateTool>();
		services.AddTransient<ClientUnitSchemaUpdateTool>();
		services.AddTransient<GetClientUnitSchemaTool>();
		services.AddTransient<GetClassicMigrationBundleTool>();
		services.AddTransient<ListEntityClientSchemasTool>();
		services.AddTransient<SqlSchemaCreateTool>();
		services.AddTransient<SqlSchemaGetTool>();
		services.AddTransient<SqlSchemaUpdateTool>();
		services.AddTransient<SqlSchemaInstallTool>();
		services.AddTransient<DeleteSchemaTool>();
		services.AddTransient<PageSyncTool>();
		services.AddSingleton<IPageBodySamplingService, PageBodySamplingServiceImpl>();
		services.AddTransient<GuidanceGetTool>();
		services.AddTransient<ComponentInfoTool>();
		services.AddTransient<RequestInfoTool>();
		services.AddTransient<BuildThemeTool>();
		services.AddTransient<AdviseThemePaletteTool>();
		services.AddTransient<ClearThemesCacheTool>();
		services.AddTransient<ListThemesTool>();
		services.AddTransient<CreateThemeTool>();
		services.AddTransient<UpdateThemeTool>();
		services.AddTransient<DeleteThemeTool>();
		services.AddTransient<SetUserThemeTool>();
		services.AddTransient<CheckThemingAccessTool>();
		services.AddTransient<GetUserCultureTool>();
		services.AddTransient<GetRecordRightsTool>();
		services.AddTransient<SetRecordRightsTool>();
		services.AddTransient<PackageHotfixTool>();
		services.AddTransient<AddPackageDependencyTool>();
		services.AddTransient<RemovePackageDependencyTool>();
		services.AddTransient<CreateUiProjectTool>();
		services.AddTransient<DataForgeTool>();
		services.AddTransient<SysSettingGetTool>();
		services.AddTransient<SysSettingsListTool>();
		services.AddTransient<SysSettingCreateTool>();
		services.AddTransient<SysSettingUpdateTool>();
		services.AddTransient<InstallGateTool>();
		services.AddTransient<ExperimentalTool>();
		services.AddTransient<ListCreatioBuildsTool>();
		services.AddTransient<GetCreatioInfoTool>();
		services.AddTransient<IDataForgeEnrichmentBuilder, DataForgeEnrichmentBuilder>();
		services.AddTransient<IApplicationCreateEnrichmentService, ApplicationCreateEnrichmentService>();
		services.AddTransient<ISchemaEnrichmentService, SchemaEnrichmentService>();
		// Shared null-object defaults for the credential-passthrough seam so ToolCommandResolver's
		// ctor deps are always satisfiable (stdio host + per-environment ephemeral containers, where
		// the real accessor/validator are absent). The mcp-http host registers the REAL
		// CredentialContextAccessor + TargetUrlValidator AFTER this shared build (McpHttpServerCommand.Run),
		// so last-registration-wins resolves the real ones in HTTP and these null objects everywhere else.
		// Both interfaces stay in the RegisterAssemblyInterfaceTypes skip-list, which suppresses
		// auto-registration of BOTH the real and the null implementations (the skip is keyed on the interface).
		services.AddSingleton<ICredentialContextAccessor, NullCredentialContextAccessor>();
		services.AddSingleton<ITargetUrlValidator, NullTargetUrlValidator>();
		// Centralized credential-passthrough fail-fast guard (FR-04, ENG-93347). Reads the SAME
		// ICredentialContextAccessor seam registered above (null object here, the real per-request
		// accessor in the mcp-http host via last-registration-wins), so the guard is inert on
		// stdio/CLI and fires only on authorized passthrough requests. Consumed by guard-only tools
		// (link-from-repository-*) through the BaseTool.RejectIfPassthroughUnsupported helper.
		services.AddSingleton<ICredentialPassthroughToolGuard, CredentialPassthroughToolGuard>();
		// Shared DEFAULT session-container cache (FR-08). The mcp-http host re-registers a run-time
		// configured instance AFTER this shared build (McpHttpServerCommand.Run) from --session-idle-ttl
		// / --max-sessions, so last-registration-wins gives the configured cache in HTTP and this
		// default everywhere else. It stays in the RegisterAssemblyInterfaceTypes skip-list: the impl
		// ctor takes primitive TimeSpan/int args that cannot be auto-resolved, so an auto-registration
		// would break ValidateOnBuild.
		services.AddSingleton<ISessionContainerCache>(_ => new SessionContainerCache(
			SessionContainerCacheDefaults.IdleTtl, SessionContainerCacheDefaults.MaxSessions));
		// Per-tenant execution lock provider (FR-05). The process-wide shared instance so the SAME
		// tenant serializes on the SAME lock regardless of which container (root or per-session
		// ephemeral) the call flows through, while DIFFERENT tenants use distinct locks.
		services.AddSingleton<ITenantExecutionLockProvider>(TenantExecutionLockProvider.Shared);
		services.AddTransient<IToolCommandResolver, ToolCommandResolver>();
		services.AddTransient<IDataForgePlatformVersionGuard, DataForgePlatformVersionGuard>();
		services.AddTransient<IDataForgeReadClient, DataForgeReadClient>();
		services.AddTransient<IDataForgeMaintenanceClient, DataForgeMaintenanceClient>();
		services.AddTransient<IRuntimeEntitySchemaReader, RuntimeEntitySchemaReader>();
		services.AddTransient<IDataForgeContextService, DataForgeContextService>();
		services.AddTransient<ODataReadTool>();
		services.AddTransient<ODataCreateTool>();
		services.AddTransient<ODataUpdateTool>();
		services.AddTransient<ODataDeleteTool>();
		services.AddTransient<OpenCfgCommand>();
		services.AddTransient<InstallGateCommand>();
		services.AddTransient<PingAppCommand>();
		services.AddTransient<ReferenceCommand>();
		// NewPkgCommand depends on the reference command via its Command<ReferenceOptions> base type.
		services.AddTransient<Command<ReferenceOptions>, ReferenceCommand>();
		services.AddTransient<NewPkgCommand>();
		services.AddTransient<SqlScriptCommand>();
		services.AddTransient<CompressPackageCommand>();
		services.AddTransient<PushNuGetPackagesCommand>();
		services.AddTransient<PackNuGetPackageCommand>();
		services.AddTransient<RestoreNugetPackageCommand>();
		services.AddTransient<InstallNugetPackageCommand>();
		services.AddTransient<SetPackageVersionCommand>();
		services.AddTransient<GetPackageVersionCommand>();
		services.AddTransient<CheckNugetUpdateCommand>();
		services.AddHttpClient<INugetPackagesProvider, NugetPackagesProvider>();
		services.AddTransient<UpdateCliCommand>();
		services.AddTransient<SetAutoupdateCommand>();
		services.AddTransient<ExperimentalCommand>();
		services.AddTransient<ConfigCommand>();
		services.AddTransient<RegisterCommand>();
		services.AddTransient<UnregisterCommand>();
		
		services.AddTransient<IUserPromptService, UserPromptService>();
		services.AddTransient<DeletePackageCommand>();
		services.AddTransient<GetPkgListCommand>();
		services.AddTransient<RestoreWorkspaceCommand>();
		services.AddTransient<CreateWorkspaceCommand>();
		services.AddTransient<PushWorkspaceCommand>();
		services.AddSingleton<IDataBindingTemplateSchemaCatalog, DataBindingTemplateCatalog>();
		services.AddSingleton<IDataBindingTemplateCatalog>(provider =>
			provider.GetRequiredService<IDataBindingTemplateSchemaCatalog>());
		services.AddTransient<IDataBindingSchemaClient, DataBindingSchemaClient>();
		services.AddTransient<IDataBindingSchemaResolver, DataBindingSchemaResolver>();
		services.AddTransient<IDataBindingSerializer, DataBindingSerializer>();
		services.AddTransient<IDataBindingValueConverter, DataBindingValueConverter>();
		services.AddTransient<IDataBindingDisplayValueResolver, DataBindingDisplayValueResolver>();
		services.AddTransient<IDataBindingService, DataBindingService>();
		services.AddTransient<ILookupRegistrationService, LookupRegistrationService>();
		services.AddTransient<CreateDataBindingCommand>();
		services.AddTransient<AddDataBindingRowCommand>();
		services.AddTransient<RemoveDataBindingRowCommand>();
		services.AddTransient<IDataBindingDbService, DataBindingDbService>();
		services.AddTransient<CreateDataBindingDbCommand>();
		services.AddTransient<UpsertDataBindingRowDbCommand>();
		services.AddTransient<RemoveDataBindingRowDbCommand>();
		services.AddTransient<IWorkspaceMerger, WorkspaceMerger>();
		services.AddTransient<IWorkspacePackageFilter, WorkspacePackageFilter>();
		services.AddTransient<MergeWorkspacesCommand>();
		services.AddTransient<LoadPackagesToFileSystemCommand>();
		services.AddTransient<LoadPackagesToDbCommand>();
		services.AddTransient<UploadLicensesCommand>();
		services.AddTransient<DistributeLicenseCommand>();
		services.AddTransient<HealthCheckCommand>();
		services.AddTransient<ShowLocalEnvironmentsCommand>();
		services.AddTransient<ClearLocalEnvironmentCommand>();
		services.AddTransient<AddPackageCommand>();
		services.AddTransient<UnlockPackageCommand>();
		services.AddTransient<LockPackageCommand>();
		services.AddTransient<DataServiceQuery>();
		services.AddTransient<CallServiceCommand>();
		services.AddTransient<RestoreFromPackageBackupCommand>();
		services.AddTransient<Marketplace>();
		services.AddTransient<CreateUiProjectCommand>();
		services.AddTransient<CreateUiProjectOptionsValidator>();
		services.AddTransient<SetIconParametersValidator>();
		services.AddTransient<DownloadConfigurationCommand>();
		services.AddTransient<DeployCommand>();
		services.AddTransient<InfoCommand>();
		services.AddTransient<QuizCommand>();
		services.AddTransient<ExtractPackageCommand>();
		// ExternalLinkCommand has an internal constructor (its IExternalLinkHandler dependency is
		// internal), so wire it via an explicit composition-root factory rather than container
		// reflection, which only selects public constructors.
		services.AddTransient(sp => new ExternalLinkCommand(
			sp.GetServices<IExternalLinkHandler>(),
			sp.GetRequiredService<IValidator<ExternalLinkOptions>>(),
			sp.GetRequiredService<ILogger>()));
		services.AddTransient<PowerShellFactory>();
		services.AddTransient<IEnvironmentRuntimeDetectionService, EnvironmentRuntimeDetectionService>();
		services.AddTransient<IIisEnvironmentDiscoveryService, IisEnvironmentDiscoveryService>();
		services.AddTransient<RegAppCommand>();
		services.AddTransient<UnregAppCommand>();
		services.AddTransient<RestartCommand>();
		services.AddTransient<StartCommand>();
		services.AddTransient<StopCommand>();
		services.AddTransient<HostsCommand>();
		services.AddTransient<RedisCommand>();
		services.AddTransient<ClearThemesCacheCommand>();
		services.AddTransient<ListThemesCommand>();
		services.AddTransient<IThemeCatalog, ListThemesCommand>();
		services.AddTransient<CreateThemeCommand>();
		services.AddTransient<UpdateThemeCommand>();
		services.AddTransient<DeleteThemeCommand>();
		services.AddTransient<IUserThemeApplier, UserThemeApplier>();
		services.AddTransient<SetUserThemeCommand>();
		services.AddTransient<CheckThemingAccessCommand>();
		services.AddTransient<ICreatioRightsClient, CreatioRightsClient>();
		services.AddTransient<ICreatioLicenseClient, CreatioLicenseClient>();
		services.AddTransient<IFsmModeStatusService, FsmModeStatusService>();
		services.AddTransient<SetFsmConfigCommand>();
		services.AddTransient<TurnFsmCommand>();
		services.AddTransient<TurnFarmModeCommand>();
		services.AddTransient<ScenarioRunnerCommand>();
		services.AddTransient<CompressAppCommand>();
		services.AddTransient<Scenario>();
		services.AddTransient<ConfigureWorkspaceCommand>();
		services.AddTransient<CreateInfrastructureCommand>();
		services.AddTransient<DeployInfrastructureCommand>();
		services.AddTransient<DeleteInfrastructureCommand>();
		services.AddTransient<OpenInfrastructureCommand>();
		services.AddTransient<CheckWindowsFeaturesCommand>();
		services.AddTransient<ManageWindowsFeaturesCommand>();
		services.AddTransient<CreateTestProjectCommand>();
		services.AddTransient<CreateIntegrationTestProjectCommand>();
		services.AddTransient<IValidator<CreateIntegrationTestProjectOptions>, CreateIntegrationTestProjectOptionsValidator>();
		services.AddTransient<ListenCommand>();
		services.AddTransient<ShowPackageFileContentCommand>();
		services.AddTransient<CompilePackageCommand>();
		services.AddTransient<SwitchNugetToDllCommand>();
		services.AddTransient<NugetMaterializer>();
		services.AddTransient<PropsBuilder>();
		services.AddTransient<UninstallAppCommand>();
		services.AddTransient<DownloadAppCommand>();
		services.AddTransient<DeployAppCommand>();
		services.AddTransient<ApplicationManager>();
		services.AddTransient<RestoreDbCommand>();
		services.AddTransient<IDbClientFactory, DbClientFactory>();
		services.AddTransient<IDbConnectionTester, DbConnectionTester>();
		services.AddTransient<IBackupFileDetector, BackupFileDetector>();
		services.AddSingleton<IPostgresToolsPathDetector, PostgresToolsPathDetector>();
		services.AddTransient<SetWebServiceUrlCommand>();
		services.AddTransient<ListInstalledAppsCommand>();
		services.AddTransient<GetCreatioInfoCommand>();
		services.AddTransient<SetApplicationVersionCommand>();
		services.AddTransient<ApplyEnvironmentManifestCommand>();
		services.AddTransient<EnvironmentManager>();
		services.AddTransient<GetWebServiceUrlCommand>();
		services.AddTransient<MockDataCommand>();
		services.AddTransient<AssertCommand>();
		services.AddTransient<ConsoleProgressbar>();
		services.AddTransient<ApplicationLogProvider>();
		services.AddTransient<LastCompilationLogCommand>();
		services.AddTransient<LinkWorkspaceWithTideRepositoryCommand>();
		services.AddTransient<CheckWebFarmNodeConfigurationsCommand>();
		services.AddTransient<GetAppHashCommand>();
		services.AddTransient<ShowAppListCommand>();
		services.AddTransient<EnvManageUiCommand>();
		services.AddTransient<IEnvManageUiService, EnvManageUiService>();
		services.AddTransient<IInstalledApplication, InstalledApplication>();
		services.AddTransient<Link4RepoCommand>();
		services.AddTransient<Link2RepoCommand>();
		services.AddTransient<LinkPackageStoreCommand>();
		services.AddTransient<LinkCoreSrcCommand>();
		services.AddTransient<RfsEnvironment>();

		services.AddTransient<IisScannerHandler>();
		services.AddTransient<IIisScanner, IisScannerHandler>();

		// ExternalLink deep-link handlers.
		services.AddTransient<IExternalLinkHandler, RegisterEnvironmentHandler>();
		services.AddTransient<IExternalLinkHandler, UnregisterEnvironmentHandler>();
		services.AddTransient<IExternalLinkHandler, RestartHandler>();
		services.AddTransient<IExternalLinkHandler, OpenUrlHandler>();
		services.AddTransient<IExternalLinkHandler, GetAppSettingsFilePathHandler>();
		services.AddTransient<IExternalLinkHandler, RegisterOAuthCredentialsHandler>();
		services.AddTransient<IExternalLinkHandler, IisScannerHandler>();

		services.AddTransient<ExternalLinkOptionsValidator>();
		services.AddTransient<SetFsmConfigOptionsValidator>();
		services.AddTransient<TurnFarmModeOptionsValidator>();
		services.AddTransient<UninstallCreatioCommandOptionsValidator>();
		services.AddTransient<Link4RepoOptionsValidator>();
		services.AddTransient<LinkPackageStoreOptionsValidator>();
		services.AddTransient<DownloadConfigurationCommandOptionsValidator>();
		services.AddTransient<AddItemOptionsValidator>();
		services.AddTransient<ICreatioUninstaller, CreatioUninstaller>();
		services.AddSingleton<IWindowsUserProfileApi, WindowsUserProfileApi>();
		services.AddSingleton<IProfileDeletionRetryDelay, ProfileDeletionRetryDelay>();
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			services.AddTransient<IAppPoolProfileCleaner, WindowsAppPoolProfileCleaner>();
		}
		else {
			services.AddTransient<IAppPoolProfileCleaner, NonWindowsAppPoolProfileCleaner>();
		}
		services.AddTransient<ICreateIISSiteHandler, CreateIISSiteRequestHandler>();
		RegisterIisHttpsServices(services);
		services.AddTransient<IConfigureConnectionStringHandler, ConfigureConnectionStringRequestHandler>();
		services.AddTransient<IUpdateIISSitePhysicalPathHandler, UpdateIISSitePhysicalPathRequestHandler>();
		services.AddTransient<GitSyncCommand>();

		services.AddTransient<DeactivatePackageCommand>();
		services.AddTransient<PublishWorkspaceCommand>();
		services.AddTransient<ActivatePackageCommand>();
		services.AddTransient<PackageHotFixCommand>();
		services.AddTransient<PackageEditableMutator>();
		services.AddTransient<AddPackageDependencyCommand>();
		services.AddTransient<RemovePackageDependencyCommand>();
		services.AddTransient<PackageDependencyManager>();
		services.AddTransient<SaveSettingsToManifestCommand>();
		services.AddTransient<ShowDiffEnvironmentsCommand>();
		services.AddTransient<CloneEnvironmentCommand>();
		services.AddTransient<PullPkgCommand>();
		services.AddTransient<AssemblyCommand>();
		services.AddTransient<UninstallCreatioCommand>();
		services.AddTransient<InstallDbHubCommand>();
		services.AddTransient<SyncDbHubCommand>();
		services.AddTransient<AddSchemaCommand>();
		services.AddTransient<CreateEntitySchemaCommand>();
		services.AddTransient<UpdateEntitySchemaCommand>();
		services.AddTransient<ModifyEntitySchemaColumnCommand>();
		services.AddTransient<GetEntitySchemaColumnPropertiesCommand>();
		services.AddTransient<GetEntitySchemaPropertiesCommand>();
		services.AddTransient<SetEntitySchemaPropertiesCommand>();
		services.AddTransient<FindEntitySchemaCommand>();
		services.AddTransient<FindAppCommand>();
		services.AddTransient<CreateUserTaskCommand>();
		services.AddTransient<ModifyUserTaskParametersCommand>();
		services.AddTransient<DeleteSchemaCommand>();
		services.AddTransient<CreatioInstallerService>();
		services.AddTransient<SetApplicationIconCommand>();
		services.AddTransient<CustomizeDataProtectionCommand>();
		services.AddTransient<GenerateProcessModelCommand>();
		services.AddTransient<DescribeProcessCommand>();
		services.AddTransient<GetProcessSignatureCommand>();
		services.AddTransient<ListPrintablesCommand>();
		services.AddTransient<AddItemCommand>();
		services.AddTransient<IZipFile, ZipFileWrapper>();
		services.AddTransient<IProcessModelGenerator, ProcessModelGenerator>();
		services.AddTransient<IProcessModelWriter, ProcessModelWriter>();
		services.AddTransient<IZipBasedApplicationDownloader, ZipBasedApplicationDownloader>();
		services.AddTransient<ICreatioHostService, CreatioHostService>();
		services.AddTransient<IISDeploymentStrategy>();
		services.AddTransient<DotNetDeploymentStrategy>();
		services.AddTransient<DeploymentStrategyFactory>();
		services.AddTransient<OpenAppCommand>();
		services.AddTransient<GetBrowserSessionCommand>();
		services.AddTransient<ClearBrowserSessionCommand>();
		services.AddSingleton<ISystemServiceManager>(sp => {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return new LinuxSystemServiceManager(sp.GetRequiredService<System.IO.Abstractions.IFileSystem>());
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return new MacOSSystemServiceManager(sp.GetRequiredService<IProcessExecutor>());
			return new WindowsSystemServiceManager();
		});
		services.AddSingleton<Common.IIS.IIISSiteDetector>(sp =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISSiteDetector(sp.GetRequiredService<IProcessExecutor>())
				: new Common.IIS.StubIISSiteDetector());
		services.AddSingleton<Common.IIS.IPlatformDetector, Common.IIS.PlatformDetector>();
		services.AddSingleton<Common.IIS.ITcpPortReservationReader, Common.IIS.TcpPortReservationReader>();
		services.AddTransient<Common.IIS.IAvailableIisPortService, Common.IIS.AvailableIisPortService>();
		services.AddSingleton<Common.IIS.IIISAppPoolManager>(sp =>
			RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new Common.IIS.WindowsIISAppPoolManager(sp.GetRequiredService<IProcessExecutor>())
				: new Common.IIS.StubIISAppPoolManager());
		services.AddTransient<ClioGateway>();
		services.AddTransient<IRequiredPackageChecker, RequiredPackageChecker>();
		services.AddTransient<CompileConfigurationCommand>();
		services.AddTransient<CompileWorkspaceCommand>();
		services.AddTransient<GenerateSourceCodeCommand>();
		services.AddTransient<GetIdentityAssertionCommand>();
		services.AddTransient<GetIdentityPublicJwkCommand>();
		services.AddTransient<RegenerateIdentitySigningKeyCommand>();
		services.AddTransient<CheckAuthCodeFlowCommand>();
		services.AddTransient<RegisterSsoProviderCommand>();
		services.AddTransient<IMssql, Mssql>();
		services.AddTransient<IPostgres, Postgres>();
		services.AddSingleton<CommandHelpCatalog>();
		services.AddTransient<CommandHelpRenderer>();
		// HelpArtifactExporter is constructed directly in Program.ExportHelpArtifacts with a
		// deterministic export-baseline IFeatureToggleService (see ExportFeatureToggleService) so
		// committed docs never depend on local feature flags. It is therefore not DI-resolved.
		services.AddTransient<LocalHelpViewer>();
		services.AddTransient<WikiHelpViewer>();
		
		services.AddTransient<Func<EnvironmentSettings, ISysSettingsManager>>(_ =>
			envSettings => new SysSettingsManager(BuildRemoteDataProvider(envSettings)));

		RegisterFluentValidators(services);
		return settingsRepository;
	}

	private static void RegisterIisHttpsServices(IServiceCollection services) {
		services.AddTransient<INetFrameworkHttpsConfigurator, NetFrameworkHttpsConfigurator>();
		services.AddTransient<IIisCertificateResolver, IisCertificateResolver>();
		services.AddTransient<ICertificateSelectionPrompt, ConsoleCertificateSelectionPrompt>();
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			services.AddTransient<IIisCertificateProvider, WindowsIisCertificateProvider>();
			services.AddTransient<IIisCertificateBindingService, WindowsIisCertificateBindingService>();
		}
		else {
			services.AddTransient<IIisCertificateProvider, NonWindowsIisCertificateProvider>();
			services.AddTransient<IIisCertificateBindingService, NonWindowsIisCertificateBindingService>();
		}
	}

	// Fallback base address for the no-credential local bootstrap case only (never a multi-tenant
	// target): used when the environment supplies no explicit Uri.
	// Composed from Uri.UriSchemeHttp rather than a literal so this loopback bootstrap fallback is not
	// a hardcoded absolute URI (Sonar S1075). It is only ever used when the environment supplies no Uri.
	private static readonly string DefaultLocalhostUri = $"{Uri.UriSchemeHttp}://localhost";

	// Builds an ATF RemoteDataProvider for the environment. Bearer-first: an AccessToken is
	// consumed via the dedicated bearer ctor and must never reach the login/password path
	// (multi-tenant safety, ENG-93208 B1). Login/password are passed as-is (no Supervisor default).
	private static RemoteDataProvider BuildRemoteDataProvider(EnvironmentSettings settings) {
		if (!string.IsNullOrEmpty(settings.AccessToken)) {
			return new RemoteDataProvider(settings.Uri, settings.AccessToken, settings.IsNetCore);
		}
		if (string.IsNullOrEmpty(settings.ClientId)) {
			return new RemoteDataProvider(settings.Uri, settings.Login, settings.Password, settings.IsNetCore);
		}
		return new RemoteDataProvider(settings.Uri, settings.AuthAppUri, settings.ClientId,
			settings.ClientSecret, settings.IsNetCore);
	}

	// Builds a CreatioClient for the environment. Bearer-first: an AccessToken is consumed via the
	// bearer ctor and must never reach the "Supervisor" fallback (multi-tenant safety, ENG-93208 B1).
	// The Supervisor/localhost default stays reachable ONLY for the no-credential bootstrap case.
	private static CreatioClient BuildCreatioClient(EnvironmentSettings settings) {
		if (!string.IsNullOrEmpty(settings.AccessToken)) {
			return new CreatioClient(settings.Uri ?? DefaultLocalhostUri, settings.AccessToken, settings.IsNetCore);
		}
		if (string.IsNullOrEmpty(settings.ClientId)) {
			return new CreatioClient(settings.Uri ?? DefaultLocalhostUri, settings.Login ?? "Supervisor",
				settings.Password ?? "Supervisor", true, settings.IsNetCore);
		}
		return CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri,
			settings.ClientId, settings.ClientSecret, settings.IsNetCore);
	}

	private static void RegisterActiveEnvironmentServices(
		IServiceCollection services, EnvironmentSettings activeSettings) {
		services.AddSingleton(activeSettings);
		services.AddTransient<IDataProvider>(_ => new LazyDataProvider(() => BuildRemoteDataProvider(activeSettings)));
		// Bearer-first; AccessToken must never reach the "Supervisor" fallback below
		// (multi-tenant safety, ENG-93208 B1).
		Lazy<CreatioClient> lazyCreatioClient = new(() => BuildCreatioClient(activeSettings));
		services.AddSingleton<CreatioClient>(_ => lazyCreatioClient.Value);
		services.AddSingleton<IApplicationClient>(sp =>
			// Bearer path must never re-login: wire NoReauthExecutor (the DI'd IReauthExecutor)
			// so an ephemeral bearer client cannot fall back to a login/password re-auth
			// (multi-tenant safety, ENG-93208 B1). Non-bearer keeps the adapter's default
			// internal closure-based ReauthExecutor byte-for-byte.
			!string.IsNullOrEmpty(activeSettings.AccessToken)
				? new CreatioClientAdapter(lazyCreatioClient, sp.GetRequiredService<IReauthExecutor>())
				: new CreatioClientAdapter(lazyCreatioClient));
		services.AddTransient<SysSettingsManager>();
	}

	private static IKubernetes CreateKubernetesClient() {
		try {
			KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
			Uri.TryCreate(config.Host, UriKind.Absolute, out Uri uriResult);
			if (uriResult is null || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)) {
				throw new InvalidOperationException("Invalid Kubernetes configuration host.");
			}
			k8sDns = uriResult.Host;
			return new Kubernetes(config);
		}
		catch {
			return new FakeKubernetes();
		}
	}

	/// <summary>
	/// Registers the MCP server host (options, request filters, feature-gated tool/resource/prompt
	/// types) into <paramref name="services"/> and returns the <see cref="IMcpServerBuilder"/> so
	/// the caller can chain the transport (<c>.WithStdioServerTransport()</c> or
	/// <c>.WithHttpTransport()</c>).
	/// </summary>
	internal static IMcpServerBuilder RegisterMcpServer(
		IServiceCollection services,
		ISettingsRepository settingsRepository) {
		JsonSerializerOptions mcpSerializerOptions = CreateMcpSerializerOptions();
		Assembly mcpAssembly = Assembly.GetExecutingAssembly();
		IFeatureToggleService mcpFeatureToggleService = new FeatureToggleService(settingsRepository);
		IMcpServerBuilder mcpServerBuilder = services.AddMcpServer(options => {
					options.Capabilities ??= new();
					options.Capabilities.Logging = new();
					options.ServerInstructions = McpServerInstructions.Text;
				})
				.WithRequestFilters(filters => filters.AddCallToolFilter(McpToolErrorFilter.HandleCallToolErrors));
		McpFeatureToggleFilter.RegisterEnabledPrimitives(
			mcpServerBuilder, mcpAssembly, mcpFeatureToggleService.IsEnabled, mcpSerializerOptions);
		return mcpServerBuilder;
	}

	private static EnvironmentSettings ResolveActiveSettings(
		EnvironmentSettings settings,
		BindingsModuleRegistrationProfile profile,
		SettingsBootstrapResult bootstrapResult) {
		if (settings is not null) {
			return settings;
		}
		if (profile == BindingsModuleRegistrationProfile.EnvironmentScoped) {
			return bootstrapResult.ResolvedEnvironment ?? CreateBootstrapPlaceholderEnvironment();
		}
		return CreateBootstrapPlaceholderEnvironment();
	}

	/// <summary>
	/// Creates <see cref="JsonSerializerOptions"/> for MCP tool/prompt argument deserialization.
	/// Enables out-of-order metadata properties so that the
	/// <c>"type"</c> polymorphic discriminator does not have to be the first JSON property —
	/// LLMs do not guarantee JSON property ordering.
	/// </summary>
	internal static JsonSerializerOptions CreateMcpSerializerOptions() {
		JsonSerializerOptions options = new(McpJsonUtilities.DefaultOptions);
		options.AllowOutOfOrderMetadataProperties = true;
		return options;
	}

	private static EnvironmentSettings CreateBootstrapPlaceholderEnvironment() {
		return new EnvironmentSettings {
			Uri = CreateBootstrapPlaceholderUri(),
			Login = string.Empty,
			Password = string.Empty
		};
	}

	private static string CreateBootstrapPlaceholderUri() {
		return new UriBuilder(Uri.UriSchemeHttp, "localhost")
			.Uri
			.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
	}

	private static void LogBootstrapDiagnostics(
		BindingsModuleRegistrationProfile profile,
		SettingsBootstrapReport report) {
		if (profile != BindingsModuleRegistrationProfile.Bootstrap) {
			return;
		}
		lock (BootstrapDiagnosticsSyncRoot) {
			if (_bootstrapDiagnosticsLogged) {
				return;
			}
			if (report.RepairsApplied.Count > 0) {
				string repairs = string.Join("; ", report.RepairsApplied.Select(repair => repair.Message));
				ConsoleLogger.Instance.WriteWarning(
					$"clio settings bootstrap repaired {repairs}. Active environment: {report.ResolvedActiveEnvironmentKey ?? "<none>"}.");
				_bootstrapDiagnosticsLogged = true;
				return;
			}
			if (string.Equals(report.Status, "broken", StringComparison.OrdinalIgnoreCase)) {
				string issue = report.Issues.FirstOrDefault()?.Message
					?? "appsettings.json is unreadable.";
				ConsoleLogger.Instance.WriteWarning(
					$"clio settings bootstrap is degraded. {issue} File path: {report.SettingsFilePath}");
				_bootstrapDiagnosticsLogged = true;
			}
		}
	}
	
	
	private static void RegisterAssemblyInterfaceTypes(IServiceCollection services){
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types) {
			if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition || type == typeof(ConsoleLogger)) {
				continue;
			}
			foreach (Type implementedInterface in type.GetInterfaces()) {
				if (implementedInterface.Namespace is null
					|| !implementedInterface.Namespace.StartsWith("Clio", StringComparison.Ordinal)
					|| !implementedInterface.Name.StartsWith("I", StringComparison.Ordinal)
					|| implementedInterface == typeof(IDbOperationLogSession)
					|| implementedInterface == typeof(IMessageChannelHubConnection)
					// ReauthExecutor requires a per-adapter Login closure; it is created by
					// CreatioClientAdapter rather than resolved from DI.
					|| implementedInterface == typeof(IReauthExecutor)
					// CliogateHttpReadinessProbe takes runtime-only ctor args (an HttpClient, the
					// attempt budget, and inter-attempt delays); it is constructed by the e2e
					// readiness wait, not resolved from DI.
					|| implementedInterface == typeof(ICliogateHttpReadinessProbe)
					// The MCP credential-passthrough seam is registered explicitly in the
					// mcp-http host only (McpHttpServerCommand.Run): the accessor depends on
					// IHttpContextAccessor, which is not part of the stdio graph, and both are
					// scoped to the HTTP transport. Auto-registering them here would fail
					// ValidateOnBuild in the stdio/tool graph.
					|| implementedInterface == typeof(ICredentialContextAccessor)
					|| implementedInterface == typeof(ICredentialHeaderParser)
					// The edge API-key gate is registered as an instance in the mcp-http host
					// (McpHttpServerCommand.Run) because its key set is resolved at Run time from
					// the CLI flag + env var. Auto-registering the type here has no key set and
					// pollutes the stdio graph.
					|| implementedInterface == typeof(IPlatformApiKeyGate)
					// The SSRF / egress target-url validator is registered as an instance in the
					// mcp-http host (McpHttpServerCommand.Run) because its policy (bound host +
					// --allowed-base-urls allowlist) is resolved at Run time. Auto-registering the
					// type here has no policy and pollutes the stdio graph.
					|| implementedInterface == typeof(ITargetUrlValidator)
					// The session-container cache is registered explicitly (a DEFAULT singleton in the
					// shared build, a run-time-configured instance in the mcp-http host). Its impl ctor
					// takes primitive TimeSpan/int args that DI cannot resolve, so auto-registering the
					// type here would fail ValidateOnBuild.
					|| implementedInterface == typeof(ISessionContainerCache)
					// The per-tenant execution lock provider (FR-05) is registered explicitly as the
					// process-wide shared instance. Its impl ctor is private (locks must be shared across
					// every container the host builds), so auto-registering the type would fail
					// ValidateOnBuild.
					|| implementedInterface == typeof(ITenantExecutionLockProvider)) {
					continue;
				}
				services.AddTransient(implementedInterface, type);
			}
		}
	}

	private static void RegisterFluentValidators(IServiceCollection services){
		Type validatorInterfaceType = typeof(IValidator<>);
		Type[] types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (Type type in types) {
			if (!type.IsClass || type.IsAbstract) {
				continue;
			}
			Type[] validatorInterfaces = type.GetInterfaces();
			foreach (Type validatorInterface in validatorInterfaces) {
				if (!validatorInterface.IsGenericType
					|| validatorInterface.GetGenericTypeDefinition() != validatorInterfaceType) {
					continue;
				}
				services.AddTransient(validatorInterface, type);
			}
		}
	}

	#endregion

	private sealed class LazyDataProvider : IDataProvider {
		private readonly Lazy<IDataProvider> _lazy;
		internal LazyDataProvider(Func<IDataProvider> factory) => _lazy = new(factory);
		public IDefaultValuesResponse GetDefaultValues(string entitySchemaName) => _lazy.Value.GetDefaultValues(entitySchemaName);
		public IItemsResponse GetItems(ISelectQuery selectQuery) => _lazy.Value.GetItems(selectQuery);
		public IExecuteResponse BatchExecute(List<IBaseQuery> queries) => _lazy.Value.BatchExecute(queries);
		public T GetSysSettingValue<T>(string sysSettingCode) => _lazy.Value.GetSysSettingValue<T>(sysSettingCode);
		public bool GetFeatureEnabled(string featureCode) => _lazy.Value.GetFeatureEnabled(featureCode);
		public IExecuteProcessResponse ExecuteProcess(IExecuteProcessRequest request) => _lazy.Value.ExecuteProcess(request);
	}

}
#pragma warning restore CLIO001 // Non-nullable field is uninitialized.
