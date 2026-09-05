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
		_logger.ClearReceivedCalls();
		_chain.ReceivedItems.Clear();
		base.TearDown();
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Uses the explicit workspace path for add-package execution when MCP supplies one.")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided(bool asApp) {
		// Arrange
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string explicitWorkspacePath = Directory.CreateDirectory(
			Path.Combine(Path.GetTempPath(), $"add-package-{Guid.NewGuid():N}")).FullName;
		AddPackageOptions options = new() {
			Name = "MyPackage",
			AsApp = asApp,
			WorkspacePath = explicitWorkspacePath
		};

		try {
			// Act
			int result = _command.Execute(options);

			// Assert
			result.Should().Be(0, "because the command should complete when follow-up execution succeeds");
			NormalizeTempPathAlias(_packageCreator.CapturedCurrentDirectory).Should().Be(NormalizeTempPathAlias(explicitWorkspacePath),
				"because package creation should run inside the explicit workspace path");
			_packageCreator.CapturedPackageName.Should().Be(options.Name,
				because: "the command must forward the requested package name unchanged");
			_packageCreator.CapturedAsApp.Should().Be(asApp,
				because: "the command must forward application-package intent unchanged");
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
	[Description("Returns a caller-correctable error message before invoking package creation for an invalid name.")]
	public void Execute_ShouldReturnValidationError_WhenPackageNameIsInvalid() {
		// Arrange
		AddPackageOptions options = new() { Name = "../Escape" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "invalid package names are caller-correctable command failures");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message == PackageCreator.InvalidPackageNameMessage));
		_packageCreator.CreateCallCount.Should().Be(0,
			because: "invalid input must be rejected before package creation is invoked");
		_chain.ReceivedItems.Should().BeEmpty(
			because: "follow-up configuration must not run after package-name validation fails");
	}

	[Test]
	[Description("Forwards the requested schema-name prefix to package creation unchanged.")]
	public void Execute_ShouldForwardSchemaNamePrefix_WhenRequested() {
		// Arrange
		AddPackageOptions options = new() {Name = "MyPackage", AsApp = true, SchemaNamePrefix = "Ktl"};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an explicit prefix is a valid request");
		_packageCreator.CapturedSchemaNamePrefix.Should().Be("Ktl",
			because: "the explicit prefix must reach the generator instead of the environment value");
	}

	[Test]
	[Description("Leaves the schema-name prefix unset so the generator reads it from the environment.")]
	public void Execute_ShouldForwardNullSchemaNamePrefix_WhenNotRequested() {
		// Arrange
		AddPackageOptions options = new() {Name = "MyPackage", AsApp = true};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "omitting the prefix is the default request");
		_packageCreator.CapturedSchemaNamePrefix.Should().BeNull(
			because: "only a null prefix lets the generator fall back to the environment setting");
	}

	[Test]
	[Description("Returns a caller-correctable error before creating anything for an unusable schema-name prefix.")]
	public void Execute_ShouldReturnValidationError_WhenSchemaNamePrefixIsInvalid() {
		// Arrange
		AddPackageOptions options = new() {Name = "MyPackage", AsApp = true, SchemaNamePrefix = "9-x"};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a prefix that cannot start a C# identifier is a caller-correctable command failure");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message == SchemaNamePrefixResolver.InvalidPrefixMessage));
		_packageCreator.CreateCallCount.Should().Be(0,
			because: "invalid input must be rejected before package creation is invoked");
		_chain.ReceivedItems.Should().BeEmpty(
			because: "follow-up configuration must not run after prefix validation fails");
	}

	[TestCase(" ")]
	[TestCase("\t")]
	[Description("Rejects a whitespace-only schema-name prefix instead of silently generating no prefix.")]
	public void Execute_ShouldReturnValidationError_WhenSchemaNamePrefixIsOnlyWhitespace(string prefix) {
		// Arrange
		AddPackageOptions options = new() {Name = "MyPackage", AsApp = true, SchemaNamePrefix = prefix};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "a whitespace-only value is a typo, not the documented request for no prefix");
		_packageCreator.CreateCallCount.Should().Be(0,
			because: "silently generating an unprefixed schema is the outcome this option exists to prevent");
	}

	[Test]
	[Description("Warns that a schema-name prefix is consumed by nothing when as-app is not requested.")]
	public void Execute_ShouldWarn_WhenSchemaNamePrefixIsSuppliedWithoutAsApp() {
		// Arrange
		AddPackageOptions options = new() {Name = "MyPackage", AsApp = false, SchemaNamePrefix = "Usr"};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an unused prefix is not a reason to fail the command");
		_logger.Received(1).WriteWarning(Arg.Is<string>(message =>
			message.Contains("--schema-name-prefix has no effect without --as-app")));
	}

	private static string? NormalizeTempPathAlias(string? path) =>
		path is not null && path.StartsWith("/private/var/", StringComparison.Ordinal)
			? path[8..]
			: path;

	private sealed class FakePackageCreator : IPackageCreator {
		public string CapturedCurrentDirectory { get; private set; }
		public string CapturedPackageName { get; private set; }
		public bool? CapturedAsApp { get; private set; }
		public string CapturedSchemaNamePrefix { get; private set; }
		public int CreateCallCount { get; set; }

		public void Create(string packageName, bool? asApp, string schemaNamePrefix = null) {
			CreateCallCount++;
			CapturedCurrentDirectory = Environment.CurrentDirectory;
			CapturedPackageName = packageName;
			CapturedAsApp = asApp;
			CapturedSchemaNamePrefix = schemaNamePrefix;
		}

		public void Create(string packagesPath, string packageName) {
			CreateCallCount++;
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
