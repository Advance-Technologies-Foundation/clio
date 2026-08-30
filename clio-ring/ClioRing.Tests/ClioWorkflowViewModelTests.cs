using System.Linq;
using ClioRing.Ipc;
using ClioRing.ViewModels;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

[TestFixture]
[Category("Unit")]
public sealed class ClioWorkflowViewModelTests {
	[Test]
	[Description("Makes a truncated list-packages result visibly incomplete instead of presenting the first page as the full package list.")]
	public void ParseRows_ShouldExposePackageCompleteness_WhenResponseIsTruncated() {
		// Arrange
		ClioToolCallResult result = new() {
			RawText = """
			          {"packages":[{"name":"Package00","version":"1.0.0","maintainer":"ATF"}],"count":1,"total":60,"offset":0,"limit":1,"truncated":true}
			          """
		};

		// Act
		string[] rows = ClioWorkflowViewModel.ParseRows("list-packages", result).ToArray();

		// Assert
		rows.Should().Contain("Showing 1 of 60 packages. More packages remain.",
			because: "the Ring must not make a bounded MCP page look like the complete environment package list");
		rows.Should().Contain(row => row.Contains("Package00"),
			because: "making incompleteness visible must preserve the package rows returned by the MCP tool");
	}
}
