using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// Stand-free end-to-end contract coverage for the Delete data element (ENG-92709) across the three
/// process-designer tools, read off the ADVERTISED contract through <c>get-tool-contract</c> over the real MCP
/// stdio transport rather than asserted against the C# constants.
/// <para>Deliberately the <c>NoEnvironment</c> tier, and deliberately NOT the whole story. The BEHAVIOURAL half
/// — build a <c>deleteData</c> element and read it back through describe — needs a stand carrying a
/// <c>CrtProcessBuilder</c> that supports the block, so it belongs with <c>ApprovalElementToolE2ETests</c> in
/// the Sandbox tier and cannot be written honestly until the package is restamped and rebundled.</para>
/// <para>What CAN be verified today is the half that ships regardless of the archive: the instruction surface.
/// That split is not a convenience — the descriptions and the bundled archive are versioned separately, so the
/// window where the text promises an element the bundled package cannot build is a real state, and these tests
/// are what make the description side of it observable.</para>
/// <para>What these tests do NOT establish is that an agent ever reads this text. These tools are on the lazy
/// surface, so a session reaches them through <c>clio-run</c> and can configure the element end to end without
/// a single description in context — observed in an agent run on 2026-09-09. The descriptions are the
/// resident-session copy of an obligation whose load-bearing copy is the <c>process-delete-data</c> guidance
/// article; asserting them here keeps the two from drifting, and is not evidence of delivery.</para>
/// </summary>
[TestFixture]
[AllureNUnit]
[Category("McpE2E.NoEnvironment")]
[Parallelizable(ParallelScope.Self)]
public sealed class DeleteDataElementContractToolE2ETests : McpContractFixtureBase {

	private const string CreateToolName = CreateBusinessProcessTool.CreateBusinessProcessToolName;
	private const string ModifyToolName = ModifyBusinessProcessTool.ModifyBusinessProcessToolName;
	private const string DescribeToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("create-business-process advertises the deleteData block AND the obligation that comes with it: count the matching records, name the object as the designer names it, and get an explicit yes. The obligation lives in the tool description on purpose - per the spec's D3 it is the only surface always in the agent's context, so a caller that never fetches guidance still sees it.")]
	[AllureFeature(CreateToolName)]
	[AllureTag(CreateToolName)]
	[AllureName("create-business-process advertises deleteData and its confirmation obligation")]
	public async Task CreateBusinessProcess_Should_AdvertiseDeleteDataBlock_AndItsConfirmationObligation() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, CreateToolName);

		// Assert
		description.Should().Contain("deleteData?",
			because: "an agent discovers the block from the advertised contract, not from the source");
		description.Should().Contain("COUNT what the",
			because: "the count is what turns an approval into an informed one, and it is the step most often "
				+ "skipped");
		description.Should().Contain("get an explicit yes",
			because: "AC6 asks the agent to confirm the selected records, and the description restates the "
				+ "obligation for the sessions where this tool is resident - it is NOT a surface guaranteed to "
				+ "be in context, because the process-designer tools sit on the lazy surface and an agent "
				+ "driving them through clio-run never sees a description at all, which is why the "
				+ "process-delete-data article is the load-bearing copy");
		description.Should().Contain("NAME THE OBJECT AS THE DESIGNER NAMES IT",
			because: "the object picker offers the platform's junction tables interleaved with the business "
				+ "objects, so a shortened name can hide that the step deletes membership rows rather than records");
		description.Should().NotContain("|deleteData|",
			because: "deleteData resolves through the generic user-task handler, so it belongs in the alias list "
				+ "with readData/changeData rather than in the type enum");
	}

	[Test]
	[Description("modify-business-process advertises deleteData among the setElement fields and states the retarget consequence: a target change clears the record filter, and an element left without one deletes nothing and fails at run time.")]
	[AllureFeature(ModifyToolName)]
	[AllureTag(ModifyToolName)]
	[AllureName("modify-business-process advertises setElement.deleteData and its retarget reset")]
	public async Task ModifyBusinessProcess_Should_AdvertiseDeleteDataUpdate_AndItsRetargetReset() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, ModifyToolName);

		// Assert
		description.Should().Contain("deleteData?",
			because: "setElement's field list is how a caller learns the element can be reconfigured in place");
		description.Should().Contain("deleteData {source?}",
			because: "the block has exactly one field, and saying so is what stops a caller inventing others");
		description.Should().Contain("deletes nothing and fails at run time",
			because: "a retarget clears the filter, and the caller has to know that leaves a broken step unless "
				+ "a setFilter follows in the same batch");
	}

	[Test]
	[Description("describe-business-process advertises the deleteData read-back and says the record filter is reported by the element's own filter block rather than inside it - the shape a round-trip depends on.")]
	[AllureFeature(DescribeToolName)]
	[AllureTag(DescribeToolName)]
	[AllureName("describe-business-process advertises the deleteData read-back")]
	public async Task DescribeBusinessProcess_Should_AdvertiseDeleteDataReadBack() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		string description = await AdvertisedDescriptionAsync(context, DescribeToolName);

		// Assert
		description.Should().Contain("deleteData element's data configuration",
			because: "a caller round-tripping a described process needs to know the block comes back");
		description.Should().Contain("not part of this one",
			because: "the filter is reported separately, and a caller expecting it inside the block would read a "
				+ "configured element as unfiltered");
	}

	/// <summary>
	/// The tool's ADVERTISED description, fetched the way an agent would. The process-designer tools live on the
	/// lazy surface rather than the resident manifest, so <c>tools/list</c> does not carry them and
	/// <c>get-tool-contract</c> is the route — the same one
	/// <see cref="ProcessDesignerContractRequiredArgsE2ETests"/> uses. Reachability is asserted first so a
	/// missing tool fails as "not discoverable" rather than as an empty description.
	/// </summary>
	private static async Task<string> AdvertisedDescriptionAsync(ArrangeContext context, string toolName) {
		IReadOnlyCollection<string> reachable =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		reachable.Should().Contain(toolName,
			because: $"the {toolName} MCP tool must be discoverable on the lazy surface before its advertised "
				+ "contract can be asserted");

		CallToolResult contractResult = await context.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["tool-names"] = new[] { toolName }
				}
			},
			context.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);
		ToolContractDefinition contract = contracts.Tools!.Single(definition => definition.Name == toolName);
		contract.Description.Should().NotBeNullOrWhiteSpace(
			because: $"the advertised contract for {toolName} must carry the description agents read");
		return contract.Description;
	}
}
