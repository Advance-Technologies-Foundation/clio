namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class GetRelatedPageAddonCommandTests {
	private IRelatedPageAddonService _service = null!;
	private ILogger _logger = null!;
	private GetRelatedPageAddonCommand _command = null!;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IRelatedPageAddonService>();
		_logger = Substitute.For<ILogger>();
		_command = new GetRelatedPageAddonCommand(_service, _logger);
	}

	private static GetRelatedPageAddonOptions Options() =>
		new() { EntitySchemaName = "UsrDeliveryItem", PackageName = "Custom" };

	private static RelatedPageAddonReadResult SampleResult() =>
		new("UsrDeliveryItem", "bb000000-0000-0000-0000-000000000002", "Custom",
			"aa000000-0000-0000-0000-000000000001", "RelatedPage", null, 1,
			new[] {
				new RelatedPageEntry("cc000000-0000-0000-0000-00000000000a", "UsrDeliveryItemFormPage",
					true, false, false, null, null, null)
			});

	[Test]
	[Description("TryGet maps the service read result into the response when the service succeeds.")]
	public void TryGet_ShouldMapResultIntoResponse_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(SampleResult());

		// Act
		bool ok = _command.TryGet(Options(), out GetRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeTrue(because: "a successful read returns true");
		response.Success.Should().BeTrue(because: "the response reflects the successful read");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the object name is carried into the response");
		response.PageCount.Should().Be(1,
			because: "the decoded page count is carried into the response");
		response.Pages.Should().ContainSingle(page => page.PageSchemaName == "UsrDeliveryItemFormPage",
			because: "the decoded entry is mapped into the response DTO");
	}

	[Test]
	[Description("Execute returns exit code 0 and logs the serialized response when the service succeeds.")]
	public void Execute_ShouldReturnZero_WhenServiceSucceeds() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(SampleResult());

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "a successful read maps to exit code 0 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("UsrDeliveryItemFormPage")));
	}

	[Test]
	[Description("Execute returns exit code 1 and reports the error when the service throws.")]
	public void Execute_ShouldReturnOne_WhenServiceThrows() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>())
			.Returns(_ => throw new InvalidOperationException("boom"));

		// Act
		int exitCode = _command.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "a failed read maps to exit code 1 for the shell/CI");
		_logger.Received().WriteInfo(Arg.Is<string>(message => message.Contains("boom")));
	}

	[Test]
	[Description("TryGet maps EVERY envelope and per-page field into the response with no positional swap, mirroring the anti-swap coverage on the create side.")]
	public void TryGet_ShouldMapEveryField_WhenServiceSucceeds() {
		// Arrange — a fully populated result with DISTINCT values per field so any field swap is caught: an untyped
		// employee default plus a portal, typed, ssp-default add page.
		RelatedPageAddonReadResult result = new(
			"UsrDeliveryItem", "bb000000-0000-0000-0000-000000000002", "Custom",
			"aa000000-0000-0000-0000-000000000001", "RelatedPage",
			"dd000000-0000-0000-0000-000000000003", 2,
			new[] {
				new RelatedPageEntry("cc000000-0000-0000-0000-00000000000a", "UsrDeliveryItemFormPage",
					true, false, false, null, null, null),
				new RelatedPageEntry("cc000000-0000-0000-0000-00000000000b", "UsrDeliveryItemPortalAddPage",
					false, true, true, "720b771c-e7a7-4f31-9cfb-52cd21c3739f", "All external users",
					"1b0bc159-150a-e111-a31b-00155d04c01d")
			});
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>()).Returns(result);

		// Act
		bool ok = _command.TryGet(Options(), out GetRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeTrue(because: "a successful read returns true");
		response.Success.Should().BeTrue(because: "the response reflects the successful read");
		response.EntitySchemaUId.Should().Be("bb000000-0000-0000-0000-000000000002",
			because: "the object UId is carried into the response, not swapped with the package UId");
		response.PackageName.Should().Be("Custom", because: "the package name is carried through verbatim");
		response.PackageUId.Should().Be("aa000000-0000-0000-0000-000000000001",
			because: "the package UId is carried into the response, not swapped with the object UId");
		response.AddonName.Should().Be("RelatedPage", because: "the add-on name is carried through");
		response.TypeColumnUId.Should().Be("dd000000-0000-0000-0000-000000000003",
			because: "the top-level type column UId is carried into the response");
		response.PageCount.Should().Be(2, because: "the decoded page count is carried through");
		RelatedPageEntryDto portal = response.Pages[1];
		portal.PageSchemaUId.Should().Be("cc000000-0000-0000-0000-00000000000b",
			because: "the raw page UId maps into the entry DTO");
		portal.PageSchemaName.Should().Be("UsrDeliveryItemPortalAddPage",
			because: "the resolved page name maps into the entry DTO");
		portal.IsDefault.Should().BeFalse(because: "the add entry is not a default");
		portal.IsAdd.Should().BeTrue(because: "the is-add flag maps into the entry DTO");
		portal.IsSspDefault.Should().BeTrue(because: "the is-ssp-default flag maps into the entry DTO");
		portal.Role.Should().Be("720b771c-e7a7-4f31-9cfb-52cd21c3739f",
			because: "the raw role UId maps into the entry DTO");
		portal.RoleName.Should().Be("All external users",
			because: "the resolved role name maps into the entry DTO, not swapped with the raw UId");
		portal.TypeColumnValue.Should().Be("1b0bc159-150a-e111-a31b-00155d04c01d",
			because: "the type-column value maps into the entry DTO");
	}

	[Test]
	[Description("TryGet fails with a clear error and never calls the service when options is null.")]
	public void TryGet_ShouldFail_WhenOptionsNull() {
		// Act
		bool ok = _command.TryGet(null, out GetRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(because: "a null options object cannot be read");
		response.Success.Should().BeFalse(because: "the failure envelope reports no success");
		response.Error.Should().Contain("options is required",
			because: "the null-options guard must surface a clear error");
		_service.DidNotReceiveWithAnyArgs().Get(default!);
	}

	[Test]
	[Description("TryGet catches a service throw and returns a failed response (Fail branch) instead of propagating the exception.")]
	public void TryGet_ShouldReturnFailedResponse_WhenServiceThrows() {
		// Arrange
		_service.Get(Arg.Any<RelatedPageAddonReadRequest>())
			.Returns(_ => throw new InvalidOperationException("object not found"));

		// Act
		bool ok = _command.TryGet(Options(), out GetRelatedPageAddonResponse response);

		// Assert
		ok.Should().BeFalse(because: "a service failure is reported as a failed read, not a thrown exception");
		response.Success.Should().BeFalse(because: "the failure envelope reports no success");
		response.Error.Should().Contain("object not found",
			because: "the catch branch must surface the service's error message to the caller");
	}
}
