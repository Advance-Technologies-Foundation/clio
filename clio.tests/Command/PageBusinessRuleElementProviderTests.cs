using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleElementProviderTests {
	[Test]
	[Category("Unit")]
	[Description("Collects named page elements recursively from every viewConfig branch.")]
	public void GetElementNames_Should_Collect_Named_Elements_From_All_Recursive_Branches() {
		// Arrange
		PageBusinessRuleElementProvider provider = new();
		PageBundleInfo bundle = new() {
			ViewConfig = JsonNode.Parse("""
			[
			  {
			    "name": "Root",
			    "type": "crt.FlexContainer",
			    "items": [
			      {
			        "name": "Input_One",
			        "type": "crt.Input"
			      },
			      {
			        "name": "Tab",
			        "type": "crt.TabContainer",
			        "tools": [
			          {
			            "name": "RefreshButton",
			            "type": "crt.Button"
			          }
			        ],
			        "listActions": [
			          {
			            "name": "AddRecordButton",
			            "type": "crt.Button"
			          }
			        ],
			        "metadata": {
			          "action": {
			            "name": "NestedMetadataButton",
			            "type": "crt.Button"
			          }
			        }
			      }
			    ]
			  }
			]
			""")!.AsArray()
		};

		// Act
		IReadOnlySet<string> result = provider.GetElementNames(bundle);

		// Assert
		result.Should().BeEquivalentTo(["Root", "Input_One", "Tab", "RefreshButton", "AddRecordButton", "NestedMetadataButton"],
			because: "show/hide page actions can target any named element discovered recursively in viewConfig");
	}
}
