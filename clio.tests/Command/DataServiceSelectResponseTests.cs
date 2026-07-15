namespace Clio.Tests.Command;

using System;
using Clio.Command;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
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
}
