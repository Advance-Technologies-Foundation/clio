# Test plan: Send chat message element (ENG-96034), clio side

**Spec**: [spec-eng-96034-send-chat-message-element.md](../prd/spec-eng-96034-send-chat-message-element.md)
**ADR**: [adr-eng-96034-send-chat-message-element.md](../adr/adr-eng-96034-send-chat-message-element.md)

The package's own unit tests (binder, applier, mapping guard, describe, generic route, template drift, designer
capture parity) live in the crt-process-builder PR; this plan covers clio.

## Unit (clio.tests)

Run: `dotnet test clio.tests/clio.tests.csproj -f net10.0 --filter "TestCategory=Unit&(Module=McpServer|Module=ProcessModel|Module=Command|Module=Common)"`

| ID | What | Test |
|---|---|---|
| TC-U-01 | `sendChatMessage`, `sendchatmessage` and `sendChatMessageUserTask` resolve to a user task, never UNKNOWN | `ManagerMapResolveDataIdTests.ResolveDataId_ShouldReturnActivityEventType_WhenActivityDataId` |
| TC-U-02 | The block passes the key check on create, addElement and a partial setElement | `ProcessBlockKeyCheckTests.Check_Should_AcceptTheChatMessageBlock_OnEveryRoute` |
| TC-U-03 | An unknown block key is refused with the path, the accepted keys and the `body`/`channel`/`contact`/case pointer | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownChatMessageKey_NamingItsPathAndPointer` |
| TC-U-04 | Unknown keys inside a value source and a `columnValues` entry are refused under their own labels | `ProcessBlockKeyCheckTests.Check_Should_RefuseAnUnknownKeyInsideAChatMessageValueSourceOrColumnValue` |
| TC-U-05 | A described block replays: `templateName`, `outputs`, `issues`, `display` are tolerated read-backs | `ProcessBlockKeyCheckTests.Check_Should_TolerateTheReadBackFields_OfADescribedChatMessageBlock` |
| TC-U-06 | A list or a bare string in `from`/`to` is refused before sending with the one-contact (one-channel) hint | `ProcessBlockKeyCheckTests.Check_Should_RefuseAListInAChatMessageValueSource_WithTheOneValueHint`, `Check_Should_RefuseABareStringInAChatMessageValueSource` |
| TC-U-07 | The key sets equal the bundled contracts; read-backs are describe-only members | `ProcessBlockKeyCheckTests.KeySets_Should_EqualTheMembersOfTheBundledServerContracts`, `ReadBackFields_Should_BeDescribeOnlyMembers` |
| TC-U-08 | The create call shape names `chatMessage (sendChatMessage)` and the bundled element contract has it | `ToolContractShortFormTests.CreateCallShape_Should_NameExactlyTheMembersOfTheBundledServerContract` |
| TC-U-09 | Describe reads `chatMessage`, keeps unknown members in the bags, leaves it null when absent | `ServerProcessDescriberTests.Describe_ShouldReadTheChatMessageMember_WhenServerReportsIt`, `Describe_ShouldKeepUnknownChatMessageMembers_InTheBag`, `Describe_ShouldLeaveTheChatMessageMemberNull_WhenServerOmitsIt` |
| TC-U-10 | create contract and prompt: type, block, guide pointer, Q1 carve-out | `CreateBusinessProcessToolTests.CreateBusinessProcessTool_ShouldOfferTheSendChatMessageElementAndRouteToItsGuide` |
| TC-U-11 | modify contract and prompt: addElement/setElement lists, Q1 refusal, setElement rules, pointer | `ModifyBusinessProcessToolTests.ModifyBusinessProcess_Description_ShouldOfferTheChatMessageBlockAndItsSetElementRules` |
| TC-U-12 | describe contract and prompt: read-back clause, describe-only members, outputs, connections warning | `DescribeProcessToolTests.DescribeProcess_ShouldDocumentTheChatMessageBlock_WhenToolContractIsRead` |
| TC-U-13 | list-user-tasks contract and prompt refuse the chat task on the generic route | `ListUserTasksToolTests.ListUserTasks_Description_ShouldRefuseTheSendChatMessageTaskOnTheGenericRoute` |
| TC-U-14 | validate-process-graph names the element as buildable | `ValidateProcessGraphToolTests.ValidateTool_ShouldNameTheWebServiceAndChatElementsAsBuildable_WhenDescriptionIsRead` |
| TC-U-15 | Per-contract ceiling and wire bundle hold, no ceiling raise | `ToolContractPayloadBudgetTests` |
| TC-U-16 | Floor literals name the chat cut | `ProcessDesignerRequiresPackageAttributeTests` (with the rebundle) |
| TC-U-17 | Archive pins (SHA, version, stamps, producing commit, security counts) | `BundledProcessBuilderPackageTests` |
| TC-U-18 | The bundled archive declares every chatMessage wire name clio sends or reads (a numeric floor alone cannot prove the block is there) | `BundledProcessBuilderPackageTests.BundledArchive_ShouldDeclareTheSendChatMessageWireNamesClioReads` |

The guidance-name oracle `UngatedMcpTools_ShouldNameOnlyUngatedGuidance_WhenDirectingAgentsToRead` checks that the
contracts point only at published guidance; `curated-knowledge-names.json` names `process-send-chat-message`.

## Integration (clio.mcp.e2e, category `McpE2E.ProcessDesigner`)

Class `SendChatMessageElementToolE2ETests`; TC-I-10 in `ListUserTasksToolE2ETests`. Every case except TC-I-10 skips
with an explicit reason on a stand without OmnichannelMessaging, or (template cases) without a uniquely named chat
template. On a stand with no active `Channel` the fixture creates and keeps one SMS channel, "Clio e2e chat channel",
under `McpE2E:AllowDestructiveMcpTests` (otherwise the cases that need a channel skip); it is never deleted, because a
Channel row cannot be deleted through the data services
(`docs/knowledge/platform/a-chat-channel-row-cannot-be-deleted-through-the-data-services.md`). TC-I-08 needs the
designer fixture `UsrSendChatDesignerFixture`; TC-I-09 needs `McpE2E:AllowDestructiveMcpTests`.

| ID | What |
|---|---|
| TC-I-01 | Template-mode build with column values (a constant text column, a lookup from a parameter) -> describe in the write shape -> setElement replay is a no-op |
| TC-I-02 | Custom-mode build; `ignoreErrors` omitted reads back `false` |
| TC-I-03 | Refusals that create nothing: generic route, unknown system setting, a list of contacts (clio), unknown template |
| TC-I-04 | An email-typed template is refused |
| TC-I-05 | Switch to custom is refused without `message` and lands with it, clearing the template |
| TC-I-06 | A raw `addMapping` onto `Template` is refused, pointing to the block |
| TC-I-07 | validate-process-graph accepts the described element, no UNKNOWN finding |
| TC-I-08 | Read-only parity: the designer fixture's block re-applied to a server-built element reads back the same |
| TC-I-09 | Run with an empty contact: ignoreErrors true completes with `SendStatus = Error`; false ends in Error |
| TC-I-10 | Hermetic: the list-user-tasks contract publishes the generic-route refusal |

## Not automated here

Tier B (a real send through the WireMock Telegram stub, column values on the created chat, Q18) and the manual
TC list of the analysis go to the Jira story.
