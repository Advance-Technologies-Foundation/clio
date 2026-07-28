using System;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 12 (FR-14, AC-03) DI-completeness gate for the credential-passthrough seam: proves that
/// EVERY new passthrough service resolves without error under the mcp-http host graph built with
/// <see cref="ServiceProviderOptions.ValidateOnBuild"/> + <see cref="ServiceProviderOptions.ValidateScopes"/>,
/// and that the shared (stdio) graph still builds and resolves its always-registered members. This
/// is the wiring/CLIO005 acceptance gate; the null-object-vs-real last-registration-wins contract
/// for the accessor/validator is locked separately by
/// <see cref="CredentialPassthroughDiRegistrationTests"/>.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class CredentialPassthroughDiTests {

	// Mirrors McpHttpServerCommand.Run: the shared BindingsModule build, then the HTTP-host-scoped
	// passthrough registrations (AFTER the shared build, so last-registration-wins gives the real
	// accessor/validator/cache in HTTP). Parser and api-key gate are registered here only.
	private static IServiceCollection BuildHttpHostServices(IFileSystem fileSystem) {
		IServiceCollection services = new ServiceCollection();
		new BindingsModule(fileSystem).RegisterInto(services);
		services.AddHttpContextAccessor();
		services.AddSingleton<ICredentialHeaderParser, CredentialHeaderParser>();
		services.AddSingleton<ICredentialContextAccessor, CredentialContextAccessor>();
		services.AddSingleton<IPlatformApiKeyGate>(new PlatformApiKeyGate([]));
		services.AddSingleton<ITargetUrlValidator>(new TargetUrlValidator("127.0.0.1", []));
		services.AddSingleton<ISessionContainerCache>(
			new SessionContainerCache(TimeSpan.FromMinutes(5), 50));
		return services;
	}

	[Test]
	[Description("Every new credential-passthrough service resolves in the mcp-http host graph built with ValidateOnBuild+ValidateScopes (AC-03).")]
	[Category("Unit")]
	public void HttpHostGraph_Should_ResolveAllPassthroughServices_When_BuiltWithValidateOnBuild() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		IServiceCollection services = BuildHttpHostServices(fileSystem);

		try {
			// Act
			ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
				ValidateOnBuild = true,
				ValidateScopes = true
			});

			// Assert
			provider.GetRequiredService<IHttpContextAccessor>().Should().NotBeNull(
				because: "the HTTP host provides IHttpContextAccessor, on which the real accessor depends");
			provider.GetRequiredService<ICredentialHeaderParser>().Should()
				.BeOfType<CredentialHeaderParser>(
					because: "the mcp-http host registers the credential header parser (HTTP-only, skip-listed)");
			provider.GetRequiredService<ICredentialContextAccessor>().Should()
				.BeOfType<CredentialContextAccessor>(
					because: "the mcp-http host registers the real accessor after the shared build");
			provider.GetRequiredService<IPlatformApiKeyGate>().Should()
				.BeOfType<PlatformApiKeyGate>(
					because: "the mcp-http host registers the edge API-key gate as an instance (HTTP-only, skip-listed)");
			provider.GetRequiredService<ITargetUrlValidator>().Should()
				.BeOfType<TargetUrlValidator>(
					because: "the mcp-http host registers the real SSRF validator after the shared build");
			provider.GetRequiredService<ISessionContainerCache>().Should()
				.BeOfType<SessionContainerCache>(
					because: "the mcp-http host registers a run-time-configured session cache after the shared build");
			provider.GetRequiredService<ITenantExecutionLockProvider>().Should().NotBeNull(
				because: "the per-tenant execution lock provider is registered as the process-wide shared instance");
			provider.GetRequiredService<IReauthExecutor>().Should().BeOfType<NoReauthExecutor>(
				because: "NoReauthExecutor is the DI-resolved IReauthExecutor used by the passthrough bearer client");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("The shared (stdio) graph still builds under ValidateOnBuild+ValidateScopes and resolves its always-registered passthrough members via the null-object seams (AC-03).")]
	[Category("Unit")]
	public void SharedStdioGraph_Should_BuildAndResolveDefaults_When_HttpHostRegistrationsAbsent() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		IServiceCollection services = new ServiceCollection();
		new BindingsModule(fileSystem).RegisterInto(services);

		try {
			// Act
			ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
				ValidateOnBuild = true,
				ValidateScopes = true
			});

			// Assert
			provider.GetRequiredService<IReauthExecutor>().Should().BeOfType<NoReauthExecutor>(
				because: "NoReauthExecutor is registered in the shared build and must resolve in the stdio graph");
			provider.GetRequiredService<ISessionContainerCache>().Should()
				.BeOfType<SessionContainerCache>(
					because: "the shared build registers the DEFAULT session cache for the stdio/ephemeral graph");
			provider.GetRequiredService<ITenantExecutionLockProvider>().Should().NotBeNull(
				because: "the process-wide shared execution lock provider is registered in the shared build");
			provider.GetRequiredService<ICredentialContextAccessor>().Should().NotBeNull(
				because: "the shared build registers a null-object accessor so the resolver's ctor deps are satisfiable in stdio");
			provider.GetRequiredService<ITargetUrlValidator>().Should().NotBeNull(
				because: "the shared build registers a null-object validator so the resolver's ctor deps are satisfiable in stdio");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}
}
