using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// A page <c>schema-name</c> becomes the folder <c>.clio-pages/{schema-name}</c> and the lock file
/// <c>.clio-pages/.locks/{schema-name}.lock</c>. These tests pin that a name which is not a plain name is
/// refused before any file is touched and before Creatio is asked, in every page tool and in the path
/// builders they share, and that a normal name resolves to the same paths as before.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageSchemaNameConfinementTests {

	private const string PlainName = "UsrLeads_FormPage";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string RefusalFragment = "is not a plain page schema name";

	private const string ValidBody =
		"define(\"UsrLeads_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	/// <summary>Names that would leave <c>.clio-pages</c> when joined into a path, on any platform.</summary>
	private static readonly string[] HostileNames = [
		"../../Outside",
		"..",
		"sub/Page",
		"/abs/Outside",
		"sub\\Page",
		// A drive-relative name: refused for its ':' on every OS, so it needs no Windows run.
		"C:Outside",
		// Windows drops a trailing dot, so the folder used would not be the folder named.
		"Trailing.",
		" LeadingSpace",
		"Control\u0001Char",
		// clio's own entries under .clio-pages: the lock folder, the staging folder and the ignore file.
		".locks",
		".staging",
		".gitignore"
	];

	/// <summary>Names every page tool reports as missing, in its own words.</summary>
	private static readonly string[] BlankNames = [null, "", "  "];

	private IApplicationClient _applicationClient;
	private IToolCommandResolver _commandResolver;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private PageUpdateCommand _updateCommand;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SelectQuery")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("GetSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true, "schema": {"body": "old body"} }""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("""{"success": true}""");
		_logger = Substitute.For<ILogger>();
		_fileSystem = CreateRecordingFileSystem();
		_updateCommand = CreateUpdateCommand(new PageBaselineGuard(_fileSystem));
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(_updateCommand);
	}

	#region Helpers

	// Real path arithmetic, recorded File and Directory: any probe, read, write, create or delete shows up
	// as a received call, wherever its path points.
	private static IFileSystem CreateRecordingFileSystem() {
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IDirectory directory = Substitute.For<IDirectory>();
		directory.GetCurrentDirectory().Returns("/ws");
		fileSystem.Path.Returns(new MockFileSystem().Path);
		fileSystem.File.Returns(Substitute.For<IFile>());
		fileSystem.Directory.Returns(directory);
		return fileSystem;
	}

	private static IServiceUrlBuilder CreateServiceUrlBuilder() {
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(call => "http://test" + call.Arg<string>());
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>())
			.Returns(call => "http://test" + ServiceUrlBuilder.KnownRoutes[call.Arg<ServiceUrlBuilder.KnownRoute>()]);
		return serviceUrlBuilder;
	}

	private PageUpdateCommand CreateUpdateCommand(IPageBaselineGuard guard) {
		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchyClient.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = PlainName, PackageUId = "test-pkg-uid" }
		]);
		return new PageUpdateCommand(
			_applicationClient, CreateServiceUrlBuilder(), _logger, guard, new PersistedResourceKeyReader(),
			hierarchyClient, viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>());
	}

	private PageUpdateTool CreateUpdateTool() =>
		new(_updateCommand, _logger, _commandResolver,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			new PageBaselineGuard(_fileSystem), new PersistedResourceKeyReader(),
			new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));

	private PageSyncTool CreateSyncTool(IFileSystem fileSystem) =>
		new(_commandResolver, fileSystem,
			Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(),
			new PageBaselineGuard(fileSystem), new PersistedResourceKeyReader(),
			new PageDataSourceReferenceValidator(new PageSchemaBodyParser()));

	// GetCurrentDirectory reads process state, not the disk, and the guard reads it while it prepares the
	// arguments of the path builder. Everything else on File or Directory is a touch of the disk.
	private static IEnumerable<string> DiskTouches(IFileSystem fileSystem) =>
		fileSystem.File.ReceivedCalls().Select(call => "File." + call.GetMethodInfo().Name)
			.Concat(fileSystem.Directory.ReceivedCalls()
				.Select(call => call.GetMethodInfo().Name)
				.Where(name => name != nameof(IDirectory.GetCurrentDirectory))
				.Select(name => "Directory." + name));

	// The verify read-back of sync-pages is private and, through the tool, is reached only after the per-page
	// name check. Calling it directly is the only way to pin that it confines the name by itself.
	private static (string BodyFile, string MetaWarning) PublishVerifiedReadBack(
		PageSyncTool tool, string schemaName, string outputDirectory, PageGetResponse readBack) {
		MethodInfo method = typeof(PageSyncTool).GetMethod(
			"PublishVerifiedReadBack", BindingFlags.Instance | BindingFlags.NonPublic);
		method.Should().NotBeNull(because: "the verify read-back of sync-pages is the method under test");
		try {
			return ((string, string))method.Invoke(tool, [schemaName, outputDirectory, "dev", readBack]);
		} catch (TargetInvocationException exception) when (exception.InnerException is not null) {
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
			throw;
		}
	}

	#endregion

	#region Path builders (the choke point)

	[TestCaseSource(nameof(HostileNames))]
	[Description("The meta.json path builder refuses a schema name that is not a plain name instead of joining it into a path.")]
	public void ResolveMetaFilePath_ShouldThrow_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		Action act = () => PageBaselineStore.ResolveMetaFilePath(
			fileSystem, "/ws", "/home/dev", "/home/dev/.clio", "/ws", bodyFile: null, hostileName, out _);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a name with a separator, '..' or a root would place meta.json outside .clio-pages")
			.WithMessage($"*{RefusalFragment}*", because: "the message has to say what is wrong with the name");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The lock path builder refuses a schema name that is not a plain name: answering null would mean 'run without the gate'.")]
	public void ResolveSchemaLockFilePath_ShouldThrow_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		Action act = () => PageBaselineStore.ResolveSchemaLockFilePath(fileSystem, "/ws/.clio-pages", hostileName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the lock file of such a name would be created outside .clio-pages/.locks")
			.WithMessage($"*{RefusalFragment}*", because: "the message has to say what is wrong with the name");
	}

	[Test]
	[Description("A normal page schema name resolves to the same meta.json and lock paths as before the name check.")]
	public void PathBuilders_ShouldResolveTheSamePaths_WhenSchemaNameIsPlain() {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		string metaFilePath = PageBaselineStore.ResolveMetaFilePath(
			fileSystem, "/ws", "/home/dev", "/home/dev/.clio", "/ws", bodyFile: null, PlainName, out string warning);
		string lockFromRoot = PageBaselineStore.ResolveSchemaLockFilePath(fileSystem, "/ws/.clio-pages", PlainName);
		string lockFromMeta = PageBaselineStore.ResolveSchemaLockFilePath(fileSystem, metaFilePath);

		// Assert
		metaFilePath.Should().Be(fileSystem.Path.Combine(fileSystem.Path.GetFullPath("/ws"), ".clio-pages", PlainName, "meta.json"),
			because: "the layout .clio-pages/{schema}/meta.json is what get-page writes and update-page reads");
		warning.Should().BeNull(because: "nothing about a plain name needs a warning");
		lockFromRoot.Should().Be(fileSystem.Path.Combine("/ws/.clio-pages", ".locks", PlainName + ".lock"),
			because: "the lock stays in the sibling .locks directory");
		lockFromMeta.Should().Be(fileSystem.Path.Combine(fileSystem.Path.GetFullPath("/ws"), ".clio-pages", ".locks", PlainName + ".lock"),
			because: "the lock derived from the meta.json path is the same file");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The baseline guard arms nothing and touches no file for a schema name that is not a plain name, even when a matching meta.json lies where the name points.")]
	public void TryArm_ShouldTouchNoFile_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		IInterprocessFileGate gate = Substitute.For<IInterprocessFileGate>();
		PageBaselineGuard guard = new(_fileSystem, gate);
		_fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
		_fileSystem.File.ReadAllText(Arg.Any<string>()).Returns(JsonSerializer.Serialize(new PageMetaFileModel {
			Baseline = new PageBaselineInfo {
				EnvironmentName = "dev", EditableSchemaExists = true, EditableSchemaUId = SchemaUId, Checksum = "planted"
			}
		}));
		PageUpdateOptions options = new() { SchemaName = hostileName, Environment = "dev", Body = ValidBody };

		// Act
		(string metaFilePath, bool refreshBaseline, string warning) = guard.TryArm(options, "/ws");

		// Assert
		DiskTouches(_fileSystem).Should().BeEmpty(
			because: "the path of such a name points outside .clio-pages, so it must not even be probed");
		gate.ReceivedCalls().Should().BeEmpty(because: "taking the gate creates the lock file and its directory");
		metaFilePath.Should().BeNull(because: "a later refresh must have no path to write to");
		refreshBaseline.Should().BeFalse(because: "nothing was armed");
		options.ExpectedChecksum.Should().BeNull(because: "a checksum read from outside .clio-pages must not arm the save");
		warning.Should().Contain(RefusalFragment, because: "the caller is told why no baseline was used");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The schema directory builder, the one place a schema name becomes a folder under .clio-pages, refuses a name that is not a plain name.")]
	public void ResolveSchemaDirectory_ShouldThrow_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		Action act = () => PageBaselineStore.ResolveSchemaDirectory(fileSystem, "/ws/.clio-pages", hostileName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the folder of such a name would be outside .clio-pages or one of clio's own folders in it")
			.WithMessage($"*{RefusalFragment}*", because: "the message has to say what is wrong with the name");
	}

	[TestCaseSource(nameof(BlankNames))]
	[Description("The schema directory builder refuses a blank name: joining it would answer the .clio-pages root itself.")]
	public void ResolveSchemaDirectory_ShouldThrow_WhenSchemaNameIsBlank(string blankName) {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		Action act = () => PageBaselineStore.ResolveSchemaDirectory(fileSystem, "/ws/.clio-pages", blankName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a blank name has no folder of its own, and the root must not be handed out as a page folder");
	}

	[Test]
	[Description("The schema directory builder answers .clio-pages/{schema-name} for a normal page schema name.")]
	public void ResolveSchemaDirectory_ShouldJoinTheNameUnderTheRoot_WhenSchemaNameIsPlain() {
		// Arrange
		MockFileSystem fileSystem = new();

		// Act
		string schemaDirectory = PageBaselineStore.ResolveSchemaDirectory(fileSystem, "/ws/.clio-pages", PlainName);

		// Assert
		schemaDirectory.Should().Be(fileSystem.Path.Combine("/ws/.clio-pages", PlainName),
			because: "the layout .clio-pages/{schema} is what get-page writes and the verify read-back rewrites");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The page-name check every page tool runs first reports a name that is not a plain name, including a name that starts with a dot.")]
	public void DescribeSchemaNameRefusal_ShouldReturnTheRefusal_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange & Act
		string refusal = PageBaselineStore.DescribeSchemaNameRefusal(hostileName);

		// Assert
		refusal.Should().Contain(RefusalFragment,
			because: "each page tool returns this text as its error before it touches a file or asks Creatio");
	}

	[TestCaseSource(nameof(BlankNames))]
	[Description("The baseline guard arms nothing, touches no file and adds no warning for a blank schema name: the tool's own 'required' error is the whole answer.")]
	public void TryArm_ShouldReturnNoWarningAndTouchNoFile_WhenSchemaNameIsBlank(string blankName) {
		// Arrange
		IInterprocessFileGate gate = Substitute.For<IInterprocessFileGate>();
		PageBaselineGuard guard = new(_fileSystem, gate);
		PageUpdateOptions options = new() { SchemaName = blankName, Environment = "dev", Body = ValidBody };

		// Act
		(string metaFilePath, bool refreshBaseline, string warning) = guard.TryArm(options, "/ws");

		// Assert
		warning.Should().BeNull(
			because: "a DISARMED warning about a name that is 'not a plain page schema name' would mislead next to 'schema-name is required'");
		metaFilePath.Should().BeNull(because: "a blank name has no baseline folder");
		refreshBaseline.Should().BeFalse(because: "nothing was armed");
		DiskTouches(_fileSystem).Should().BeEmpty(because: "there is no baseline to probe for a blank name");
		gate.ReceivedCalls().Should().BeEmpty(because: "there is no lock to take for a blank name");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The after-save baseline refresh used by localize-page touches no file for a schema name that is not a plain name.")]
	public void RefreshAfterSave_ShouldTouchNoFile_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		IInterprocessFileGate gate = Substitute.For<IInterprocessFileGate>();
		PageBaselineGuard guard = new(_fileSystem, gate);
		_fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
		bool savedMetadataRead = false;

		// Act
		string warning = guard.RefreshAfterSave(
			new EnvironmentOptions { Environment = "dev" }, hostileName, SchemaUId, "/ws", "checksum",
			() => {
				savedMetadataRead = true;
				return ("new", "2026-01-01");
			});

		// Assert
		DiskTouches(_fileSystem).Should().BeEmpty(because: "the refresh must not probe or rewrite a file outside .clio-pages");
		gate.ReceivedCalls().Should().BeEmpty(because: "taking the gate creates the lock file and its directory");
		savedMetadataRead.Should().BeFalse(because: "without a baseline there is nothing to refresh, so Creatio is not asked");
		warning.Should().BeNull(because: "no baseline exists for such a name, which is not a failure of the save");
	}

	#endregion

	#region update-page

	[TestCaseSource(nameof(HostileNames))]
	[Description("update-page refuses a schema name that is not a plain name with an ordinary tool error, before any file is touched and before Creatio is asked.")]
	public async Task UpdatePage_ShouldRefuseWithoutFileOrCreatioCall_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		PageUpdateTool tool = CreateUpdateTool();
		PageUpdateArgs args = new(hostileName, ValidBody, OutputDirectory: "/ws") { EnvironmentName = "dev" };

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args);

		// Assert
		response.Success.Should().BeFalse(because: "the name would make clio work outside .clio-pages");
		response.Error.Should().Contain(RefusalFragment, because: "the caller has to learn which argument to fix");
		_applicationClient.ReceivedCalls().Should().BeEmpty(because: "the refusal comes before any request to Creatio");
		_commandResolver.ReceivedCalls().Should().BeEmpty(because: "no environment is resolved for a refused call");
		DiskTouches(_fileSystem).Should().BeEmpty(because: "no baseline is probed, locked or read for a refused name");
	}

	[Test]
	[Description("update-page marks the refusal of a dry run as a dry run, like every other failure of a dry run.")]
	public async Task UpdatePage_ShouldStampDryRun_WhenRefusedCallWasADryRun() {
		// Arrange
		PageUpdateTool tool = CreateUpdateTool();
		PageUpdateArgs args = new("../../Outside", ValidBody, DryRun: true, OutputDirectory: "/ws") { EnvironmentName = "dev" };

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args);

		// Assert
		response.Error.Should().Contain(RefusalFragment, because: "the name is refused in a dry run as well");
		response.DryRun.Should().BeTrue(because: "a failed dry run must be distinguishable from a failed save");
	}

	[Test]
	[Description("update-page still saves a page with a normal schema name and looks for its baseline under .clio-pages/{schema-name}.")]
	public async Task UpdatePage_ShouldSaveAndProbeTheBaselineUnderClioPages_WhenSchemaNameIsPlain() {
		// Arrange
		PageUpdateTool tool = CreateUpdateTool();
		PageUpdateArgs args = new(PlainName, ValidBody, OutputDirectory: "/ws") { EnvironmentName = "dev" };
		string expectedMeta = _fileSystem.Path.Combine(_fileSystem.Path.GetFullPath("/ws"), ".clio-pages", PlainName, "meta.json");

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args);

		// Assert
		response.Success.Should().BeTrue(because: $"a normal name is not refused (error: {response.Error})");
		_fileSystem.File.Received().Exists(expectedMeta);
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema")), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[TestCaseSource(nameof(BlankNames))]
	[Description("update-page answers a blank schema name with its own 'required' error only: no DISARMED baseline warning rides along.")]
	public async Task UpdatePage_ShouldReportOnlyTheRequiredError_WhenSchemaNameIsBlank(string blankName) {
		// Arrange
		PageUpdateTool tool = CreateUpdateTool();
		PageUpdateArgs args = new(blankName, ValidBody, OutputDirectory: "/ws") { EnvironmentName = "dev" };

		// Act
		PageUpdateResponse response = await tool.UpdatePage(args);

		// Assert
		response.Success.Should().BeFalse(because: "a page cannot be saved without a schema name");
		response.Error.Should().Be("schemaName is required", because: "the tool reports a missing name in its own words");
		(response.Warnings ?? []).Should().NotContain(
			warning => warning.Contains("DISARMED") || warning.Contains(RefusalFragment),
			because: "a blank name is a missing argument, not a name that is not plain, and no detection was skipped for it");
	}

	[TestCaseSource(nameof(BlankNames))]
	[Description("The CLI update-page answers a blank schema name with its 'required' error only: the output carries no DISARMED baseline warning.")]
	public void UpdatePageCommand_ShouldReportOnlyTheRequiredError_WhenSchemaNameIsBlank(string blankName) {
		// Arrange
		PageUpdateCommand command = CreateUpdateCommand(new PageBaselineGuard(_fileSystem));
		PageUpdateOptions options = new() { SchemaName = blankName, Body = ValidBody, Environment = "dev" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a missing schema name is a failed command");
		_logger.Received().WriteInfo(Arg.Is<string>(json => json.Contains("schemaName is required")));
		_logger.DidNotReceive().WriteInfo(Arg.Is<string>(json => json.Contains("DISARMED") || json.Contains(RefusalFragment)));
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(text => text.Contains("DISARMED") || text.Contains(RefusalFragment)));
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The CLI update-page refuses a schema name that is not a plain name without arming the baseline guard and without a request to Creatio.")]
	public void UpdatePageCommand_ShouldRefuseWithoutArmingOrCreatioCall_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		IPageBaselineGuard guard = Substitute.For<IPageBaselineGuard>();
		PageUpdateCommand command = CreateUpdateCommand(guard);
		PageUpdateOptions options = new() { SchemaName = hostileName, Body = ValidBody, Environment = "dev" };

		// Act
		int exitCode = command.Execute(options);

		// Assert
		exitCode.Should().Be(1, because: "a refused name is a failed command");
		guard.ReceivedCalls().Should().BeEmpty(because: "the baseline of a refused name is not looked up");
		_applicationClient.ReceivedCalls().Should().BeEmpty(because: "the refusal comes before any request to Creatio");
		_logger.Received().WriteInfo(Arg.Is<string>(json => json.Contains(RefusalFragment)));
	}

	#endregion

	#region sync-pages

	[Test]
	[Description("sync-pages fails every page whose schema name is not a plain name, and touches no file and asks Creatio nothing when no other page is in the batch.")]
	public async Task SyncPages_ShouldRefuseWithoutFileOrCreatioCall_WhenEverySchemaNameIsNotAPlainName() {
		// Arrange
		PageSyncTool tool = CreateSyncTool(_fileSystem);
		PageSyncArgs args = new("dev", HostileNames.Select(name => new PageSyncPageInput(name, ValidBody)).ToArray(),
			Verify: true, OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		response.Success.Should().BeFalse(because: "no page of the batch was saved");
		response.Pages.Should().HaveCount(HostileNames.Length, because: "every page gets its own result");
		response.Pages.Should().OnlyContain(page => !page.Success && page.Error.Contains(RefusalFragment),
			because: "each page is refused for its name");
		_applicationClient.ReceivedCalls().Should().BeEmpty(because: "the refusal comes before any request to Creatio");
		_commandResolver.ReceivedCalls().Should().BeEmpty(because: "no environment is resolved when every page is refused");
		DiskTouches(_fileSystem).Should().BeEmpty(because: "no baseline is probed and no read-back is written for a refused name");
	}

	[Test]
	[Description("sync-pages refuses only the page with the bad schema name: the other page of the batch is saved, and nothing appears outside .clio-pages.")]
	public async Task SyncPages_ShouldSaveTheOtherPage_WhenOnePageHasASchemaNameThatIsNotPlain() {
		// Arrange
		MockFileSystem fileSystem = new();
		fileSystem.AddFile("/Outside/meta.json", new MockFileData("keep"));
		string[] filesBefore = fileSystem.AllFiles.ToArray();
		string[] directoriesBefore = fileSystem.AllDirectories.ToArray();
		PageSyncTool tool = CreateSyncTool(fileSystem);
		PageSyncArgs args = new("dev", [
			new PageSyncPageInput("../../Outside", ValidBody),
			new PageSyncPageInput(PlainName, ValidBody)
		], Validate: false, OutputDirectory: "/ws/project");

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		response.Pages[0].Success.Should().BeFalse(because: "the first page has a name that leaves .clio-pages");
		response.Pages[0].Error.Should().Contain(RefusalFragment, because: "the page result says which argument to fix");
		response.Pages[1].Success.Should().BeTrue(
			because: $"a refused page must not stop the batch (error: {response.Pages[1].Error})");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(), Arg.Is<string>(body => body.Contains("Outside")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		fileSystem.AllFiles.Should().BeEquivalentTo(filesBefore, because: "no file is created, changed or deleted for the refused page");
		fileSystem.AllDirectories.Should().BeEquivalentTo(directoriesBefore, because: "no lock directory is created outside .clio-pages");
		fileSystem.File.ReadAllText("/Outside/meta.json").Should().Be("keep", because: "the file the name points at is left alone");
	}

	[TestCaseSource(nameof(BlankNames))]
	[Description("sync-pages answers a page with a blank schema name with the 'required' error only: the page result carries no DISARMED baseline warning.")]
	public async Task SyncPages_ShouldReportOnlyTheRequiredError_WhenSchemaNameIsBlank(string blankName) {
		// Arrange
		PageSyncTool tool = CreateSyncTool(_fileSystem);
		PageSyncArgs args = new("dev", [new PageSyncPageInput(blankName, ValidBody)], Validate: false, OutputDirectory: "/ws");

		// Act
		PageSyncResponse response = await tool.SyncPages(args);

		// Assert
		response.Pages.Should().ContainSingle(because: "the one page of the batch gets its own result");
		response.Pages[0].Success.Should().BeFalse(because: "a page cannot be saved without a schema name");
		response.Pages[0].Error.Should().Be("schemaName is required", because: "a missing name is reported in the tool's own words");
		(response.Pages[0].Validation?.Warnings ?? []).Should().NotContain(
			warning => warning.Contains("DISARMED") || warning.Contains(RefusalFragment),
			because: "a blank name is a missing argument, not a name that is not plain, and no detection was skipped for it");
	}

	[Test]
	[Description("The sync-pages verify=true read-back writes body.js and meta.json into .clio-pages/{schema-name} for a normal schema name.")]
	public void SyncPagesVerifyReadBack_ShouldWriteUnderClioPages_WhenSchemaNameIsPlain() {
		// Arrange
		MockFileSystem fileSystem = new();
		PageSyncTool tool = CreateSyncTool(fileSystem);
		PageGetResponse readBack = new() { Success = true, Raw = new PageRawInfo { Body = ValidBody } };
		string schemaDirectory = fileSystem.Path.Combine(fileSystem.Path.GetFullPath("/ws"), ".clio-pages", PlainName);

		// Act
		(string bodyFile, string metaWarning) = PublishVerifiedReadBack(tool, PlainName, "/ws", readBack);

		// Assert
		bodyFile.Should().Be(fileSystem.Path.Combine(schemaDirectory, "body.js"),
			because: "the verified body belongs in the schema's own folder under .clio-pages");
		fileSystem.File.ReadAllText(bodyFile).Should().Be(ValidBody, because: "the read-back body is what gets written");
		fileSystem.File.Exists(fileSystem.Path.Combine(schemaDirectory, "meta.json")).Should().BeTrue(
			because: $"the baseline of the read-back is written beside the body (warning: {metaWarning})");
	}

	[TestCaseSource(nameof(HostileNames))]
	[Description("The sync-pages verify=true read-back refuses a schema name that is not a plain name by itself, without relying on the per-page check that runs earlier, and writes nothing.")]
	public void SyncPagesVerifyReadBack_ShouldThrowAndWriteNothing_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		MockFileSystem fileSystem = new();
		string[] filesBefore = fileSystem.AllFiles.ToArray();
		string[] directoriesBefore = fileSystem.AllDirectories.ToArray();
		PageSyncTool tool = CreateSyncTool(fileSystem);
		PageGetResponse readBack = new() { Success = true, Raw = new PageRawInfo { Body = ValidBody } };

		// Act
		Action act = () => PublishVerifiedReadBack(tool, hostileName, "/ws", readBack);

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "the read-back builds its folder through the shared resolver, which refuses the name")
			.WithMessage($"*{RefusalFragment}*", because: "the message has to say what is wrong with the name");
		fileSystem.AllFiles.Should().BeEquivalentTo(filesBefore, because: "no body.js or meta.json is written for a refused name");
		fileSystem.AllDirectories.Should().BeEquivalentTo(directoriesBefore, because: "no folder is created for a refused name");
	}

	#endregion

	#region get-page

	[TestCaseSource(nameof(HostileNames))]
	[Description("get-page refuses a schema name that is not a plain name before Creatio is asked and before the page files are written.")]
	public void GetPage_ShouldRefuseWithoutFileOrCreatioCall_WhenSchemaNameIsNotAPlainName(string hostileName) {
		// Arrange
		IPageFileWriter writer = Substitute.For<IPageFileWriter>();
		PageGetCommand command = new(
			_applicationClient, CreateServiceUrlBuilder(), _logger,
			Substitute.For<IPageDesignerHierarchyClient>(), new PageSchemaBodyParser(),
			Substitute.For<IPageBundleBuilder>(), writer, Substitute.For<IPageLayerInspector>());
		_commandResolver.Resolve<PageGetCommand>(Arg.Any<PageGetOptions>()).Returns(command);
		PageGetTool tool = new(command, _logger, _commandResolver, writer);

		// Act
		PageGetResponse response = tool.GetPage(new PageGetArgs(hostileName, OutputDirectory: "/ws") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(because: "the page files of such a name would land outside .clio-pages");
		response.Error.Should().Contain(RefusalFragment, because: "the caller has to learn which argument to fix");
		_applicationClient.ReceivedCalls().Should().BeEmpty(because: "the refusal comes before any request to Creatio");
		_commandResolver.ReceivedCalls().Should().BeEmpty(because: "no environment is resolved for a refused call");
		writer.ReceivedCalls().Should().BeEmpty(because: "nothing is written for a refused name");
	}

	#endregion
}
