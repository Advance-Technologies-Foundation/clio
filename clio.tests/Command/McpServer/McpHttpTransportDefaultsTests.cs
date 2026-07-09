using Clio.Command.McpServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Story 15e (ENG-93208, FR-16 / ADR RISK #1) transport-default pin. The credential-passthrough edge
/// assumes tool handlers run on the request's <see cref="System.Threading.ExecutionContext"/> so that
/// the per-request credential context set by the capture middleware flows into the handler. That holds
/// only while <see cref="HttpServerTransportOptions.PerSessionExecutionContext"/> is
/// <see langword="false"/>. These tests resolve the options the SDK actually configures through
/// <see cref="McpHttpServerCommand.ConfigureHttpTransport"/> — the SAME lambda production wiring passes
/// to <c>WithHttpTransport</c> — and assert the pinned values, so a future SDK default flip cannot
/// silently drift the assumption.
/// </summary>
[TestFixture]
[Category("Integration")]
[Property("Module", "McpServer")]
public sealed class McpHttpTransportDefaultsTests {

	[Test]
	[Description("The MCP HTTP transport options resolved from DI honor the pinned defaults (EnableLegacySse=false, PerSessionExecutionContext=false, Stateless=false) that the credential-passthrough edge depends on (ADR RISK #1).")]
	public void WithHttpTransport_ShouldPinTransportDefaults_WhenConfiguredByHost() {
		// Arrange — register the MCP server + HTTP transport exactly as McpHttpServerCommand.Run does,
		// applying the production ConfigureHttpTransport lambda, then build the provider.
		ServiceCollection services = new();
		services.AddMcpServer().WithHttpTransport(McpHttpServerCommand.ConfigureHttpTransport);
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		HttpServerTransportOptions options = provider
			.GetRequiredService<IOptions<HttpServerTransportOptions>>()
			.Value;

		// Assert
		// MCP9004: reading the [Obsolete] EnableLegacySse is intentional here — the test exists to pin that
		// legacy SSE stays DISABLED (false). Suppression is scoped to the assertion read.
#pragma warning disable MCP9004
		options.EnableLegacySse.Should().BeFalse(
			because: "only the modern Streamable HTTP endpoint is exposed; the legacy SSE endpoints must stay unmapped (Story 15e)");
#pragma warning restore MCP9004
		options.PerSessionExecutionContext.Should().BeFalse(
			because: "tool handlers must run on the REQUEST's ExecutionContext so the per-request credential context flows into the handler — RISK #1 must not silently drift (Story 15e)");
		options.Stateless.Should().BeFalse(
			because: "the server must track per-session state so the per-session container cache keys off it (Story 15e)");
	}

	[Test]
	[Description("The shared ConfigureHttpTransport lambda applied directly sets the pinned transport values, guaranteeing the production wiring and the assertion cannot diverge (Story 15e).")]
	public void ConfigureHttpTransport_ShouldSetPinnedValues_WhenAppliedToFreshOptions() {
		// Arrange
		HttpServerTransportOptions options = new();

		// Act
		McpHttpServerCommand.ConfigureHttpTransport(options);

		// Assert
		// MCP9004: reading the [Obsolete] EnableLegacySse is intentional here — the test exists to pin that
		// legacy SSE stays DISABLED (false). Suppression is scoped to the assertion read.
#pragma warning disable MCP9004
		options.EnableLegacySse.Should().BeFalse(
			because: "the pin disables the legacy SSE endpoints (Story 15e)");
#pragma warning restore MCP9004
		options.PerSessionExecutionContext.Should().BeFalse(
			because: "the pin keeps handlers on the request ExecutionContext for credential passthrough (Story 15e)");
		options.Stateless.Should().BeFalse(
			because: "the pin keeps the server session-stateful (Story 15e)");
	}
}
