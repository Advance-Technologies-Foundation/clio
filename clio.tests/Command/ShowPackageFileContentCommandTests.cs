using System.IO;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class ShowPackageFileContentCommandTests : BaseCommandTests<ShowPackageFileContentOptions> {
	private IApplicationClient _applicationClient;
	private ShowPackageFileContentCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		base.AdditionalRegistrations(services);
		_applicationClient = Substitute.For<IApplicationClient>();
		services.AddSingleton(_applicationClient);
	}

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<ShowPackageFileContentCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Deserializes the ClioGate JSON array without splitting file names that contain commas.")]
	public void TryListPackageFiles_ShouldPreserveCommas_WhenGateReturnsAJsonArray() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode" };
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("GetPackageFilesDirectoryContent")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("[\"src/cs/Hello,World.cs\",\"UsrCode.csproj\"]");

		// Act
		bool success = _command.TryListPackageFiles(options, out PackageFileListResponse response);

		// Assert
		success.Should().BeTrue(because: "the gate returned a valid JSON file list");
		response.Files.Should().Contain("src/cs/Hello,World.cs",
			because: "a comma is part of the path and must not be treated as a list delimiter");
		response.Count.Should().Be(2, because: "both JSON array elements must be returned");
	}

	[Test]
	[Description("Uses ordinal case as a deterministic tie-breaker when a case-sensitive server returns case-variant paths.")]
	public void TryListPackageFiles_ShouldSortDeterministically_WhenPathsDifferOnlyByCase() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode" };
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("[\"src/foo.cs\",\"src/Foo.cs\"]");

		// Act
		bool success = _command.TryListPackageFiles(options, out PackageFileListResponse response);

		// Assert
		success.Should().BeTrue(because: "the gate returned a valid JSON file list");
		response.Files.Should().Equal(new[] {"src/Foo.cs", "src/foo.cs"},
			because: "case-variant paths need a stable ordinal tie-breaker independent of filesystem enumeration order");
	}

	[Test]
	[Description("Deserializes escaped source exactly and includes the generated package project in the structured result.")]
	public void TryGetPackageFile_ShouldReturnExactContentAndProject_WhenGateReturnsJsonStrings() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode", FilePath = "src/cs/Probe.cs" };
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("filePath=src%2Fcs%2FProbe.cs")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"line 1\\r\\n\\t\\\"quoted\\\" \\u263A\"");
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("filePath=UsrCode.csproj")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"<Project>\\n  <ItemGroup />\\n</Project>\"");

		// Act
		bool success = _command.TryGetPackageFile(options, out PackageFileContentResponse response);

		// Assert
		success.Should().BeTrue(because: "both requested files were returned as valid JSON strings");
		response.Content.Should().Be("line 1\r\n\t\"quoted\" ☺",
			because: "JSON escape processing must preserve the exact source content");
		response.ProjectFilePath.Should().Be("UsrCode.csproj",
			because: "the generated project path is deterministic for the package");
		response.ProjectContent.Should().Contain("<Project>",
			because: "the structured tool response must include the generated project content");
	}

	[Test]
	[Description("Passes the caller's timeout and retry settings to every ClioGate package file request.")]
	public void TryGetPackageFile_ShouldPropagateRequestSettings_ToEveryGateRead() {
		// Arrange
		ShowPackageFileContentOptions options = new() {
			PackageName = "UsrCode",
			FilePath = "src/cs/Probe.cs",
			TimeOut = 12_345,
			MaxAttempts = 4,
			RetryDelay = 7
		};
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"content\"");

		// Act
		bool success = _command.TryGetPackageFile(options, out _);

		// Assert
		success.Should().BeTrue(because: "both ClioGate reads returned valid JSON strings");
		_applicationClient.Received(2).ExecuteGetRequest(
			Arg.Any<string>(), 12_345, 4, 7);
	}

	[Test]
	[Description("Keeps a successful source read when the companion generated project is unavailable.")]
	public void TryGetPackageFile_ShouldReturnPrimaryContent_WhenProjectIsUnavailable() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode", FilePath = "src/cs/Probe.cs" };
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("filePath=src%2Fcs%2FProbe.cs")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("\"class Probe {}\"");
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("filePath=UsrCode.csproj")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html>file not found</html>");

		// Act
		bool success = _command.TryGetPackageFile(options, out PackageFileContentResponse response);

		// Assert
		success.Should().BeTrue(because: "the requested source file was read successfully");
		response.Content.Should().Be("class Probe {}", because: "project enrichment must not discard primary content");
		response.ProjectContent.Should().BeNull(because: "the generated project could not be read");
		response.ProjectError.Should().Contain("UsrCode.csproj",
			because: "the caller needs a non-fatal explanation for the missing project content");
	}

	[Test]
	[Description("Rejects traversal before calling ClioGate so a package file read cannot escape the package Files directory.")]
	public void TryGetPackageFile_ShouldRejectTraversal_BeforeCallingGate() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode", FilePath = "../web.config" };

		// Act
		bool success = _command.TryGetPackageFile(options, out PackageFileContentResponse response);

		// Assert
		success.Should().BeFalse(because: "parent traversal is never a valid package-relative path");
		response.Error.Should().Contain("inside the package Files directory",
			because: "the operator needs an actionable validation error");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default, default, default, default);
	}

	[Test]
	[Description("Rejects rooted paths before calling ClioGate so reads remain package-relative on every platform.")]
	public void TryGetPackageFile_ShouldRejectRootedPath_BeforeCallingGate() {
		// Arrange
		string rootedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outside.cs"));
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode", FilePath = rootedPath };

		// Act
		bool success = _command.TryGetPackageFile(options, out PackageFileContentResponse response);

		// Assert
		success.Should().BeFalse(because: "an absolute path is never package-relative");
		response.Error.Should().Contain("relative",
			because: "the caller needs to know that only package-relative paths are accepted");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default, default, default, default);
	}

	[Test]
	[Description("Rejects Windows drive-prefixed paths even when clio runs on a non-Windows host.")]
	public void TryGetPackageFile_ShouldRejectWindowsDrivePath_OnEveryPlatform() {
		// Arrange
		ShowPackageFileContentOptions options = new() {
			PackageName = "UsrCode", FilePath = "C:/Windows/outside.cs"
		};

		// Act
		bool success = _command.TryGetPackageFile(options, out PackageFileContentResponse response);

		// Assert
		success.Should().BeFalse(because: "a Windows drive path is never package-relative on any client host");
		response.Error.Should().Contain("relative",
			because: "the caller needs the same portable package-relative path rule");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default, default, default, default);
	}

	[Test]
	[Description("Classifies an HTML ClioGate error page as an invalid response instead of exposing markup as source content.")]
	public void TryListPackageFiles_ShouldReturnActionableFailure_WhenGateReturnsHtml() {
		// Arrange
		ShowPackageFileContentOptions options = new() { PackageName = "UsrCode" };
		_applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("<html>route not found</html>");

		// Act
		bool success = _command.TryListPackageFiles(options, out PackageFileListResponse response);

		// Assert
		success.Should().BeFalse(because: "HTML is not the ClioGate JSON contract");
		response.Error.Should().Contain("install", because: "the error should point to the cliogate remediation");
		response.Error.Should().Contain("Error.log", because: "the error should identify the server-side diagnostic source");
	}
}
