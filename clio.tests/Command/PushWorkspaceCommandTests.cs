using System;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class PushWorkspaceCommandTests : BaseCommandTests<PushWorkspaceCommandOptions> {
	private PushWorkspaceCommand _command = null!;
	private IWorkspace _workspace = null!;
	private IWorkspacePageTextInspector _inspector = null!;
	private ILogger _logger = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_workspace = Substitute.For<IWorkspace>();
		_workspace.GetFilteredPackages().Returns(["UsrPkg"]);
		_inspector = Substitute.For<IWorkspacePageTextInspector>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_workspace);
		containerBuilder.AddSingleton(_inspector);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<PushWorkspaceCommand>();
	}

	public override void TearDown() {
		_workspace.ClearReceivedCalls();
		_inspector.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Warns with the schema and element names for a page with inline literals and still installs the workspace.")]
	public void Execute_ShouldWarnAndStillInstall_WhenPageHasInlineLiterals() {
		// Arrange
		_inspector.Inspect(Arg.Any<System.Collections.Generic.IEnumerable<string>>())
			.Returns([new PageTextFinding("UsrPkg", "UsrPkg_FormPage", ["UsrLabel.caption", "UsrTab.caption"])]);

		// Act
		int exitCode = _command.Execute(new PushWorkspaceCommandOptions());

		// Assert
		exitCode.Should().Be(0, because: "the literal check is a warning and must never fail push-workspace");
		_workspace.Received(1).Install(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("'UsrPkg_FormPage'") &&
			message.Contains("'UsrPkg'") &&
			message.Contains("UsrLabel.caption, UsrTab.caption")));
	}

	[Test]
	[Description("Emits no literal warning when no page schema breaks the rule.")]
	public void Execute_ShouldNotWarn_WhenNoPageHasInlineLiterals() {
		// Arrange
		_inspector.Inspect(Arg.Any<System.Collections.Generic.IEnumerable<string>>()).Returns([]);

		// Act
		int exitCode = _command.Execute(new PushWorkspaceCommandOptions());

		// Assert
		exitCode.Should().Be(0, because: "a clean workspace pushes as before");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Downgrades an inspection failure to a warning and still installs the workspace.")]
	public void Execute_ShouldStillInstall_WhenInspectionThrows() {
		// Arrange
		_inspector.Inspect(Arg.Any<System.Collections.Generic.IEnumerable<string>>())
			.Returns(_ => throw new InvalidOperationException("unreadable file"));

		// Act
		int exitCode = _command.Execute(new PushWorkspaceCommandOptions());

		// Assert
		exitCode.Should().Be(0, because: "an advisory check must not block the deployment it advises on");
		_workspace.Received(1).Install(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>());
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("unreadable file")));
	}
}
