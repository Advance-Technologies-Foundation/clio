using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Common.RuntimeAttachment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public class RuntimeAttachmentTests {
	private IAttachmentProcess _process;
	private IRuntimeAttachmentProvider _provider;
	private MockFileSystem _files;
	private RuntimeAttachmentService _service;
	private string _workspace;

	[SetUp]
	public void SetUp() {
		_process = Substitute.For<IAttachmentProcess>();
		_process.Run("mutagen", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns("0.18.1");
		_provider = Substitute.For<IRuntimeAttachmentProvider>();
		_provider.Name.Returns("operator-kubernetes");
		_files = new MockFileSystem();
		_workspace = _files.Path.GetFullPath("attachment-test");
		_files.AddFile(_files.Path.Combine(_workspace, ".clio", "workspaceSettings.json"), new MockFileData("{}"));
		_files.AddDirectory(_files.Path.Combine(_workspace, "packages"));
		_service = new RuntimeAttachmentService([_provider], _process, _files, Substitute.For<ILogger>());
	}

	private AttachOptions Options() => new() { Workspace = _workspace, Provider = "operator-kubernetes",
		Mode = "develop", SshAlias = "test-runtime", Environment = "test" };

	private void SuccessfulRuntime(bool endpointFailure = false, bool conflict = false) {
		_provider.Discover(Arg.Any<AttachOptions>()).Returns(new RuntimeTarget {
			Context = "test", Namespace = "test", Name = "test", Uid = "original",
			WorkspaceRoot = "/workspaces", PackageRoot = "/packages"
		});
		bool created = false;
		_process.Run("mutagen", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(call => {
			IReadOnlyList<string> args = call.ArgAt<IReadOnlyList<string>>(1);
			if (args.Contains("version")) { return "0.18.1"; }
			if (args.Contains("create")) { created = true; }
			if (args.Contains("terminate")) { created = false; }
			if (!args.Contains("list")) { return ""; }
			if (!created) { return "[]"; }
			AttachmentReceipt receipt = JsonSerializer.Deserialize<AttachmentReceipt>(
				_files.File.ReadAllText(_files.Path.Combine(_workspace, ".clio", "attachment.local.json")));
			return JsonSerializer.Serialize(new[] { new {
				name = receipt.Session,
				alpha = new { path = _workspace, connected = true, scanned = true },
				beta = new { host = receipt.SshAlias, path = receipt.RemoteWorkspace, connected = true,
					scanned = true, transitionProblems = endpointFailure ? new[] { new { error = "denied" } } : [] },
				conflicts = conflict ? new[] { new { path = "test" } } : []
			} });
		});
	}

	[Test, Description("Successful detach terminates only its owned session and preserves workspace content.")]
	public void Detach_ShouldPreserveFiles_WhenAttachedSuccessfully() {
		// Arrange
		SuccessfulRuntime();
		string source = _files.Path.Combine(_workspace, "packages", "test.txt");
		_files.AddFile(source, new MockFileData("keep"));
		_service.Attach(Options());
		// Act
		_service.Detach(_workspace);
		// Assert
		_files.File.ReadAllText(source).Should().Be("keep", because: "detachment never deletes source files");
		JsonSerializer.Deserialize<AttachmentReceipt>(_files.File.ReadAllText(_files.Path.Combine(_workspace, ".clio", "attachment.local.json")))
			.Detached.Should().BeTrue(because: "the retained receipt allows safe reattachment to the same files");
		_provider.Received(1).Release(Arg.Is<AttachmentReceipt>(r => r.Target.Uid == "original"));
	}

	[TestCase(true, false), TestCase(false, true)]
	[Description("File transition failures and conflicts are not reported as a healthy attachment, but can still be detached.")]
	public void Attach_ShouldRetainRecoverableReceipt_WhenSyncHasProblems(bool endpointFailure, bool conflict) {
		// Arrange
		SuccessfulRuntime(endpointFailure, conflict);
		// Act
		Action attach = () => _service.Attach(Options());
		// Assert
		attach.Should().Throw<InvalidOperationException>(because: "partial synchronization is not readiness");
		_files.File.Exists(_files.Path.Combine(_workspace, ".clio", "attachment.local.json")).Should().BeTrue(because: "cleanup needs its ownership record");
		Action detach = () => _service.Detach(_workspace);
		detach.Should().NotThrow(because: "stopping sync must not require resolving a content conflict");
	}

	[Test, Description("Repeating attachment reuses its owned session instead of creating another synchronization writer.")]
	public void Attach_ShouldReuseSession_WhenRepeated() {
		// Arrange
		SuccessfulRuntime();
		_service.Attach(Options());
		// Act
		_service.Attach(Options());
		// Assert
		_process.Received(1).Run("mutagen", Arg.Is<IReadOnlyList<string>>(a => a.Contains("create")), null);
	}

	[Test, Description("Reattaching a detached workspace reuses the same remote directory rather than conflicting with its preserved links.")]
	public void Attach_ShouldReuseRemoteWorkspace_WhenReattachedAfterDetach() {
		// Arrange
		SuccessfulRuntime();
		_service.Attach(Options());
		string receiptPath = _files.Path.Combine(_workspace, ".clio", "attachment.local.json");
		AttachmentReceipt before = JsonSerializer.Deserialize<AttachmentReceipt>(_files.File.ReadAllText(receiptPath));
		_service.Detach(_workspace);
		// Act
		_service.Attach(Options());
		// Assert
		AttachmentReceipt after = JsonSerializer.Deserialize<AttachmentReceipt>(_files.File.ReadAllText(receiptPath));
		after.RemoteWorkspace.Should().Be(before.RemoteWorkspace, because: "the preserved runtime links must still point at the same workspace");
		after.Detached.Should().BeFalse(because: "the attachment is active again");
	}

	[Test, Description("A missing local Mutagen executable fails before any provider operation or attachment receipt.")]
	public void Attach_ShouldFailBeforeMutation_WhenMutagenIsMissing() {
		// Arrange
		_process.Run("mutagen", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(_ => throw new InvalidOperationException());
		// Act
		Action act = () => _service.Attach(Options());
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*Install Mutagen locally*", because: "sync is a required dependency");
		_provider.DidNotReceive().Discover(Arg.Any<AttachOptions>());
		_files.File.Exists(_files.Path.Combine(_workspace, ".clio", "attachment.local.json")).Should().BeFalse(because: "preflight must not create attachment state");
	}

	[Test, Description("Unsupported providers fail without invoking Kubernetes or Mutagen.")]
	public void Attach_ShouldRejectProvider_WhenUnsupported() {
		// Arrange
		AttachOptions options = Options(); options.Provider = "iis";
		// Act
		Action act = () => _service.Attach(options);
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("Unsupported*", because: "IIS is outside this slice");
		_process.DidNotReceiveWithAnyArgs().Run(default, default, default);
	}

	[Test, Description("An invalid local workspace cannot result in remote preparation.")]
	public void Attach_ShouldRejectWorkspace_WhenMissing() {
		// Arrange
		AttachOptions options = Options(); options.Workspace = _workspace + "-missing";
		// Act
		Action act = () => _service.Attach(options);
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("Expected an existing*", because: "attachment requires a workspace");
		_provider.DidNotReceive().Prepare(Arg.Any<AttachmentReceipt>());
	}

	[Test, Description("Detaching a workspace without a receipt does not touch any remote resource.")]
	public void Detach_ShouldBeIdempotent_WhenNotAttached() {
		// Arrange / Act
		_service.Detach(_workspace);
		// Assert
		_process.DidNotReceiveWithAnyArgs().Run(default, default, default);
		_provider.DidNotReceive().Release(Arg.Any<AttachmentReceipt>());
	}

	[Test, Description("The operator provider refuses an ambiguous name across namespaces.")]
	public void Discover_ShouldRejectAmbiguity_WhenMultipleResourcesMatch() {
		// Arrange
		_process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns("""
{"items":[{"metadata":{"name":"test"}},{"metadata":{"name":"test"}}]}
""");
		KubernetesAttachmentProvider provider = new(_process);
		AttachOptions options = Options(); options.Context = "local-cluster";
		// Act
		Action act = () => provider.Discover(options);
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("Expected exactly one*", because: "namespace selection must be unambiguous");
	}

	[Test, Description("Cleanup refuses a runtime recreated under the same name.")]
	public void Release_ShouldRejectReplacement_WhenRuntimeUidChanged() {
		// Arrange
		_process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns("{\"metadata\":{\"uid\":\"new\"}}");
		KubernetesAttachmentProvider provider = new(_process);
		// Act
		Action act = () => provider.Release(new AttachmentReceipt { Target = new RuntimeTarget {
			Context = "local", Namespace = "test", Name = "test", Uid = "old" } });
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("The runtime was replaced*", because: "names do not prove ownership");
	}
}
