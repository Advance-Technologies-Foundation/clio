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
/// Behaviour of <see cref="ProcessPageButtonChecker"/> — the only place an invented completing-button name can
/// be caught. The server cannot do it: a Freedom UI page is merged client-side, so it never sees the page's
/// buttons. Without this check the name is stored, the process builds and saves green, and the step then waits
/// forever at run time with nothing reporting anything.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class ProcessPageButtonCheckerTests {

	private const string Page = "Accounts_FormPage";
	private const string Env = "dev";

	private IProcessPageReader _pageReader;
	private IToolCommandResolver _commandResolver;
	private ProcessPageButtonChecker _checker;

	[SetUp]
	public void Setup() {
		_pageReader = Substitute.For<IProcessPageReader>();
		_commandResolver = Substitute.For<IToolCommandResolver>();
		_commandResolver.Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>())
			.Returns(_ => new ProcessPageFactsCommand(_pageReader, ConsoleLogger.Instance));
		_checker = new ProcessPageButtonChecker(_commandResolver);
	}

	/// <summary>
	/// Stubs the page read. A name prefixed with '~' is put on the page with a NON-completing handler, so it
	/// exists but is not a candidate — the case that separates "refuse" from "warn".
	/// </summary>
	private void StubPageButtons(params string[] buttonNames) {
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
			Bundle = new PageBundleInfo { ViewConfig = viewConfig }
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

	private static JsonNode BuildDescriptor(string page, params string[] buttonNames) {
		JsonArray buttons = [];
		foreach (string name in buttonNames) {
			buttons.Add(new JsonObject { ["name"] = name, ["caption"] = $"{name} | {name}" });
		}
		return new JsonObject {
			["name"] = "UsrFlow",
			["elements"] = new JsonArray {
				new JsonObject { ["name"] = "Start1", ["type"] = "startEvent" },
				new JsonObject {
					["name"] = "Review1",
					["type"] = "preconfiguredPage",
					["preconfiguredPage"] = new JsonObject {
						["page"] = page,
						["buttons"] = buttons
					}
				}
			}
		};
	}

	[Test]
	[Description("A button name the page does not carry is REFUSED. This is the whole point of the check: the server accepts the name, the build and the save both succeed, and the step then hangs forever because the run time matches the pressed button against a tag no button on the page raises.")]
	public void CheckButtons_ShouldRefuseAButtonNameThePageDoesNotHave() {
		// Arrange — the page has Save and Cancel; the descriptor invents "SubmitButton".
		StubPageButtons("SaveButton", "CancelButton");

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "SubmitButton"));

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("SubmitButton").And.Contain(Page,
			because: "the caller has to know which name on which page was rejected");
		result.Error.Should().Contain("SaveButton").And.Contain("CancelButton",
			because: "naming what the page DOES carry is what turns the refusal into a next step");
	}

	[Test]
	[Description("Every named button being a real candidate passes. Without this the check would be a blanket refusal and no test would tell the two apart.")]
	public void CheckButtons_ShouldAcceptButtonsThePageCarries() {
		// Arrange
		StubPageButtons("SaveButton", "CancelButton");

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "SaveButton", "CancelButton"));

		// Assert
		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("A button that EXISTS on the page but is not a completing candidate is WARNED about, never refused. The candidate rule admits a handler issuing a completing request or none at all; a custom button that finishes the step in its own code satisfies neither and is still legitimate, so refusing it would block correct work on a heuristic.")]
	public void CheckButtons_ShouldWarnButNotRefuseANonCandidateThatExists() {
		// Arrange — PrintButton is on the page, but its handler issues crt.PrintRequest.
		StubPageButtons("SaveButton", "~PrintButton");

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "PrintButton"));

		// Assert
		result.Error.Should().BeNull(because: "the button exists, so this is a judgement call and not a defect");
		result.Warnings.Should().ContainSingle()
			.Which.Should().Contain("PrintButton").And.Contain("not among its completing-button candidates");
	}

	[Test]
	[Description("A candidate produces no warning — without this the warn path could fire on every button and no test would notice.")]
	public void CheckButtons_ShouldNotWarnAboutACandidate() {
		// Arrange
		StubPageButtons("SaveButton", "~PrintButton");

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "SaveButton"));

		// Assert
		result.Error.Should().BeNull();
		result.Warnings.Should().BeEmpty();
	}

	[Test]
	[Description("A case-only difference is refused too: the name is stored verbatim and the run time matches the tag composed from it, so 'savebutton' raises nothing on a page carrying 'SaveButton' — the same silent hang as a misspelt name.")]
	public void CheckButtons_ShouldRefuseAButtonDifferingOnlyInCase() {
		// Arrange
		StubPageButtons("SaveButton");

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "savebutton"));

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("savebutton");
	}

	[Test]
	[Description("A modify operation is checked the same way: the block sits at elementUpdate.preconfiguredPage there rather than under elements[], and an invented name reaches the run time by that route just as easily.")]
	public void CheckButtons_ShouldCheckAModifyOperationsArray() {
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
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, operations);

		// Assert
		result.Error.Should().NotBeNull();
		result.Error.Should().Contain("GhostButton");
	}

	[Test]
	[Description("When the page cannot be read the check stays SILENT. An unknown page, an unreachable environment and a Classic page are each refused downstream with a message about that; replacing it with a button complaint would trade a precise diagnosis for a worse one.")]
	public void CheckButtons_ShouldStaySilentWhenThePageCannotBeRead() {
		// Arrange
		StubPageUnreadable();

		// Act
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, BuildDescriptor(Page, "AnyButton"));

		// Assert
		result.Error.Should().BeNull(because: "the build's own refusal for a missing page is the better message");
	}

	[Test]
	[Description("A block that names no page is skipped — a modify changing only the recommendation carries none, and the buttons it does not send are the stored ones, already checked when they were set.")]
	public void CheckButtons_ShouldSkipABlockThatNamesNoPage() {
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
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, operations);

		// Assert
		result.Error.Should().BeNull();
		_commandResolver.DidNotReceive().Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>());
	}

	[Test]
	[Description("Two elements on the SAME page cost one page read, not two — a process routinely shows one page from several steps, and this check must not multiply the round trips a build makes.")]
	public void CheckButtons_ShouldReadEachPageOnlyOnce() {
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
		ProcessPageButtonCheckResult result = _checker.CheckButtons(Env, descriptor);

		// Assert
		result.Error.Should().BeNull();
		_commandResolver.Received(1).Resolve<ProcessPageFactsCommand>(Arg.Any<ProcessPageFactsOptions>());
	}

}
