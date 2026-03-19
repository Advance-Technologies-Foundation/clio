using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class PageToolsTests {
[Test]
public void PageListTool_HasCorrectName() {
PageListTool.ToolName.Should().Be("page-list");
}

[Test]
public void PageGetTool_HasCorrectName() {
PageGetTool.ToolName.Should().Be("page-get");
}

[Test]
public void PageUpdateTool_HasCorrectName() {
PageUpdateTool.ToolName.Should().Be("page-update");
}
}
