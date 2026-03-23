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

	[Test]
	[Description("TryGetPage calls ServiceUrlBuilder without /0/ prefix for DesignerService URL")]
	public void TryGetPage_UsesCorrectDesignerServiceUrl_WithoutDoubleZeroPrefix() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject {
					["Name"] = "TestPage_FormPage",
					["UId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
					["PackageName"] = "TestPkg",
					["ParentSchemaName"] = "PageWithTabsFreedomTemplate"
				}
			}
		};
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject {
				["body"] = "define(\"TestPage_FormPage\", [], function() { return {}; });"
			}
		};
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				return callIndex == 1
					? metadataResponse.ToString()
					: getSchemaResponse.ToString();
			});
		var command = new PageGetCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageGetOptions { SchemaName = "TestPage_FormPage" };
		bool result = command.TryGetPage(options, out PageGetResponse response);
		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.Body.Should().Contain("TestPage_FormPage");
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.DidNotReceive().Build(Arg.Is<string>(s => s.Contains("/0/ServiceModel")));
	}

	[Test]
	[Description("TryGetPage returns body and metadata when schema is found")]
	public void TryGetPage_WhenSchemaExists_ReturnsBodyAndMetadata() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(ci => $"http://test{ci.Arg<string>()}");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject {
					["Name"] = "UsrApp_FormPage",
					["UId"] = "11111111-2222-3333-4444-555555555555",
					["PackageName"] = "UsrApp",
					["ParentSchemaName"] = "PageWithTabsFreedomTemplate"
				}
			}
		};
		string expectedBody = "define(\"UsrApp_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return {}; });";
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject { ["body"] = expectedBody }
		};
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => (++callIndex) == 1 ? metadataResponse.ToString() : getSchemaResponse.ToString());
		var command = new PageGetCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageGetOptions { SchemaName = "UsrApp_FormPage" };
		bool result = command.TryGetPage(options, out PageGetResponse response);
		result.Should().BeTrue();
		response.SchemaName.Should().Be("UsrApp_FormPage");
		response.SchemaUId.Should().Be("11111111-2222-3333-4444-555555555555");
		response.PackageName.Should().Be("UsrApp");
		response.ParentSchemaName.Should().Be("PageWithTabsFreedomTemplate");
		response.Body.Should().Be(expectedBody);
	}

	[Test]
	[Description("TryGetPage returns error when schema not found in SysSchema")]
	public void TryGetPage_WhenSchemaNotFound_ReturnsError() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://test/url");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		var command = new PageGetCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageGetOptions { SchemaName = "NonExistentPage" };
		bool result = command.TryGetPage(options, out PageGetResponse response);
		result.Should().BeFalse();
		response.Error.Should().Contain("NonExistentPage").And.Contain("not found");
	}

	[Test]
	[Description("TryUpdatePage calls ServiceUrlBuilder without /0/ prefix for both GetSchema and SaveSchema")]
	public void TryUpdatePage_UsesCorrectDesignerServiceUrls_WithoutDoubleZeroPrefix() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery")
			.Returns("http://test/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema")
			.Returns("http://test/0/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["UId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }
			}
		};
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var getSchemaResponse = new JObject {
			["success"] = true,
			["schema"] = new JObject {
				["body"] = "old body",
				["name"] = "Test_FormPage"
			}
		};
		var saveResponse = new JObject { ["success"] = true };
		int callIndex = 0;
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				callIndex++;
				if (callIndex == 1) return metadataResponse.ToString();
				if (callIndex == 2) return getSchemaResponse.ToString();
				return saveResponse.ToString();
			});
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageUpdateOptions {
			SchemaName = "Test_FormPage",
			Body = validBody,
			DryRun = false
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.BodyLength.Should().Be(validBody.Length);
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.Received(1).Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
		serviceUrlBuilder.DidNotReceive().Build(Arg.Is<string>(s => s.Contains("/0/ServiceModel")));
	}

	[Test]
	[Description("TryUpdatePage with dryRun skips GetSchema and SaveSchema calls")]
	public void TryUpdatePage_WhenDryRun_SkipsDesignerServiceCalls() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns("http://test/url");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray {
				new JObject { ["UId"] = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }
			}
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		string validBody = "define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageUpdateOptions {
			SchemaName = "Test_FormPage",
			Body = validBody,
			DryRun = true
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeTrue();
		response.DryRun.Should().BeTrue();
		response.BodyLength.Should().Be(validBody.Length);
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
		serviceUrlBuilder.DidNotReceive().Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema");
	}

	[Test]
	[Description("TryUpdatePage returns error when schema not found")]
	public void TryUpdatePage_WhenSchemaNotFound_ReturnsError() {
		var applicationClient = Substitute.For<IApplicationClient>();
		var serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		var logger = Substitute.For<ILogger>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns("http://test/url");
		var metadataResponse = new JObject {
			["success"] = true,
			["rows"] = new JArray()
		};
		applicationClient.ExecutePostRequest(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(metadataResponse.ToString());
		string validBody = "define(\"X\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";
		var command = new PageUpdateCommand(applicationClient, serviceUrlBuilder, logger);
		var options = new PageUpdateOptions {
			SchemaName = "MissingPage",
			Body = validBody,
			DryRun = false
		};
		bool result = command.TryUpdatePage(options, out PageUpdateResponse response);
		result.Should().BeFalse();
		response.Error.Should().Contain("MissingPage").And.Contain("not found");
	}
}
