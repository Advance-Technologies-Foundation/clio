using System;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace Clio.Tests;

/// <summary>
/// Unit coverage for the switch that turns a process-designer test's "the package cannot serve this" skip into a
/// failure on the lane that installs CrtProcessBuilder itself.
/// </summary>
/// <remarks>
/// Lives in <c>clio.tests</c> for the reason the e2e project's csproj gives: nothing in <c>clio.mcp.e2e</c> runs before
/// merge, and a regression here would turn a broken install on the process-designer lane back into a green build with
/// the whole category ignored.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
public sealed class ProcessDesignerE2ESupportTests {

	[Test]
	[Description("Without McpE2E:RequireProcessBuilder a package that cannot serve the test only ignores it, with the reason unchanged, so a developer stand that is merely behind is not reported as a regression.")]
	public void IgnoreUnlessProcessBuilderIsRequired_ShouldIgnore_WhenTheLaneDoesNotRequireThePackage() {
		// Arrange
		McpE2ESettings settings = new() { RequireProcessBuilder = false };

		// Act
		Action act = () => ProcessDesignerE2ESupport.IgnoreUnlessProcessBuilderIsRequired("CrtProcessBuilder is too old", settings);

		// Assert
		act.Should().Throw<IgnoreException>(
				because: "outside the process-designer lane an outdated stand is a precondition, not a failure")
			.WithMessage("CrtProcessBuilder is too old",
				because: "the Ignore must carry the reason unchanged");
	}

	[Test]
	[Description("With McpE2E:RequireProcessBuilder set, the same reason fails the test and says why, because the process-designer lane installed the package itself and an Ignore would hide a broken install behind a green build.")]
	public void IgnoreUnlessProcessBuilderIsRequired_ShouldFail_WhenTheLaneRequiresThePackage() {
		// Arrange
		McpE2ESettings settings = new() { RequireProcessBuilder = true };
		Action act = () => ProcessDesignerE2ESupport.IgnoreUnlessProcessBuilderIsRequired("CrtProcessBuilder is too old", settings);

		// Act + Assert: Assert.Fail records the failure on the CURRENT test result before it throws, so the call runs in
		// an isolated context - catching the exception alone would still fail this test.
		using (new TestExecutionContext.IsolatedContext()) {
			act.Should().Throw<AssertionException>(
					because: "on the lane that installs the package, a package that cannot serve the test is a failure")
				.Which.Message.Should().Contain("CrtProcessBuilder is too old",
					because: "the failure must keep the reason the Ignore would have given")
				.And.Contain(ProcessDesignerE2ESupport.RequiredProcessBuilderNote,
					because: "the failure must say why it is not an Ignore");
		}
	}
}
