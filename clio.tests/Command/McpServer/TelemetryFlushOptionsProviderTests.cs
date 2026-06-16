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
	private string _previousEndpoint;
	private string _previousIngestKey;
	private string _previousEnabled;

	[SetUp]
	public void SetUp()
	{
		// Make every test hermetic against ambient CLIO_TELEMETRY_* configuration: capture and clear
		// the three environment variables so a developer's or CI runner's real telemetry settings
		// cannot leak into resolution. Each test then sets only the variables it exercises.
		_previousEndpoint = Environment.GetEnvironmentVariable(TelemetryFlushOptionsProvider.EndpointEnvironmentVariable);
		_previousIngestKey = Environment.GetEnvironmentVariable(TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable);
		_previousEnabled = Environment.GetEnvironmentVariable(TelemetryFlushOptionsProvider.EnabledEnvironmentVariable);
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, null);
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, null);
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.EnabledEnvironmentVariable, null);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.EndpointEnvironmentVariable, _previousEndpoint);
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.IngestKeyEnvironmentVariable, _previousIngestKey);
		Environment.SetEnvironmentVariable(TelemetryFlushOptionsProvider.EnabledEnvironmentVariable, _previousEnabled);
	}

	[Test]
	[Category("Unit")]
	[Description("Pins the shipped default endpoint to the production HTTPS collector so an accidental edit is caught.")]
	public void DefaultEndpoint_Should_Be_The_Production_Https_Collector()
	{
		// Assert
		TelemetryFlushOptionsProvider.DefaultEndpoint.Should().Be("https://caadt-telemetry.creatio.com/v1/logs",
			because: "the production OTLP/HTTP collector is the default that installed clients point at");
		TelemetryFlushOptionsProvider.DefaultEndpoint.Should().StartWith("https://",
			because: "the shipped default must satisfy the https-only transport guard for a remote host");
	}

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
	[Description("Prefers the settings-file endpoint over the shipped default when the environment variable is not set.")]
	public void Resolve_Should_Prefer_Settings_Endpoint_Over_Default()
	{
		// Arrange
		TelemetryFlushOptionsProvider provider = CreateProvider(
			new TelemetrySettings { Endpoint = "https://settings.example.com/v1/logs", IngestKey = "settings-key" });

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeTrue(
			because: "an endpoint configured in the settings file enables uploading");
		options.Endpoint.Should().Be("https://settings.example.com/v1/logs",
			because: "an explicitly configured settings endpoint must win over the shipped production default");
		options.IngestKey.Should().Be("settings-key",
			because: "the ingest key is read from the same settings section");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to the shipped production default endpoint when neither the environment variables nor the settings file configure one.")]
	public void Resolve_Should_Use_Default_Endpoint_When_Nothing_Configured()
	{
		// Arrange
		TelemetryFlushOptionsProvider provider = CreateProvider(new TelemetrySettings());

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeTrue(
			because: "a built-in default endpoint ships so fresh and updated installs upload without manual configuration");
		options.Endpoint.Should().Be(TelemetryFlushOptionsProvider.DefaultEndpoint,
			because: "the shipped production default is the lowest-precedence endpoint source");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats an invalid or non-http(s) endpoint as not configured so uploading stays disabled and does not silently fall back to the default.")]
	public void Resolve_Should_Disable_Sending_When_Endpoint_Invalid()
	{
		// Arrange
		TelemetryFlushOptionsProvider relativeUrl = CreateProvider(new TelemetrySettings { Endpoint = "not-a-url" });
		TelemetryFlushOptionsProvider wrongScheme = CreateProvider(
			new TelemetrySettings { Endpoint = "ftp://collector.example.com/v1/logs" });

		// Act / Assert
		relativeUrl.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "a malformed configured endpoint must disable uploading instead of failing at POST time or using the default");
		wrongScheme.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "only http(s) endpoints are valid OTLP/HTTP targets");
	}

	[Test]
	[Category("Unit")]
	[Description("Disables uploading when the settings opt-out flag is set, even though a default endpoint ships and an endpoint is configured.")]
	public void Resolve_Should_Disable_Sending_When_Settings_Opt_Out()
	{
		// Arrange
		TelemetryFlushOptionsProvider provider = CreateProvider(
			new TelemetrySettings { Enabled = false, Endpoint = "https://settings.example.com/v1/logs" });

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeFalse(
			because: "an explicit telemetry.enabled:false hard-disables uploading even when an endpoint is configured");
	}

	[Test]
	[Category("Unit")]
	[Description("Disables uploading when the CLIO_TELEMETRY_ENABLED environment variable is false, overriding the shipped default endpoint.")]
	public void Resolve_Should_Disable_Sending_When_Env_Opt_Out()
	{
		// Arrange
		using EnvironmentVariableScope enabledScope = new(
			TelemetryFlushOptionsProvider.EnabledEnvironmentVariable, "false");
		TelemetryFlushOptionsProvider provider = CreateProvider(new TelemetrySettings());

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeFalse(
			because: "CLIO_TELEMETRY_ENABLED=false is an operator kill switch that suppresses the shipped default endpoint");
	}

	[Test]
	[Category("Unit")]
	[Description("Lets CLIO_TELEMETRY_ENABLED=true re-enable uploading over a settings opt-out, mirroring the endpoint env-over-settings precedence.")]
	public void Resolve_Should_Enable_Sending_When_Env_Opt_Out_Overrides_Settings_Flag()
	{
		// Arrange
		using EnvironmentVariableScope enabledScope = new(
			TelemetryFlushOptionsProvider.EnabledEnvironmentVariable, "true");
		TelemetryFlushOptionsProvider provider = CreateProvider(new TelemetrySettings { Enabled = false });

		// Act
		TelemetryFlushOptions options = provider.Resolve();

		// Assert
		options.IsSendingEnabled.Should().BeTrue(
			because: "the CLIO_TELEMETRY_ENABLED environment variable wins over the settings flag, so it can re-enable a fleet");
		options.Endpoint.Should().Be(TelemetryFlushOptionsProvider.DefaultEndpoint,
			because: "with uploading re-enabled and nothing else configured, the shipped default endpoint applies");
	}

	[Test]
	[Category("Unit")]
	[Description("Requires https for remote endpoints but allows loopback http for local-collector testing.")]
	public void Resolve_Should_Require_Https_For_Remote_But_Allow_Loopback_Http()
	{
		// Arrange
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

	[Test]
	[Category("Unit")]
	[Description("THROWAWAY build: permits cleartext http to RFC1918 private IPs (stage NodePort testing) but keeps public hosts and hostnames https-only.")]
	public void Resolve_Should_Allow_Private_Ip_Http_But_Reject_Public_Cleartext()
	{
		// Arrange
		TelemetryFlushOptionsProvider privateTen = CreateProvider(
			new TelemetrySettings { Endpoint = "http://10.48.14.67:31419/v1/logs" });
		TelemetryFlushOptionsProvider private192 = CreateProvider(
			new TelemetrySettings { Endpoint = "http://192.168.1.10:31419/v1/logs" });
		TelemetryFlushOptionsProvider publicIp = CreateProvider(
			new TelemetrySettings { Endpoint = "http://8.8.8.8/v1/logs" });
		TelemetryFlushOptionsProvider privateHostname = CreateProvider(
			new TelemetrySettings { Endpoint = "http://collector.internal/v1/logs" });

		// Act / Assert
		privateTen.Resolve().IsSendingEnabled.Should().BeTrue(
			because: "the throwaway build allows cleartext http to an RFC1918 private IP for internal NodePort testing");
		private192.Resolve().IsSendingEnabled.Should().BeTrue(
			because: "192.168.0.0/16 is also a private range covered by the relaxation");
		publicIp.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "a public IP over http must stay rejected - the relaxation never enables internet cleartext");
		privateHostname.Resolve().IsSendingEnabled.Should().BeFalse(
			because: "only IP literals qualify; a hostname could resolve anywhere and is never treated as private");
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
