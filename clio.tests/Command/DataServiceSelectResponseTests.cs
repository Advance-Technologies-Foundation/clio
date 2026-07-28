namespace Clio.Tests.Command;

using System;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class DataServiceSelectResponseTests {

	[Test]
	[Description("A successful SelectQuery response returns every row to the caller.")]
	public void ReadRows_Should_Return_Rows_When_Response_Succeeds() {
		// Arrange
		string json = """{"success":true,"rows":[{"Name":"A"},{"Name":"B"}]}""";

		// Act
		JArray rows = DataServiceSelectResponse.ReadRows(json);

		// Assert
		rows.Should().HaveCount(2, because: "both returned rows must be surfaced, not dropped");
	}

	[Test]
	[Description("A genuinely empty but successful result returns an empty array rather than throwing.")]
	public void ReadRows_Should_Return_Empty_When_Result_Is_Empty_And_Successful() {
		// Arrange
		string json = """{"success":true,"rows":[]}""";

		// Act
		JArray rows = DataServiceSelectResponse.ReadRows(json);

		// Assert
		rows.Should().BeEmpty(because: "an empty successful result is a valid outcome, not an error");
	}

	[Test]
	[Description("A DataService failure envelope (success:false + errorInfo) throws instead of being read as an empty success.")]
	public void ReadRows_Should_Throw_When_Response_Is_Failure_Envelope() {
		// Arrange - restricted SysSchema access returns HTTP 200 with this shape and no rows
		string json = """{"success":false,"errorInfo":{"errorCode":"AccessDenied","message":"Access to SysSchema is denied"}}""";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a failure envelope must surface as an error, not a silent empty result")
			.WithMessage("*Access to SysSchema is denied*");
	}

	[Test]
	[Description("A successful envelope carrying the nullable errorInfo field (errorInfo:null) returns rows, not a failure.")]
	public void ReadRows_Should_Return_Rows_When_Successful_Envelope_Carries_Null_ErrorInfo() {
		// Arrange - a common Creatio success shape carries the nullable envelope field as errorInfo:null; in
		// Newtonsoft this parses to a JValue of type Null (NOT C# null), which must not be read as a failure.
		string json = """{"success":true,"errorInfo":null,"rows":[{"Name":"A"}]}""";

		// Act
		JArray rows = DataServiceSelectResponse.ReadRows(json);

		// Assert
		rows.Should().HaveCount(1, because: "errorInfo:null is a success shape, not a failure signal, so the rows must be returned");
	}

	[Test]
	[Description("A failure envelope with errorInfo:null still throws via success:false without an opaque JValue-indexing error.")]
	public void ReadRows_Should_Throw_Cleanly_When_Failure_Envelope_Has_Null_ErrorInfo() {
		// Arrange - success:false wins the failure gate; reading errorInfo["message"] must not throw a
		// JValue-indexing exception when errorInfo is JSON null, and must fall back to the responseStatus message.
		string json = """{"success":false,"errorInfo":null,"responseStatus":{"Message":"denied"}}""";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "success:false is a failure regardless of the null errorInfo")
			.WithMessage("*denied*");
	}

	[Test]
	[Description("An errorInfo object WITHOUT success:false still throws so a permission/service failure is not read as zero rows.")]
	public void ReadRows_Should_Throw_When_ErrorInfo_Object_Present_Without_Success_False() {
		// Arrange - a restricted SysSchema read can return HTTP 200 with an errorInfo object and no explicit
		// success:false; the failure gate must key off the errorInfo object too, not success alone.
		string json = """{"errorInfo":{"errorCode":"AccessDenied","message":"Access to SysSchema is denied"}}""";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an errorInfo object is a failure signal on its own")
			.WithMessage("*Access to SysSchema is denied*");
	}

	[Test]
	[Description("TryGetFailure reports no failure for a success:null token instead of throwing on the non-nullable read.")]
	public void TryGetFailure_Should_Not_Throw_When_Success_Token_Is_Null() {
		// Arrange - a "success": null token parses to a Newtonsoft JValue-Null; the nullable read must not throw.
		JObject parsed = JObject.Parse("""{"success":null,"rows":[]}""");

		// Act
		bool isFailure = DataServiceSelectResponse.TryGetFailure(parsed, out string message);

		// Assert
		isFailure.Should().BeFalse(because: "a null success token is not a failure signal and must not throw");
		message.Should().BeNull(because: "no failure means no reason string");
	}

	[Test]
	[Description("A responseStatus error envelope also throws so the real error is surfaced, not read as zero rows.")]
	public void ReadRows_Should_Throw_When_ResponseStatus_Carries_ErrorCode() {
		// Arrange
		string json = """{"responseStatus":{"ErrorCode":"ServiceError","Message":"boom"}}""";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a responseStatus error must not be read as zero rows")
			.WithMessage("*boom*");
	}

	[Test]
	[Description("A success envelope carrying an EMPTY errorInfo object (errorInfo:{}) is not a failure — an empty error object must not turn a genuine success into a hard error.")]
	public void ReadRows_Should_Return_Rows_When_ErrorInfo_Object_Is_Empty() {
		// Arrange - an empty error object alongside rows is a success shape, not a failure signal
		string json = """{"success":true,"errorInfo":{},"rows":[{"Name":"A"}]}""";

		// Act
		JArray rows = DataServiceSelectResponse.ReadRows(json);

		// Assert
		rows.Should().HaveCount(1,
			because: "an empty errorInfo object is not a failure signal, so the rows must be returned");
	}

	[Test]
	[Description("A signal-less response with no rows token at all throws instead of being read as an empty success, so the migration tools never silently report zero schemas to migrate.")]
	public void ReadRows_Should_Throw_When_No_Rows_And_No_Failure_Signal() {
		// Arrange - a truncated / atypical 200 body: no rows, no success:false, no errorInfo, no responseStatus
		string json = "{}";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a body with no rows and no explicit success signal is not a trustworthy empty result");
	}

	[Test]
	[Description("An explicit rows:null token (a JValue-Null, not C# null) throws rather than being read as an empty success, so a malformed/atypical body is never mistaken for zero schemas to migrate.")]
	public void ReadRows_Should_Throw_When_Rows_Token_Is_Json_Null() {
		// Arrange - "rows": null parses to a JValue-Null, distinct from an absent token and from an empty array
		string json = """{"rows":null}""";

		// Act
		Action act = () => DataServiceSelectResponse.ReadRows(json);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a JSON null rows token is not a trustworthy empty result and must not be read as an empty array");
	}
}
