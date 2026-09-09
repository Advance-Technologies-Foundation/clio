using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Behaviour of <see cref="ProcessPageFactsChecker"/> — the only place an invented completing-button name or an
/// invented data-source name can be caught. The server cannot do it: a Freedom UI page is merged client-side, so
/// it never sees the page's buttons or data sources. Without this check the name is stored, the process builds
/// and saves green, reads back with <c>inSync: true</c>, and the step then waits forever at run time with
/// nothing reporting anything.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class ProcessPageFactsCheckerTests {

	private const string Page = "Accounts_FormPage";
	private const string Env = "dev";

	/// <summary>Marks a stub name as present on the page but outside the set the facts report.</summary>
	private const char NonOfferedMarker = '~';

	private IProcessPageReader _pageReader;
	private IToolCommandResolver _commandResolver;
	private ProcessPageFactsChecker _checker;

	[SetUp]
	public void Setup() {
		_pageReader = Substitute.For<IProcessPageReader>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>())
			.Returns(_ => new ProcessPageFactsCommand(_pageReader, ConsoleLogger.Instance));
		_checker = new ProcessPageFactsChecker(_commandResolver);
	}

	/// <summary>
	/// Stubs the page read. A name prefixed with '~' is put on the page with a NON-completing handler, so it
	/// exists but is not a candidate — the case that separates "refuse" from "warn".
	/// </summary>
	private void StubPageButtons(params string[] buttonNames) {
		StubPage(buttonNames, []);
	}

	/// <summary>
	/// Stubs the page read with data sources as well. Each spec is <c>name:entity</c> for a page-scoped entity
	/// source — the kind the facts report and the card offers — or <c>~name</c> for one the page declares but the
	/// facts filter out. That second kind is what separates a data-source "refuse" from a "warn": the run time
	/// resolves against everything the page declares, while the facts report only the offered subset.
	/// </summary>
	private void StubPage(string[] buttonNames, string[] dataSourceSpecs) {
		JsonObject dataSources = [];
		foreach (string spec in dataSourceSpecs) {
			if (spec.StartsWith(NonOfferedMarker)) {
				// Declared, but view-element scoped: a list's own source. The runtime resolves it; the card
				// never offers it.
				dataSources[spec[1..]] = new JsonObject {
					["scope"] = "viewElement",
					["type"] = "crt.EntityDataSource",
					["config"] = new JsonObject { ["entitySchemaName"] = "SomeEntity" }
				};
				continue;
			}
			string[] parts = spec.Split(':');
			dataSources[parts[0]] = new JsonObject {
				["scope"] = "page",
				["type"] = "crt.EntityDataSource",
				["config"] = new JsonObject { ["entitySchemaName"] = parts[1] }
			};
		}
		JsonArray viewConfig = [];
		foreach (string spec in buttonNames) {
			bool completing = !spec.StartsWith('~');
			string name = completing ? spec : spec[1..];
			viewConfig.Add(new JsonObject {
				["type"] = "crt.Button",
				["name"] = name,
				["caption"] = name,
				["clicked"] = new JsonObject {
					["request"] = completing ? "crt.SaveRecordRequest" : "crt.PrintRequest"
				}
			});
		}
		PageGetResponse page = new() {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = Page, SchemaType = "web" },
			Bundle = new PageBundleInfo {
				ViewConfig = viewConfig,
				ModelConfig = new JsonObject { ["dataSources"] = dataSources }
			}
		};
		_pageReader.TryGetPage(Arg.Any<PageGetOptions>(), out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = page;
				return true;
			});
	}

	private void StubPageUnreadable() {
		PageGetResponse page = new() { Success = false, Error = "page not found" };
		_pageReader.TryGetPage(Arg.Any<PageGetOptions>(), out Arg.Any<PageGetResponse>())
			.Returns(call => {
				call[1] = page;
				return false;
			});
	}

	private static JsonNode BuildDescriptor(string page, params string[] buttonNames) =>
		BuildDescriptor(page, buttonNames, []);

	/// <summary>Builds a create descriptor. Each data-source spec is <c>name</c> or <c>name:entity</c>.</summary>
	private static JsonNode BuildDescriptor(string page, string[] buttonNames, string[] dataSourceSpecs) {
		JsonArray buttons = [];
		foreach (string name in buttonNames) {
			buttons.Add(new JsonObject { ["name"] = name, ["caption"] = $"{name} | {name}" });
		}
		JsonArray dataSources = [];
		foreach (string spec in dataSourceSpecs) {
			string[] parts = spec.Split(':');
			JsonObject dataSource = new() { ["name"] = parts[0] };
			if (parts.Length > 1) {
				dataSource["entitySchemaName"] = parts[1];
			}
			dataSources.Add(dataSource);
		}
		JsonObject block = new() {
			["page"] = page,
			["buttons"] = buttons
		};
		if (dataSources.Count > 0) {
			block["dataSources"] = dataSources;
		}
		return new JsonObject {
			["name"] = "UsrFlow",
			["elements"] = new JsonArray {
				new JsonObject { ["name"] = "Start1", ["type"] = "startEvent" },
				new JsonObject {
					["name"] = "Review1",
					["type"] = "preconfiguredPage",
					["preconfiguredPage"] = block
				}
			}
		};
	}

	[Test]
	[Description("A button name the page does not carry is REFUSED. This is the whole point of the check: the server accepts the name, the build and the save both succeed, and the step then hangs forever because the run time matches the pressed button against a tag no button on the page raises.")]
	public void CheckPreconfiguredPages_ShouldRefuseAButtonNameThePageDoesNotHave() {
		// Arrange — the page has Save and Cancel; the descriptor invents "SubmitButton".
		StubPageButtons("SaveButton", "CancelButton");

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "SubmitButton"));

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("SubmitButton").And.Contain(Page,
			because: "the caller has to know which name on which page was rejected");
		result.Error.Should().Contain("SaveButton").And.Contain("CancelButton",
			because: "naming what the page DOES carry is what turns the refusal into a next step");
	}

	[Test]
	[Description("Every named button being a real candidate passes. Without this the check would be a blanket refusal and no test would tell the two apart.")]
	public void CheckPreconfiguredPages_ShouldAcceptButtonsThePageCarries() {
		// Arrange
		StubPageButtons("SaveButton", "CancelButton");

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "SaveButton", "CancelButton"));

		// Assert
		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("A button that EXISTS on the page but is not a completing candidate is WARNED about, never refused. The candidate rule admits a handler issuing a completing request or none at all; a custom button that finishes the step in its own code satisfies neither and is still legitimate, so refusing it would block correct work on a heuristic.")]
	public void CheckPreconfiguredPages_ShouldWarnButNotRefuseANonCandidateThatExists() {
		// Arrange — PrintButton is on the page, but its handler issues crt.PrintRequest.
		StubPageButtons("SaveButton", "~PrintButton");

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "PrintButton"));

		// Assert
		result.Error.Should().BeNull(because: "the button exists, so this is a judgement call and not a defect");
		result.Warnings.Should().ContainSingle()
			.Which.Should().Contain("PrintButton").And.Contain("not among its completing-button candidates");
	}

	[Test]
	[Description("A candidate produces no warning — without this the warn path could fire on every button and no test would notice.")]
	public void CheckPreconfiguredPages_ShouldNotWarnAboutACandidate() {
		// Arrange
		StubPageButtons("SaveButton", "~PrintButton");

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "SaveButton"));

		// Assert
		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("A case-only difference is refused too: the name is stored verbatim and the run time matches the tag composed from it, so 'savebutton' raises nothing on a page carrying 'SaveButton' — the same silent hang as a misspelt name.")]
	public void CheckPreconfiguredPages_ShouldRefuseAButtonDifferingOnlyInCase() {
		// Arrange
		StubPageButtons("SaveButton");

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "savebutton"));

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("savebutton");
	}

	[Test]
	[Description("A modify operation is checked the same way: the block sits at elementUpdate.preconfiguredPage there rather than under elements[], and an invented name reaches the run time by that route just as easily.")]
	public void CheckPreconfiguredPages_ShouldCheckAModifyOperationsArray() {
		// Arrange
		StubPageButtons("SaveButton");
		JsonNode operations = new JsonArray {
			new JsonObject {
				["op"] = "setElement",
				["elementName"] = "Review1",
				["elementUpdate"] = new JsonObject {
					["preconfiguredPage"] = new JsonObject {
						["page"] = Page,
						["buttons"] = new JsonArray { new JsonObject { ["name"] = "GhostButton" } }
					}
				}
			}
		};

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, operations);

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("GhostButton");
	}

	[Test]
	[Description("When the page cannot be read the check stays SILENT. An unknown page, an unreachable environment and a Classic page are each refused downstream with a message about that; replacing it with a button complaint would trade a precise diagnosis for a worse one.")]
	public void CheckPreconfiguredPages_ShouldStaySilentWhenThePageCannotBeRead() {
		// Arrange
		StubPageUnreadable();

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, BuildDescriptor(Page, "AnyButton"));

		// Assert
		result.Error.Should().BeNull(because: "the build's own refusal for a missing page is the better message");
	}

	[Test]
	[Description("A block that names no page is skipped — a modify changing only the recommendation carries none, and the buttons it does not send are the stored ones, already checked when they were set.")]
	public void CheckPreconfiguredPages_ShouldSkipABlockThatNamesNoPage() {
		// Arrange
		StubPageButtons("SaveButton");
		JsonNode operations = new JsonArray {
			new JsonObject {
				["op"] = "setElement",
				["elementName"] = "Review1",
				["elementUpdate"] = new JsonObject {
					["preconfiguredPage"] = new JsonObject { ["recommendation"] = "Fill it in" }
				}
			}
		};

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, operations);

		// Assert
		result.Error.Should().BeNull();
		_commandResolver.DidNotReceive().Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>());
	}

	[Test]
	[Description("Two elements on the SAME page cost one page read, not two — a process routinely shows one page from several steps, and this check must not multiply the round trips a build makes.")]
	public void CheckPreconfiguredPages_ShouldReadEachPageOnlyOnce() {
		// Arrange
		StubPageButtons("SaveButton");
		JsonNode descriptor = new JsonObject {
			["elements"] = new JsonArray {
				new JsonObject {
					["name"] = "Review1",
					["preconfiguredPage"] = new JsonObject {
						["page"] = Page,
						["buttons"] = new JsonArray { new JsonObject { ["name"] = "SaveButton" } }
					}
				},
				new JsonObject {
					["name"] = "Review2",
					["preconfiguredPage"] = new JsonObject {
						["page"] = Page,
						["buttons"] = new JsonArray { new JsonObject { ["name"] = "SaveButton" } }
					}
				}
			}
		};

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, descriptor);

		// Assert
		result.Error.Should().BeNull();
		_commandResolver.Received(1).Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>());
	}

	[Test]
	[Description("A data source the page does not declare is REFUSED. Measured on a stand (ENG-92705, 2026-09-07): the same page and the same button completes in 33 s without the entry and hangs forever with it. The run time resolves the stored name against the page view model's data-source map when the completing button is pressed, and a name that resolves to nothing aborts the completion before the engine is signalled — the page just stays open with no error, no validation message, and the instance stuck at 'Running'.")]
	public void CheckPreconfiguredPages_ShouldRefuseADataSourceThePageDoesNotDeclare() {
		// Arrange — the page declares no data source; the descriptor names PDS anyway. This is the exact
		// ...Isolate2 case from the stand run, which the create path accepted silently.
		StubPage(["SaveButton"], []);

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env,
			BuildDescriptor(Page, ["SaveButton"], ["PDS:UsrRequest"]));

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("PDS").And.Contain(Page,
			because: "the caller has to know which name on which page was rejected");
		result.Error.Should().Contain("Running",
			because: "the symptom is a silently stuck instance, which is what makes this worth refusing");
	}

	[Test]
	[Description("A data source the page DOES declare passes. Without this the check could be a blanket refusal of every dataSources entry and no test would tell the two apart.")]
	public void CheckPreconfiguredPages_ShouldAcceptADataSourceThePageDeclares() {
		// Arrange
		StubPage(["SaveButton"], ["PDS:UsrRequest"]);

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env,
			BuildDescriptor(Page, ["SaveButton"], ["PDS:UsrRequest"]));

		// Assert
		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("A source the page declares but the FACTS filter out is warned about, never refused. The facts report only page-scoped entity sources, because those are what the card offers; the run time is looser and resolves anything the page declares. Refusing on the narrower set would block a caller who named a real source deliberately.")]
	public void CheckPreconfiguredPages_ShouldWarnButNotRefuseADeclaredSourceOutsideTheOfferedSet() {
		// Arrange — GridDS is view-element scoped: declared on the page, absent from the facts.
		StubPage(["SaveButton"], ["PDS:UsrRequest", "~GridDS"]);

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env,
			BuildDescriptor(Page, ["SaveButton"], ["GridDS"]));

		// Assert
		result.Error.Should().BeNull(
			because: "the run time resolves it, so this is a judgement call and not a step that cannot finish");
		result.Warnings.Should().ContainSingle()
			.Which.Should().Contain("GridDS").And.Contain("page-scoped entity data sources");
	}

	[Test]
	[Description("A name that matches but an entity that does not is warned about. The value still arrives from the page's own source so the step completes, but the generated parameter is typed from the entity passed here — the wrong one resolves the wrong primary column and the record id silently never reaches the process.")]
	public void CheckPreconfiguredPages_ShouldWarnWhenTheEntityDoesNotMatchThePageSource() {
		// Arrange
		StubPage(["SaveButton"], ["PDS:UsrRequest"]);

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env,
			BuildDescriptor(Page, ["SaveButton"], ["PDS:UsrOrder"]));

		// Assert
		result.Error.Should().BeNull(because: "the step still completes; only the parameter's type is wrong");
		result.Warnings.Should().ContainSingle()
			.Which.Should().Contain("UsrRequest").And.Contain("UsrOrder");
	}

	[Test]
	[Description("The RETARGET path is refused too, and it is the route that matters most: moving a step onto a page with fewer data sources leaves the previous page's source behind, dataSources has no removal path through the modify contract, and the parameter carries no SourceParameterUId for the orphan pass to find — so before this check a working element could be turned into an unfinishable one with no way back.")]
	public void CheckPreconfiguredPages_ShouldRefuseADataSourceOnAModifyOperationsArray() {
		// Arrange — the new page declares nothing; the retarget still carries the old page's PDS.
		StubPage(["SaveButton"], []);
		JsonNode operations = new JsonArray {
			new JsonObject {
				["op"] = "setElement",
				["elementName"] = "Review1",
				["elementUpdate"] = new JsonObject {
					["preconfiguredPage"] = new JsonObject {
						["page"] = Page,
						["buttons"] = new JsonArray { new JsonObject { ["name"] = "SaveButton" } },
						["dataSources"] = new JsonArray {
							new JsonObject { ["name"] = "PDS", ["entitySchemaName"] = "UsrRequest" }
						}
					}
				}
			}
		};

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, operations);

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("PDS");
	}

	[Test]
	[Description("A block carrying ONLY dataSources — no buttons — is still checked. A modify may legitimately send one without the other, and skipping the page read whenever buttons are absent would leave the defect open on exactly the payload that has nothing else to validate.")]
	public void CheckPreconfiguredPages_ShouldCheckDataSourcesWhenNoButtonsAreNamed() {
		// Arrange
		StubPage(["SaveButton"], []);
		JsonNode operations = new JsonArray {
			new JsonObject {
				["op"] = "setElement",
				["elementName"] = "Review1",
				["elementUpdate"] = new JsonObject {
					["preconfiguredPage"] = new JsonObject {
						["page"] = Page,
						["dataSources"] = new JsonArray { new JsonObject { ["name"] = "PDS" } }
					}
				}
			}
		};

		// Act
		ProcessPageCheckResult result = _checker.CheckPreconfiguredPages(Env, operations);

		// Assert
		result.Error.Should().NotBeNull(
			because: "a payload with no buttons must not skip the page read and pass by default");
	}

}
