using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.RecordRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public class CreatioRightsClientTests {

	private static (CreatioRightsClient client, IApplicationClient applicationClient) CreateClient(bool isNetCore = false) {
		// Real ServiceUrlBuilder so the route mapping + framework prefix are exercised; only the I/O boundary is faked.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = new ServiceUrlBuilder(new EnvironmentSettings {
			Uri = "http://localhost",
			IsNetCore = isNetCore
		});
		return (new CreatioRightsClient(applicationClient, urlBuilder), applicationClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the operation name to the WebApp-prefixed RightsService REST path and returns true when the result is true.")]
	public void GetCanExecuteOperation_ShouldPostOperationAndReturnTrue_WhenPermitted() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient(isNetCore: false);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetCanExecuteOperationResult\":true}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeTrue(because: "GetCanExecuteOperationResult=true means the operation is permitted");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/rest/RightsService/GetCanExecuteOperation",
			"{\"operation\":\"CanManageThemes\"}", 100_000, 3, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when RightsService reports GetCanExecuteOperationResult=false.")]
	public void GetCanExecuteOperation_ShouldReturnFalse_WhenDenied() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetCanExecuteOperationResult\":false}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeFalse(because: "a false result means the operation is denied");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns false when the response omits the result property (defensive default).")]
	public void GetCanExecuteOperation_ShouldReturnFalse_WhenResultMissing() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");

		// Act
		bool granted = client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		granted.Should().BeFalse(because: "a missing result property must default to not-permitted");
	}

	[Test]
	[Category("Unit")]
	[Description("Caps the response-body excerpt echoed into the diagnostic, so a multi-kilobyte HTML error page does not flood the error message.")]
	public void GetCanExecuteOperation_ShouldTruncateEchoedBody_WhenNonJsonResponseIsLarge() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		string largeBody = new string('x', 5000);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(largeBody);

		// Act
		Action act = () => client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body is a transport-level failure")
			.Which.Message.Length.Should().BeLessThan(700,
				because: "the echoed body excerpt is capped at 500 characters plus the diagnostic prefix");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws a diagnostic InvalidOperationException when the RightsService response is a non-JSON body (e.g. an auth redirect).")]
	public void GetCanExecuteOperation_ShouldThrow_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		Action act = () => client.GetCanExecuteOperation("CanManageThemes", new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body signals the request never reached RightsService")
			.WithMessage("*RightsService*");
	}

	private const string CapturedGetRecordRightsResponse =
		"{\"GetRecordRightsResult\":[{\"Id\":\"5e7e4100-fc17-4cc2-9200-f91b1ceab8ea\",\"Operation\":0," +
		"\"Position\":0,\"RightLevel\":2,\"SysAdminUnit\":{\"displayValue\":\"Supervisor\"," +
		"\"value\":\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"}," +
		"\"SysAdminUnitType\":\"472e97c7-6bd7-df11-9b2a-001d60e938c6\",\"isDeleted\":false,\"isNew\":false}," +
		"{\"Id\":\"8fed760a-f002-4931-bbc7-cabbc9eaafdb\",\"Operation\":0,\"Position\":0,\"RightLevel\":1," +
		"\"SysAdminUnit\":{\"displayValue\":\"Mandrill\",\"value\":\"8ab1343f-cb58-49c7-95a2-058b5f60acd3\"}," +
		"\"SysAdminUnitType\":\"472e97c7-6bd7-df11-9b2a-001d60e938c6\",\"isDeleted\":false,\"isNew\":false}]}";

	[Test]
	[Category("Unit")]
	[Description("Posts a camelCase { tableName, recordId } body to the WebApp-prefixed GetRecordRights path and parses the returned rights rows.")]
	public void GetRecordRights_ShouldPostCamelCaseBodyAndParseRows_WhenServiceReturnsRights() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient(isNetCore: false);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CapturedGetRecordRightsResponse);

		// Act
		IReadOnlyList<RecordRightRow> rows = client.GetRecordRights("SysContactRight", "abc", new CreatioRequestOptions());

		// Assert
		rows.Should().HaveCount(2, because: "the captured payload contains two rights rows");
		rows[0].SysAdminUnit.DisplayValue.Should().Be("Supervisor", because: "the first grantee must be parsed");
		rows[0].SysAdminUnitType.Should().Be("472e97c7-6bd7-df11-9b2a-001d60e938c6",
			because: "GetRecordRights returns SysAdminUnitType populated");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/rest/RightsService/GetRecordRights",
			"{\"tableName\":\"SysContactRight\",\"recordId\":\"abc\"}", 100_000, 3, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty list when GetRecordRights reports no rows, so callers never dereference null.")]
	public void GetRecordRights_ShouldReturnEmptyList_WhenServiceReturnsNoRows() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetRecordRightsResult\":[]}");

		// Act
		IReadOnlyList<RecordRightRow> rows = client.GetRecordRights("SysContactRight", "abc", new CreatioRequestOptions());

		// Assert
		rows.Should().BeEmpty(because: "an empty result set maps to an empty list");
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the record ref + recordRights array under the ApplyChanges 'record'/'recordRights' shape to the WebApp-prefixed ApplyChanges path and tolerates any valid JSON response.")]
	public void ApplyChanges_ShouldPostToApplyChangesPath_WhenInvoked() {
		// Arrange
		(CreatioRightsClient client, IApplicationClient applicationClient) = CreateClient(isNetCore: false);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rowsAffected\":1}");
		ApplyChangesRecordRef record = new() { EntitySchemaName = "Contact", PrimaryColumnValue = "rec-1" };
		RecordRightRow row = new() {
			Id = "row-1", Operation = 0, RightLevel = 1,
			SysAdminUnit = new SysAdminUnitRef { Value = "grantee-1", DisplayValue = "Everyone" },
			SysAdminUnitType = "type-1", IsNew = true
		};

		// Act
		client.ApplyChanges(record, new[] { row }, new CreatioRequestOptions());

		// Assert
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/rest/RightsService/ApplyChanges",
			Arg.Is<string>(body => body.Contains("\"record\":{") && body.Contains("\"recordRights\":[")),
			100_000, 3, 1);
	}

	[TestCase(ServiceUrlBuilder.KnownRoute.RightsGetRecordRights,
		"http://localhost/0/rest/RightsService/GetRecordRights")]
	[TestCase(ServiceUrlBuilder.KnownRoute.RightsApplyChanges,
		"http://localhost/0/rest/RightsService/ApplyChanges")]
	[Category("Unit")]
	[Description("Resolves each RightsService KnownRoute to its raw /rest path with the /0 WebApp alias prepended for .NET Framework.")]
	public void Build_ShouldResolveRawRightsServicePath_WhenNewKnownRouteIsRequested(
		ServiceUrlBuilder.KnownRoute route, string expectedUrl) {
		// Arrange
		ServiceUrlBuilder sut = new(new EnvironmentSettings { Uri = "http://localhost", IsNetCore = false });

		// Act
		string actual = sut.Build(route);

		// Assert
		actual.Should().Be(expectedUrl,
			because: "Build must prepend 0/ for .NET Framework and never hardcode it in the route map");
	}
}
