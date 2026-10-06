using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
	[Description("A wrapper around one fault is rendered by that fault alone: the AggregateException Creatio's client throws puts a generic 'One or more errors occurred.' before it.")]
	public void DisplayFailure_ShouldRenderTheWrappedFault_WhenTheExceptionIsAnAggregateOfOne() {
		// Arrange
		Exception wrapped = new AggregateException(new AggregateException(new TaskCanceledException("The request timed out.")));

		// Act
		string rendered = ObjectRightsSupport.DisplayFailure(wrapped);

		// Assert
		rendered.Should().Be("The request timed out.", because: "the wrapper's generic prefix names nothing");
	}

	[Test]
	[Description("A wrapper around several faults keeps its own message, which lists them all, and a missing exception renders as nothing.")]
	public void DisplayFailure_ShouldKeepEveryFault_WhenTheWrapperHoldsSeveral() {
		// Arrange
		Exception wrapped = new AggregateException(new HttpRequestException("first fault"), new IOException("second fault"));

		// Act
		string rendered = ObjectRightsSupport.DisplayFailure(wrapped);
		string missing = ObjectRightsSupport.DisplayFailure(null);

		// Assert
		rendered.Should().Contain("first fault", because: "no fault of several is dropped");
		rendered.Should().Contain("second fault", because: "no fault of several is dropped");
		missing.Should().BeEmpty(because: "there is nothing to render");
	}

	[Test]
	[Description("The wrapped fault's own text goes through the same redaction and one-line rendering as any service message.")]
	public void DisplayFailure_ShouldRedactAndFlattenTheWrappedFault_WhenItCarriesACredentialedUri() {
		// Arrange
		Exception wrapped = new AggregateException(new HttpRequestException(
			"Request to https://admin:s3cr3t@tenant.example/0/ServiceModel/RightManagementService.svc failed\r\nretry"));

		// Act
		string rendered = ObjectRightsSupport.DisplayFailure(wrapped);

		// Assert
		rendered.Should().NotContain("s3cr3t", because: "a credential in the fault is redacted");
		rendered.Should().NotContain("tenant.example", because: "a request URI names the customer's host");
		rendered.Should().NotContain("\n", because: "a line break would start a new output line");
		rendered.Should().StartWith("Request to", because: "the readable part of the fault is kept");
	}

	private static IEnumerable<TestCaseData> ServiceFailures() {
		yield return new TestCaseData(new TaskCanceledException("t"), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenTheExceptionIsABareCancellation");
		yield return new TestCaseData(new HttpRequestException("503"), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenTheExceptionIsABareTransportFault");
		yield return new TestCaseData(new AggregateException(new TaskCanceledException("t")), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenATimeoutArrivesWrapped");
		yield return new TestCaseData(new AggregateException(new AggregateException(new HttpRequestException("503"))), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenATransportFaultArrivesWrappedTwice");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503"), new TaskCanceledException("t")),
				true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenEveryWrappedFaultIsAServiceFailure");
		yield return new TestCaseData(new AggregateException(new TaskCanceledException("t"), new NullReferenceException()),
				false)
			.SetName("IsServiceFailure_ShouldBeFalse_WhenAWrapperAlsoCarriesAProgrammingError");
		yield return new TestCaseData(new AggregateException(new NullReferenceException()), false)
			.SetName("IsServiceFailure_ShouldBeFalse_WhenAProgrammingErrorArrivesWrapped");
		yield return new TestCaseData(new AggregateException(), false)
			.SetName("IsServiceFailure_ShouldBeFalse_WhenTheWrapperIsEmpty");
		yield return new TestCaseData(new WebException("refused", WebExceptionStatus.ConnectFailure), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenTheConnectionIsRefused");
		yield return new TestCaseData(new IOException("reset"), true)
			.SetName("IsServiceFailure_ShouldBeTrue_WhenTheConnectionIsReset");
		yield return new TestCaseData(new ArgumentException("bug"), false)
			.SetName("IsServiceFailure_ShouldBeFalse_WhenTheExceptionIsAProgrammingError");
	}

	[TestCaseSource(nameof(ServiceFailures))]
	[Description("A fault of the service call — bare, or wrapped in the AggregateException Creatio's client throws through Task.Result — is a service failure when every fault in it is one; a programming error is not, even wrapped.")]
	public void IsServiceFailure_ShouldClassifyTheFault_WhenGivenAnException(Exception exception, bool expected) {
		// Act
		bool actual = ObjectRightsSupport.IsServiceFailure(exception);

		// Assert
		actual.Should().Be(expected,
			because: "a failure of the service call is attributed to the object, a programming error is not");
	}

	private static IEnumerable<TestCaseData> Timeouts() {
		yield return new TestCaseData(new TaskCanceledException("t"), true)
			.SetName("IsTimeout_ShouldBeTrue_WhenTheExceptionIsABareCancellation");
		yield return new TestCaseData(new AggregateException(new TaskCanceledException("t")), true)
			.SetName("IsTimeout_ShouldBeTrue_WhenATimeoutArrivesWrapped");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503"), new TaskCanceledException("t")),
				true)
			.SetName("IsTimeout_ShouldBeTrue_WhenAnyWrappedFaultIsATimeout");
		yield return new TestCaseData(new AggregateException(new HttpRequestException("503")), false)
			.SetName("IsTimeout_ShouldBeFalse_WhenTheWrappedFaultWasAnswered");
		yield return new TestCaseData(new WebException("login timed out", WebExceptionStatus.Timeout), true)
			.SetName("IsTimeout_ShouldBeTrue_WhenTheLoginStepTimesOut");
		yield return new TestCaseData(new WebException("refused", WebExceptionStatus.ConnectFailure), false)
			.SetName("IsTimeout_ShouldBeFalse_WhenTheConnectionIsRefused");
		yield return new TestCaseData(new HttpRequestException("connect",
				new SocketException((int)SocketError.TimedOut)), true)
			.SetName("IsTimeout_ShouldBeTrue_WhenTheTcpConnectTimesOut");
		yield return new TestCaseData(new HttpRequestException("connect",
				new SocketException((int)SocketError.ConnectionRefused)), false)
			.SetName("IsTimeout_ShouldBeFalse_WhenTheTcpConnectIsRefused");
		yield return new TestCaseData(new AggregateException(), false)
			.SetName("IsTimeout_ShouldBeFalse_WhenTheWrapperIsEmpty");
	}

	[TestCaseSource(nameof(Timeouts))]
	[Description("A hang — a cancellation, a login step or a TCP connect that timed out, bare or wrapped — is a timeout, so the probe loop and the connected listing stop on it; a fault the server answered is not.")]
	public void IsTimeout_ShouldClassifyTheFault_WhenGivenAnException(Exception exception, bool expected) {
		// Act
		bool actual = ObjectRightsSupport.IsTimeout(exception);

		// Assert
		actual.Should().Be(expected, because: "only a hang stops the probe loop and the connected listing");
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
	[Description("The operations a request names are spelled exactly like a row's, in the order the request names them.")]
	public void FormatOperations_ShouldSpellRequestedOperationsLikeARow_WhenGivenTheRequestList() {
		// Arrange
		ObjectOperation[] every = { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete };
		RoleOperationRights full = new(Role, "Sales managers", 0, true, true, true, true);

		// Act
		string requested = ObjectRightsSupport.FormatOperations(every);
		string row = ObjectRightsSupport.FormatOperations(full);
		string reordered = ObjectRightsSupport.FormatOperations(new[] { ObjectOperation.Edit, ObjectOperation.Read });

		// Assert
		requested.Should().Be(row, because: "a request and a row name an operation the same way");
		reordered.Should().Be("edit/read", because: "a request is echoed in the order it was given");
	}

	[TestCase("Feature", "Creatio functionality", "'Creatio functionality' (Feature)")]
	[TestCase("Feature", "  Creatio functionality ", "'Creatio functionality' (Feature)")]
	[TestCase("Contact", "Contact", "'Contact'")]
	[TestCase("Contact", "contact", "'Contact'")]
	[TestCase("UsrFoo", null, "'UsrFoo'")]
	[TestCase("UsrFoo", " ", "'UsrFoo'")]
	[Description("An object is named by its title and its code when it has a title of its own, and by its code alone when its title is the code or the service returned none.")]
	public void FormatObject_ShouldNameTheObjectByTitleAndCodeOrByCodeAlone_WhenGivenItsTitle(string schemaName,
		string caption, string expected) {
		// Act
		string rendered = ObjectRightsSupport.FormatObject(schemaName, caption);

		// Assert
		rendered.Should().Be(expected, because: "the developer sees which object the code names");
	}

	[Test]
	[Description("A title is rendered on one line, so it cannot start a line that reads like the command's own output.")]
	public void FormatObject_ShouldKeepTheTitleOnOneLine_WhenItHasALineBreak() {
		// Act
		string rendered = ObjectRightsSupport.FormatObject("UsrOrder", "Order\nError: forged");

		// Assert
		rendered.Should().NotContain("\n", because: "a server-supplied title is rendered on one line");
		rendered.Should().EndWith("(UsrOrder)", because: "the code still follows the title");
	}

	[Test]
	[Description("A title cannot close its quotes early: its own apostrophes are rendered as typographic ones, so a title such as \"Orders' (UsrOrder)\" cannot show another object's code where the real one belongs, in the object's name or in a list of candidates.")]
	public void FormatObject_ShouldNotLetATitleCloseItsQuotes_WhenTheTitleHasAnApostrophe() {
		// Arrange
		const string forged = "Orders' (UsrOrder)";

		// Act
		string named = ObjectRightsSupport.FormatObject("SysAdminUnit", forged);
		string listed = ObjectRightsSupport.FormatTitleMatches(new[] { new ObjectTitleMatch("SysAdminUnit", forged) });

		// Assert
		named.Should().Be("'Orders’ (UsrOrder)' (SysAdminUnit)", because: "the only plain quotes are the ones around the title");
		listed.Should().Be("'Orders’ (UsrOrder)' (code: SysAdminUnit)", because: "a candidate list renders a title the same way");
	}

	[Test]
	[Description("A long title is shortened, so it cannot carry a whole made-up clause into the output.")]
	public void FormatObject_ShouldShortenTheTitle_WhenItIsLong() {
		// Act
		string named = ObjectRightsSupport.FormatObject("UsrOrder", new string('x', 300));

		// Assert
		named.Length.Should().BeLessThan(120, because: "a title is capped well below the general display cap");
		named.Should().EndWith("(UsrOrder)", because: "the code still follows the title");
	}

	[Test]
	[Description("The objects a title names are listed as 'title' (code: name), like the process tools list the candidates of a caption, and a long list names five and counts the rest.")]
	public void FormatTitleMatches_ShouldNameFiveAndCountTheRest_WhenATitleNamesMany() {
		// Arrange
		ObjectTitleMatch[] two = { new("Specification", "Feature"), new("UsrFeature", "Feature") };
		ObjectTitleMatch[] seven = Enumerable.Range(1, 7).Select(index => new ObjectTitleMatch($"Usr{index}", "Feature"))
			.ToArray();

		// Act
		string few = ObjectRightsSupport.FormatTitleMatches(two);
		string many = ObjectRightsSupport.FormatTitleMatches(seven);

		// Assert
		few.Should().Be("'Feature' (code: Specification); 'Feature' (code: UsrFeature)",
			because: "each candidate shows its title and its code");
		many.Should().EndWith("'Feature' (code: Usr5); and 2 more", because: "a long list stays bounded and says how many it left out");
	}
}
