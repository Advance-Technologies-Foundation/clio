using System;
using Clio.Common.Telemetry;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class TelemetryFlushOptionsProviderTests
{
	[Test]
	[Category("Unit")]
	[Description("Prefers the CLIO_TELEMETRY_ENDPOINT and CLIO_TELEMETRY_INGEST_KEY environment variables over the settings file.")]
	public void Resolve_Should_Prefer_Environment_Variables_Over_Settings()
	{
		// Arrange
		using EnvironmentVariableScope endpointScope = new(
			TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, "https://env.example.com/v1/logs");
		using EnvironmentVariableScope keyScope = new(
			TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, "env-key");
		TelemetryFlushOptionsProvider provider = CreateProvider(
			new TelemetrySettings { Endpoint = "https://settings.example.com/v1/logs", IngestKey = "settings-key" });

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.Endpoint.Should().Be("https://env.example.com/v1/logs",
			because: "environment variables must win over the settings file for ops overrides");
		options.IngestKey.Should().Be("env-key",
			because: "the ingest key follows the same precedence as the endpoint");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to the settings file when no environment variables are set.")]
	public void Resolve_Should_Fall_Back_To_Settings_When_Environment_Not_Set()
	{
		// Arrange
		using EnvironmentVariableScope endpointScope = new(
			TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, null);
		using EnvironmentVariableScope keyScope = new(
			TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, null);
		TelemetryFlushOptionsProvider provider = CreateProvider(
			new TelemetrySettings { Endpoint = "https://settings.example.com/v1/logs", IngestKey = "settings-key" });

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeTrue(
			because: "an endpoint configured in the settings file enables uploading");
		options.Endpoint.Should().Be("https://settings.example.com/v1/logs",
			because: "the settings file is the regular configuration source");
		options.IngestKey.Should().Be("settings-key",
			because: "the ingest key is read from the same settings section");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats an invalid or non-http(s) endpoint as not configured so uploading stays disabled.")]
	public void Resolve_Should_Disable_Sending_When_Endpoint_Invalid()
	{
		// Arrange
		using EnvironmentVariableScope endpointScope = new(
			TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, null);
		using EnvironmentVariableScope keyScope = new(
			TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, null);
		TelemetryFlushOptionsProvider relativeUrl = CreateProvider(new TelemetrySettings { Endpoint = "not-a-url" });
		TelemetryFlushOptionsProvider wrongScheme = CreateProvider(
			new TelemetrySettings { Endpoint = "ftp://collector.example.com/v1/logs" });

		// Act / Assert
		relativeUrl.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "a malformed endpoint must disable uploading instead of failing at POST time");
		wrongScheme.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "only http(s) endpoints are valid OTLP/HTTP targets");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports uploading disabled when neither environment variables nor settings configure an endpoint.")]
	public void Resolve_Should_Disable_Sending_When_Nothing_Configured()
	{
		// Arrange
		using EnvironmentVariableScope endpointScope = new(
			TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, null);
		using EnvironmentVariableScope keyScope = new(
			TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, null);
		TelemetryFlushOptionsProvider provider = CreateProvider(new TelemetrySettings());

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeFalse(
			because: "telemetry uploading is disabled by default until an endpoint is explicitly configured");
	}

	[Test]
	[Category("Unit")]
	[Description("Requires https for remote endpoints but allows loopback http for local-collector testing.")]
	public void Resolve_Should_Require_Https_For_Remote_But_Allow_Loopback_Http()
	{
		// Arrange
		using EnvironmentVariableScope endpointScope = new(
			TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, null);
		using EnvironmentVariableScope keyScope = new(
			TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, null);
		TelemetryFlushOptionsProvider remoteHttp = CreateProvider(
			new TelemetrySettings { Endpoint = "http://collector.example.com/v1/logs" });
		TelemetryFlushOptionsProvider loopbackHttp = CreateProvider(
			new TelemetrySettings { Endpoint = "http://127.0.0.1:30080/v1/logs" });
		TelemetryFlushOptionsProvider remoteHttps = CreateProvider(
			new TelemetrySettings { Endpoint = "https://collector.example.com/v1/logs" });

		// Act / Assert
		remoteHttp.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "the ingest key and event payload must never traverse the network in cleartext to a remote host");
		loopbackHttp.Resolve().IsSendingEnabled.Should().BeTrue(
			because: "plaintext http is acceptable only for a loopback collector during local testing");
		remoteHttps.Resolve().IsSendingEnabled.Should().BeTrue(
			because: "https is the required transport for a remote OTLP collector");
	}

	private static TelemetryFlushOptionsProvider CreateProvider(TelemetrySettings settings)
	{
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		repository.GetTelemetrySettings().Returns(settings);
		return new TelemetryFlushOptionsProvider(repository);
	}

	private sealed class EnvironmentVariableScope : IDisposable
	{
		private readonly string _name;
		private readonly string _previous;

		public EnvironmentVariableScope(string name, string value)
		{
			_name = name;
			_previous = Environment.GetEnvironmentVariable(name);
			Environment.SetEnvironmentVariable(name, value);
		}

		public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
	}
}
