using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Clio;
using Clio.Command.McpServer;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Locks the keystone cross-host DI contract for the credential-passthrough seam: the shared
/// <c>BindingsModule.RegisterInto</c> registers null-object defaults, and the mcp-http host
/// registers the REAL accessor/validator AFTER the shared build, so last-registration-wins
/// resolves the real ones in HTTP and the null objects in the stdio graph. A future reorder that
/// silently breaks this must fail here.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public class CredentialPassthroughDiRegistrationTests {

	[Test]
	[Description("Resolves the null-object accessor and validator in the shared (stdio) DI graph, and the graph validates on build.")]
	[Category("Unit")]
	public void SharedGraph_Should_Resolve_NullObjects_When_HttpHost_Registrations_Absent() {
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
			provider.GetRequiredService<ICredentialContextAccessor>().Should()
				.BeOfType<NullCredentialContextAccessor>(
					"because the stdio graph has no HTTP-host registration, so the shared null-object default must win");
			provider.GetRequiredService<ITargetUrlValidator>().Should()
				.BeOfType<NullTargetUrlValidator>(
					"because the stdio graph has no HTTP-host registration, so the shared null-object default must win");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}

	[Test]
	[Description("Resolves the real accessor and validator in the mcp-http DI graph, where they are registered after the shared build.")]
	[Category("Unit")]
	public void HttpGraph_Should_Resolve_RealImplementations_When_Registered_After_Shared_Build() {
		// Arrange
		IFileSystem originalFileSystem = SettingsRepository.FileSystem;
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsRepository.FileSystem = fileSystem;
		IServiceCollection services = new ServiceCollection();
		// Shared build first (registers the null-object defaults)...
		new BindingsModule(fileSystem).RegisterInto(services);
		// ...then the mcp-http host registrations, mirroring McpHttpServerCommand.Run ordering.
		services.AddHttpContextAccessor();
		services.AddSingleton<ICredentialContextAccessor, CredentialContextAccessor>();
		services.AddSingleton<ITargetUrlValidator>(new TargetUrlValidator("localhost", []));

		try {
			// Act
			ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions {
				ValidateOnBuild = true,
				ValidateScopes = true
			});

			// Assert
			provider.GetRequiredService<ICredentialContextAccessor>().Should()
				.BeOfType<CredentialContextAccessor>(
					"because the mcp-http host registers the real accessor after the shared build, so last-registration-wins gives the real one in HTTP");
			provider.GetRequiredService<ITargetUrlValidator>().Should()
				.BeOfType<TargetUrlValidator>(
					"because the mcp-http host registers the real validator after the shared build, so last-registration-wins gives the real one in HTTP");
			provider.GetRequiredService<IHttpContextAccessor>().Should().NotBeNull(
				"because the real accessor depends on IHttpContextAccessor, which the HTTP graph must also provide");
		}
		finally {
			SettingsRepository.FileSystem = originalFileSystem;
		}
	}
}
