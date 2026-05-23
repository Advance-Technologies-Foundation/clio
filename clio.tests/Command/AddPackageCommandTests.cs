using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class AddPackageCommandTests {

	[Test]
	[Description("Uses the explicit workspace path for add-package execution when MCP supplies one.")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided() {
		// Arrange
		FakePackageCreator packageCreator = new();
		ILogger logger = Substitute.For<ILogger>();
		IFollowupUpChainItem chainItem = new FakeChainItem();
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string explicitWorkspacePath = Directory.CreateDirectory(
			Path.Combine(Path.GetTempPath(), $"add-package-{Guid.NewGuid():N}")).FullName;
		FakeFollowUpChain chain = new();
		AddPackageCommand command = new(packageCreator, logger, chain, chainItem);
		AddPackageOptions options = new() {
			Name = "MyPackage",
			WorkspacePath = explicitWorkspacePath
		};

		try {
			// Act
			int result = command.Execute(options);

			// Assert
			result.Should().Be(0, "because the command should complete when follow-up execution succeeds");
			NormalizeTempPathAlias(packageCreator.CapturedCurrentDirectory).Should().Be(NormalizeTempPathAlias(explicitWorkspacePath),
				"because package creation should run inside the explicit workspace path");
			Environment.CurrentDirectory.Should().Be(originalCurrentDirectory,
				"because the command should restore process-global current directory after execution");
			chain.ReceivedItems.Should().ContainSingle().Which.Should().BeSameAs(chainItem,
				"because the configured follow-up item should still be added to the chain");
		}
		finally {
			Environment.CurrentDirectory = originalCurrentDirectory;
			Directory.Delete(explicitWorkspacePath, recursive: true);
		}
	}

	private static string? NormalizeTempPathAlias(string? path) =>
		path is not null && path.StartsWith("/private/var/", StringComparison.Ordinal)
			? path[8..]
			: path;

	private sealed class FakePackageCreator : IPackageCreator {
		public string CapturedCurrentDirectory { get; private set; }

		public void Create(string packageName, bool? asApp) {
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
