using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class PageToolsTests {

	[Test]
	[Description("Verifies that PageListTool has the correct MCP tool name")]
	public void PageListTool_HasCorrectName() {
		PageListTool.ToolName.Should().Be("page-list", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageGetTool has the correct MCP tool name")]
	public void PageGetTool_HasCorrectName() {
		PageGetTool.ToolName.Should().Be("page-get", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("Verifies that PageUpdateTool has the correct MCP tool name")]
	public void PageUpdateTool_HasCorrectName() {
		PageUpdateTool.ToolName.Should().Be("page-update", "because the MCP tool name must match the protocol contract");
	}

	[Test]
	[Description("TryListPages returns success with pages when DataService returns valid rows")]
	public void TryListPages_WhenDataServiceReturnsRows_ReturnsSuccessWithPages() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/DataService/json/SyncReply/SelectQuery");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "TestPage1", ["UId"] = "uid-1", ["PackageName"] = "TestPkg" },
				new JObject { ["Name"] = "TestPage2", ["UId"] = "uid-2", ["PackageName"] = "TestPkg" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeTrue("because the DataService returned a successful response");
		response.Success.Should().BeTrue("because the query succeeded");
		response.Count.Should().Be(2, "because two rows were returned from the DataService");
		response.Pages.Should().HaveCount(2, "because each row maps to a page item");
		response.Pages[0].Name.Should().Be("TestPage1", "because the first row has Name=TestPage1");
	}

	[Test]
	[Description("TryListPages returns failure when DataService returns unsuccessful response")]
	public void TryListPages_WhenDataServiceFails_ReturnsFailure() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject { ["success"] = false };
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeFalse("because the DataService returned success=false");
		response.Success.Should().BeFalse("because the query failed");
		response.Error.Should().Be("Query failed", "because a non-success DataService response maps to this error");
	}

	[Test]
	[Description("TryListPages filters by package name when PackageName is provided")]
	public void TryListPages_WhenPackageNameProvided_IncludesPackageFilter() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { PackageName = "MyPackage", Limit = 50 };
		command.TryListPages(options, out PageListResponse response);
		applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("SysPackage.Name") && body.Contains("MyPackage")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("TryListPages returns empty list when DataService returns no rows")]
	public void TryListPages_WhenNoRows_ReturnsEmptyList() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeTrue("because the query itself succeeded even with zero results");
		response.Success.Should().BeTrue("because empty result set is still a successful query");
		response.Count.Should().Be(0, "because no rows were returned");
		response.Pages.Should().BeEmpty("because there are no pages to list");
	}

	[Test]
	[Description("TryListPages catches exceptions and returns error response")]
	public void TryListPages_WhenExceptionThrown_ReturnsErrorResponse() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(x => throw new System.Exception("Connection refused"));
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		bool result = command.TryListPages(options, out PageListResponse response);
		result.Should().BeFalse("because an exception occurred during execution");
		response.Success.Should().BeFalse("because the operation failed with an exception");
		response.Error.Should().Be("Connection refused", "because the exception message should be propagated");
	}

	[Test]
	[Description("Execute delegates to TryListPages and logs result")]
	public void Execute_WhenSuccessful_ReturnsZeroAndLogsResponse() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var dataServiceResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["Name"] = "Page1", ["UId"] = "uid-1", ["PackageName"] = "Pkg1" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(dataServiceResponse.ToString());
		var command = new PageListCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageListOptions { Limit = 50 };
		int exitCode = command.Execute(options);
		exitCode.Should().Be(0, "because TryListPages succeeded");
		logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("Page1")));
	}
}
