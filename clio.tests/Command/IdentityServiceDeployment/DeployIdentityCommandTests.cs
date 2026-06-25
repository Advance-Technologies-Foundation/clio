using System;
using System.Collections.Generic;
using Clio.Command.IdentityServiceDeployment;
using Clio.Common;
using ConsoleTables;
using FluentAssertions;
using FluentValidation.Results;
using NUnit.Framework;

namespace Clio.Tests.Command.IdentityServiceDeployment;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DeployIdentityCommandTests
{
	[Test]
	[Description("Successful deploy-identity output masks the OAuth client secret and reports only the client id.")]
	public void Execute_Should_Mask_Client_Secret_When_Deployment_Succeeds()
	{
		// Arrange
		FakeDeploymentService service = new(new IdentityServiceDeploymentResult(
			true,
			"deployed",
			"http://localhost:40086",
			"client-id"));
		TestLogger logger = new();
		DeployIdentityCommand command = new(service, logger);
		DeployIdentityOptions options = new();

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "successful IdentityService deployment should return a success exit code");
		string output = string.Join(Environment.NewLine, logger.Messages);
		output.Should().Contain("client-id",
			because: "the generated client id is safe and needed by the operator");
		output.Should().Contain("<stored in clio settings>",
			because: "the command should explain where the secret went without printing it");
		output.Should().NotContain("client-secret",
			because: "deploy-identity must not echo generated OAuth secrets to the console");
	}

	[Test]
	[Description("Successful deploy-identity output reports skipped OAuth app creation without implying that a client secret was stored.")]
	public void Execute_Should_Report_Skipped_Client_When_NoApp_Succeeds()
	{
		// Arrange
		FakeDeploymentService service = new(new IdentityServiceDeploymentResult(
			true,
			"deployed",
			"http://localhost:40086",
			string.Empty));
		TestLogger logger = new();
		DeployIdentityCommand command = new(service, logger);
		DeployIdentityOptions options = new();

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "successful IdentityService deployment should return a success exit code");
		string output = string.Join(Environment.NewLine, logger.Messages);
		output.Should().Contain("OAuth client: skipped",
			because: "no-app deployments should make skipped client creation explicit");
		output.Should().NotContain("<stored in clio settings>",
			because: "no-app deployments do not store generated clio OAuth credentials");
	}

	private sealed class FakeDeploymentService : IIdentityServiceDeploymentService
	{
		private readonly IdentityServiceDeploymentResult _result;

		public FakeDeploymentService(IdentityServiceDeploymentResult result) {
			_result = result;
		}

		public IdentityServiceDeploymentResult Deploy(DeployIdentityOptions options) => _result;
	}

	private sealed class TestLogger : ILogger
	{
		List<LogMessage> ILogger.LogMessages => [];
		bool ILogger.PreserveMessages { get; set; }
		internal List<string> Messages { get; } = [];

		public void ClearMessages() { }
		public IDisposable BeginScopedFileSink(string logFilePath) => new NoopDisposable();
		public void Start(string logFilePath = "") { }
		public void SetCreatioLogStreamer(ILogStreamer creatioLogStreamer) { }
		public void StartWithStream() { }
		public void Stop() { }
		public void Write(string value) => Messages.Add(value);
		public void WriteLine() => Messages.Add(string.Empty);
		public void WriteLine(string value) => Messages.Add(value);
		public void WriteWarning(string value) => Messages.Add(value);
		public void WriteError(string value) => Messages.Add(value);
		public void WriteInfo(string value) => Messages.Add(value);
		public void WriteDebug(string value) => Messages.Add(value);
		public void PrintTable(ConsoleTable table) { }
		public void PrintValidationFailureErrors(IEnumerable<ValidationFailure> errors) { }

		private sealed class NoopDisposable : IDisposable
		{
			public void Dispose() { }
		}
	}
}
