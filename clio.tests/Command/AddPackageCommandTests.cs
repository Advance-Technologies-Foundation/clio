using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.Command.ChainItems;
using Clio.Common;
using Clio.Package;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public class AddPackageCommandTests : BaseCommandTests<AddPackageOptions> {
	private AddPackageCommand _command = null!;
	private FakePackageCreator _packageCreator = null!;
	private ILogger _logger = null!;
	private FakeFollowUpChain _chain = null!;
	private FakeChainItem _chainItem = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_packageCreator = new FakePackageCreator();
		_logger = Substitute.For<ILogger>();
		_chain = new FakeFollowUpChain();
		_chainItem = new FakeChainItem();
		containerBuilder.AddSingleton<IPackageCreator>(_packageCreator);
		containerBuilder.AddSingleton(_logger);
		containerBuilder.AddSingleton<IFollowUpChain>(_chain);
		containerBuilder.AddKeyedSingleton<IFollowupUpChainItem>(nameof(DconfChainItem), _chainItem);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<AddPackageCommand>();
	}

	public override void TearDown() {
		_packageCreator.RejectPackageName = false;
		_logger.ClearReceivedCalls();
		_chain.ReceivedItems.Clear();
		base.TearDown();
	}

	[Test]
	[Description("Uses the explicit workspace path for add-package execution when MCP supplies one.")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided() {
		// Arrange
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string explicitWorkspacePath = Directory.CreateDirectory(
			Path.Combine(Path.GetTempPath(), $"add-package-{Guid.NewGuid():N}")).FullName;
		AddPackageOptions options = new() {
			Name = "MyPackage",
			WorkspacePath = explicitWorkspacePath
		};

		try {
			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because the command should complete when follow-up execution succeeds");
			NormalizeTempPathAlias(_packageCreator.CapturedCurrentDirectory).Should().Be(NormalizeTempPathAlias(explicitWorkspacePath),
				"because package creation should run inside the explicit workspace path");
			Environment.CurrentDirectory.Should().Be(originalCurrentDirectory,
				"because the command should restore process-global current directory after execution");
			_chain.ReceivedItems.Should().ContainSingle().Which.Should().BeSameAs(_chainItem,
				"because the configured follow-up item should still be added to the chain");
		}
		finally {
			Environment.CurrentDirectory = originalCurrentDirectory;
			Directory.Delete(explicitWorkspacePath, recursive: true);
		}
	}

	[Test]
	[Description("Returns a caller-correctable error message when the package creator rejects the package name.")]
	public void Execute_ShouldReturnValidationError_WhenPackageNameIsInvalid() {
		// Arrange
		_packageCreator.RejectPackageName = true;
		AddPackageOptions options = new() { Name = "../Escape" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "invalid package names are caller-correctable command failures");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.StartsWith(PackageCreator.InvalidPackageNameMessage, StringComparison.Ordinal)));
		_chain.ReceivedItems.Should().BeEmpty(
			because: "follow-up configuration must not run after package-name validation fails");
	}

	private static string? NormalizeTempPathAlias(string? path) =>
		path is not null && path.StartsWith("/private/var/", StringComparison.Ordinal)
			? path[8..]
			: path;

	private sealed class FakePackageCreator : IPackageCreator {
		public string CapturedCurrentDirectory { get; private set; }
		public bool RejectPackageName { get; set; }

		public void Create(string packageName, bool? asApp) {
			if (RejectPackageName) {
				throw new ArgumentException(PackageCreator.InvalidPackageNameMessage, nameof(packageName));
			}
			CapturedCurrentDirectory = Environment.CurrentDirectory;
		}

		public void Create(string packagesPath, string packageName) {
			CapturedCurrentDirectory = Environment.CurrentDirectory;
		}
	}

	private sealed class FakeChainItem : IFollowupUpChainItem {
		public ErrorOr<int> Execute() => 0;

		public ErrorOr<int> Execute(IDictionary<string, object> context) => 0;
	}

	private sealed class FakeFollowUpChain : IFollowUpChain, IExecutableChain {
		public List<IFollowupUpChainItem> ReceivedItems { get; } = [];

		public IDictionary<string, object> CreateContextFromOptions(object options) => new Dictionary<string, object>();

		public IExecutableChain With(IFollowupUpChainItem item) {
			ReceivedItems.Add(item);
			return this;
		}

		public ErrorOr<int> Execute() => 0;

		public ErrorOr<int> Execute(IDictionary<string, object> context) => 0;
	}
}
