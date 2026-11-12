using System;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// restore-workspace reports a restore that did not happen as a failure: an [ERR] line with the reason and a
/// non-zero exit code, never "Done".
/// </summary>
[TestFixture]
[Property("Module", "Command")]
public sealed class RestoreWorkspaceCommandTests : BaseCommandTests<RestoreWorkspaceOptions> {

	private IWorkspace _workspace;
	private ILogger _logger;
	private RestoreWorkspaceCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_workspace = Substitute.For<IWorkspace>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_workspace);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RestoreWorkspaceCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_workspace.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("The command half of the fix: when the restore throws because the environment did not answer (a rejected login), the command logs that reason as an error and exits 1. The restore throwing at all - instead of returning silently - is guarded by PackageDownloaderTests, WorkspaceRestorerTests and RestoreWorkspaceToolE2ETests; this test alone also passes on master.")]
	public void Execute_ShouldFailWithTheReason_WhenTheEnvironmentDidNotAnswer() {
		// Arrange
		const string reason = "Packages were not downloaded: the environment did not answer "
			+ "(Unauthorized Supervisor for http://localhost:5000).";
		_workspace.When(workspace => workspace.Restore(Arg.Any<WorkspaceOptions>()))
			.Do(_ => throw new InvalidOperationException(reason));

		// Act
		int exitCode = _command.Execute(new RestoreWorkspaceOptions {
			IsNugetRestore = false, IsCreateSolution = false, AddBuildProps = false
		});

		// Assert
		exitCode.Should().Be(1, because: "a restore that downloaded nothing must not look like a success to a script or an agent");
		_logger.Received(1).WriteError(reason);
		_logger.DidNotReceive().WriteInfo("Done");
	}
}
