using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class CreatioArtifactMergeCommandTests : BaseCommandTests<CreatioArtifactMergeOptions> {
	private ICreatioArtifactMergeService _mergeService = null!;
	private ILogger _logger = null!;
	private CreatioArtifactMergeCommand _command = null!;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_mergeService = Substitute.For<ICreatioArtifactMergeService>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_mergeService);
		containerBuilder.AddSingleton(_logger);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreatioArtifactMergeCommand>();
	}

	public override void TearDown() {
		_mergeService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Keeps the CLI merge command available without an experimental feature flag.")]
	public void Options_ShouldNotCarryFeatureToggle_WhenCommandIsPublic() {
		// Arrange
		FeatureToggleAttribute attribute = typeof(CreatioArtifactMergeOptions)
			.GetCustomAttribute<FeatureToggleAttribute>(inherit: false);

		// Act
		bool isFeatureGated = attribute is not null;

		// Assert
		isFeatureGated.Should().BeFalse(
			because: "the merge command must be available without local feature configuration");
	}

	[Test]
	[Description("Reads the three explicit stage files and emits the shared merge result as JSON.")]
	public void Execute_ShouldReturnZero_WhenMergeIsResolved() {
		// Arrange
		CreatioArtifactMergeOptions options = AddFiles();
		CreatioArtifactMergeResult result = Result("resolved", "merged");
		_mergeService.MergeAsync(Arg.Any<CreatioArtifactMergeArgs>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(result));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "a fully resolved merge is safe for shell automation to accept");
		_mergeService.Received(1).MergeAsync(
			Arg.Is<CreatioArtifactMergeArgs>(args =>
				args.ArtifactPath == options.ArtifactPath &&
				args.BaseContent == "base" &&
				args.OursContent == "ours" &&
				args.TheirsContent == "theirs" &&
				args.DescriptorContent == "descriptor"),
			Arg.Any<CancellationToken>());
		_logger.Received(1).WriteLine(Arg.Is<string>(json =>
			json.Contains("\"status\": \"resolved\"") && json.Contains("\"content\": \"merged\"")));
	}

	[Test]
	[Description("Returns a failing process exit code while preserving the structured conflict result.")]
	public void Execute_ShouldReturnOne_WhenConflictsRemain() {
		// Arrange
		CreatioArtifactMergeOptions options = AddFiles();
		_mergeService.MergeAsync(Arg.Any<CreatioArtifactMergeArgs>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Result("conflicts-remain", "<<<<<<< ours\n=======\n>>>>>>> theirs")));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "unresolved marker content must fail closed in shell automation");
		_logger.Received(1).WriteLine(Arg.Is<string>(json => json.Contains("\"status\": \"conflicts-remain\"")));
	}

	[Test]
	[Description("Reports an input error without invoking the resolver when a stage file cannot be read.")]
	public void Execute_ShouldReturnOne_WhenStageFileIsMissing() {
		// Arrange
		CreatioArtifactMergeOptions options = AddFiles();
		options.BaseFile = "missing.json";

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "missing stage evidence prevents a valid three-way merge");
		_mergeService.DidNotReceiveWithAnyArgs().MergeAsync(default!, default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.StartsWith("Unable to read merge input:")));
	}

	[Test]
	[Description("Rejects an oversized stage file before allocating its contents or invoking the merge service.")]
	public void Execute_ShouldReturnOne_WhenCombinedStageFilesExceedLimit() {
		// Arrange
		CreatioArtifactMergeOptions options = AddFiles();
		FileSystem.AddFile(options.BaseFile, new(new string('x', CreatioArtifactMergeArgs.MaxCombinedContentBytes + 1)));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "oversized stage evidence must fail before entering semantic merge code");
		_mergeService.DidNotReceiveWithAnyArgs().MergeAsync(default!, default);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("4 MiB")));
	}

	private CreatioArtifactMergeOptions AddFiles() {
		FileSystem.AddFile("base.json", new("base"));
		FileSystem.AddFile("ours.json", new("ours"));
		FileSystem.AddFile("theirs.json", new("theirs"));
		FileSystem.AddFile("descriptor.json", new("descriptor"));
		return new CreatioArtifactMergeOptions {
			ArtifactPath = "packages/Test/Schemas/UsrObject/metadata.json",
			BaseFile = "base.json",
			OursFile = "ours.json",
			TheirsFile = "theirs.json",
			DescriptorFile = "descriptor.json"
		};
	}

	private static CreatioArtifactMergeResult Result(string status, string content) {
		return new CreatioArtifactMergeResult(
			status,
			"entity-schema-metadata",
			"test",
			content,
			CreatioArtifactMergeReport.Empty,
			[]);
	}
}
