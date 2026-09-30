using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common.ObjectRights;

/// <summary>
/// The rules the object-rights commands share: the security/system name list the connected listing leaves out, the
/// one-line rendering of names, rows and service messages, and which exceptions are failures of the service call —
/// including the AggregateException Creatio's client wraps every transport fault in.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class ObjectRightsSupportTests {

	private static readonly Guid Role = Guid.Parse("197c2267-236b-4dac-97ec-13e4f34aa38d");

	[TestCase("SysAdminUnit", true)]
	[TestCase("sysuserinrole", true)]
	[TestCase("UsrOrderRights", true)]
	[TestCase("UsrOrder", false)]
	[TestCase("Contact", false)]
	[Description("The guard matches a family prefix or suffix in any case, and nothing else.")]
	public void IsSecurityOrSystemObject_ShouldMatchTheFamilies_WhenGivenAName(string schemaName, bool expected) {
		// Act
		bool actual = ObjectRightsSupport.IsSecurityOrSystemObject(schemaName);

		// Assert
		actual.Should().Be(expected, because: $"'{schemaName}' {(expected ? "is" : "is not")} a security or system object");
	}

	[Test]
	[Description("A service message with line breaks is rendered on one line, so it cannot start a line of its own inside a result.")]
	public void DisplayError_ShouldKeepTheMessageOnOneLine_WhenItHasLineBreaks() {
		// Act
		string rendered = ObjectRightsSupport.DisplayError("Violation of PRIMARY KEY\r\nThe statement has been terminated.");

		// Assert
		rendered.Should().NotContain("\n", because: "a line break would start a new output line");
		rendered.Should().Contain("The statement has been terminated.", because: "the message itself is kept");
	}

	[Test]
	[Description("A service message is redacted before it is printed: the CLI and the log have no redaction pass of their own.")]
	public void DisplayError_ShouldRedactTheHost_WhenTheMessageCarriesARequestUri() {
		// Act
		string rendered = ObjectRightsSupport.DisplayError(
			"Unexpected response from https://tenant.example/0/ServiceModel/RightManagementService.svc/Save");

		// Assert
		rendered.Should().NotContain("tenant.example", because: "a request URI names the customer's host");
		rendered.Should().StartWith("Unexpected response from", because: "the readable part of the message is kept");
	}

	[Test]
	[Description("A wrapper around one fault is rendered by that fault: the AggregateException Creatio's client throws says only 'One or more errors occurred'.")]
	public void DisplayError_ShouldRenderTheWrappedFault_WhenTheExceptionIsAnAggregateOfOne() {
		// Arrange
		Exception wrapped = new AggregateException(new TaskCanceledException("The request timed out."));

		// Act
		string rendered = ObjectRightsSupport.DisplayError(wrapped);

		// Assert
		rendered.Should().Be("The request timed out.", because: "the wrapper's own message names nothing");
	}

	private static IEnumerable<TestCaseData> Failures() {
		yield return new TestCaseData(new TaskCanceledException("t"), true, true)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheExceptionIsABareCancellation");
		yield return new TestCaseData(new HttpRequestException("503"), true, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheExceptionIsABareTransportFault");
		yield return new TestCaseData(new AggregateException(new TaskCanceledException("t")), true, true)
			.SetName("IsServiceFailure_ShouldClassify_WhenATimeoutArrivesWrapped");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503")), true, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenATransportFaultArrivesWrapped");
		yield return new TestCaseData(new AggregateException(new AggregateException(new TaskCanceledException("t"))), true,
				true)
			.SetName("IsServiceFailure_ShouldClassify_WhenATimeoutArrivesWrappedTwice");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503"), new NullReferenceException()),
				false, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenAWrapperAlsoCarriesAProgrammingError");
		yield return new TestCaseData(new AggregateException(new NullReferenceException()), false, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenAProgrammingErrorArrivesWrapped");
		yield return new TestCaseData(new WebException("login timed out", WebExceptionStatus.Timeout), true, true)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheLoginStepTimesOut");
		yield return new TestCaseData(new WebException("refused", WebExceptionStatus.ConnectFailure), true, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheConnectionIsRefused");
		yield return new TestCaseData(new IOException("reset"), true, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheConnectionIsReset");
		yield return new TestCaseData(new ArgumentException("bug"), false, false)
			.SetName("IsServiceFailure_ShouldClassify_WhenTheExceptionIsAProgrammingError");
	}

	[TestCaseSource(nameof(Failures))]
	[Description("A fault of the service call — bare, or wrapped in the AggregateException Creatio's client throws through Task.Result — is a service failure, and a hang is a timeout; a programming error is neither, even wrapped.")]
	public void IsServiceFailure_ShouldClassify_WhenGivenAnException(Exception exception, bool serviceFailure,
		bool timeout) {
		// Act
		bool actualServiceFailure = ObjectRightsSupport.IsServiceFailure(exception);
		bool actualTimeout = ObjectRightsSupport.IsTimeout(exception);

		// Assert
		actualServiceFailure.Should().Be(serviceFailure,
			because: "a failure of the service call is attributed to the object, a programming error is not");
		actualTimeout.Should().Be(timeout, because: "only a hang stops the probe loop and the connected listing");
	}

	[Test]
	[Description("A row is rendered as its position, the grantee's name — with its id when asked for — and its operations in grid order; a row with none says so.")]
	public void FormatRow_ShouldRenderPositionNameAndOperations_WhenGivenARow() {
		// Arrange
		RoleOperationRights row = new(Role, "Sales managers", 1, true, true, false, false);
		RoleOperationRights empty = new(Role, "Sales managers", 2, false, false, false, false);

		// Act
		string plain = ObjectRightsSupport.FormatRow(row);
		string withId = ObjectRightsSupport.FormatRow(row, withGranteeId: true);
		string none = ObjectRightsSupport.FormatRow(empty);

		// Assert
		plain.Should().Be("[1] Sales managers: read/create", because: "set-object-rights renders a row without the id");
		withId.Should().Be($"[1] Sales managers ({Role}): read/create", because: "get-object-rights adds the id");
		none.Should().Be("[2] Sales managers: no operations", because: "a row with no operations is a deny, and says so");
	}

	[Test]
	[Description("Rows are rendered in priority order whatever order they come in, and an empty set reads 'none'.")]
	public void FormatRows_ShouldRenderInPriorityOrder_WhenGivenRows() {
		// Arrange
		RoleOperationRights second = new(Role, "B", 1, true, false, false, false);
		RoleOperationRights first = new(Role, "A", 0, true, true, true, true);

		// Act
		string rendered = ObjectRightsSupport.FormatRows(new[] { second, first });
		string empty = ObjectRightsSupport.FormatRows(Array.Empty<RoleOperationRights>());

		// Assert
		rendered.Should().Be("[0] A: read/create/edit/delete; [1] B: read", because: "position 0 decides first");
		empty.Should().Be("none", because: "an object can have no rows at all");
	}

	[Test]
	[Description("The operations a request names are spelled like a row's, in the order the request names them.")]
	public void FormatOperations_ShouldSpellRequestedOperationsLikeARow_WhenGivenTheRequestList() {
		// Act
		string rendered = ObjectRightsSupport.FormatOperations(new[] { ObjectOperation.Edit, ObjectOperation.Read });

		// Assert
		rendered.Should().Be("edit/read", because: "a request is echoed as it was given, in the row's lower-case spelling");
	}
}
