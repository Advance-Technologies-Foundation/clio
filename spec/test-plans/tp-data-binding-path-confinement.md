# Test Plan: data-binding path confinement

**Feature**: data-binding-path-confinement
**Stories**: [story-1](../stories/story-data-binding-path-confinement-1.md)
**ADR**: [adr-data-binding-path-confinement.md](../adr/adr-data-binding-path-confinement.md)
**Status**: Implemented
**Created**: 2026-11-15

| ID | Test | Proves |
|---|---|---|
| TC-U-01 | `DataBindingPathGuardTests.RequirePlainName_*` | Separators, dot segments, rooted paths, `:`, control characters, surrounding spaces and a trailing dot are refused; ordinary names with inner dots and spaces pass. |
| TC-U-02 | `DataBindingPathGuardTests.RequireCultureName_*` | Keys with separators, `..`, underscores, spaces or a missing part are refused; `en-US`, `sr-Latn-RS`, `zh-Hans` pass. |
| TC-U-03 | `DataBindingPathGuardTests.IsStrictlyInside_*` | The root itself, a parent segment, a prefix sibling and a path that differs from the root only by letter case are outside. |
| TC-U-04 | `CreateDataBindingCommandTests`, `AddDataBindingRowCommandTests`, `RemoveDataBindingRowCommandTests` `Execute_ShouldRefuseName_WhenItIsNotAPlainFolderName`; `CreateDataBindingCommandTests.Execute_ShouldRefuseSchemaNameAsBindingName_*` | Every unsafe package or binding name is refused with no file created, changed or deleted; the default binding name is refused before any Creatio request. |
| TC-U-05 | `CreateDataBindingCommandTests` and `AddDataBindingRowCommandTests` `Execute_ShouldRefuseLocalizationCulture_*` | An unsafe culture key is refused before any write; a JSON file outside the binding is not replaced. |
| TC-U-06 | `CreateDataBindingCommandTests.Execute_ShouldRefuseLinkedFolder_WhenItLeadsOutOfTheWorkspace` (`packages`, package), `Execute_ShouldRefuseLocalizationFolder_WhenItIsASymbolicLink` | A linked `packages` folder, a linked package folder and a linked `Localization` folder are refused before anything is created, written or deleted. |
| TC-U-07 | `AddDataBindingRowCommandTests.Execute_ShouldRefuseBindingFile_WhenItIsASymbolicLink` (`data.json`, culture file), `RemoveDataBindingRowCommandTests.Execute_ShouldRefuseLocalizationFile_WhenItIsASymbolicLink` | A linked `data.json` or culture file is refused before any file is written. |
| TC-U-08 | `AddDataBindingRowCommandTests.Execute_ShouldRefuseLocalizedImagePath_BeforeDataJsonIsWritten` | A refused localized value leaves `data.json` and every culture file unchanged. |
| TC-U-09 | `CreateDataBindingCommandTests.Execute_ShouldRefuseImagePathOutsideWorkspace_WhetherOrNotTheFileExists`, `Execute_ShouldRefuseImagePath_WhenItPassesThroughASymbolicLink`, `Execute_ShouldKeepBase64ImageValue_WhenItLooksLikeARootedPath` | Same refusal for an existing and a missing file, outside the workspace or behind a link inside it; base64 starting with `/` is kept. |
| TC-U-10 | `DataBindingToolTests.CreateDataBinding_Should_Refuse_Package_Name_That_Leaves_The_Workspace` | The MCP wrapper returns a normal failure envelope without resolving an environment or creating a file. |
| TC-E-01 | `DataBindingPathConfinementE2ETests.DataBinding_ShouldRefuseName_WhenItLeavesTheWorkspace` (`McpE2E.NoEnvironment`) | Over real stdio, each tool refuses a `..` package or binding name, and binding files planted beside the workspace stay unchanged. |
| TC-E-02 | `DataBindingPathConfinementE2ETests.DataBinding_ShouldRefuseCulture_WhenItIsNotACultureName` (`McpE2E.NoEnvironment`) | create and add refuse a `..` culture key; the JSON file it named and the binding stay unchanged. |
| TC-E-03 | `DataBindingPathConfinementE2ETests.CreateDataBinding_ShouldRefuseImagePath_WhenItIsOutsideTheWorkspace` (`McpE2E.NoEnvironment`) | The offline SysModule template refuses an outside image path with the same message whether or not the file exists. |

Links are covered by unit tests on the mock file system only: hosted Windows runners cannot create symbolic links,
and a skipped case would fail the `McpE2E.NoEnvironment` gate.
