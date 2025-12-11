# Environment Information Command - Implementation Plan

**Status:** Updated for Existing Command Extension  
**Created:** 2025-12-11  
**Updated:** 2025-12-11  
**Target Completion:** TBD  
**Priority:** Medium  
**Complexity:** Low-Medium

---

## Executive Summary

**Revised Approach:** Extend the existing `ShowAppListCommand` with additional output format options instead of creating a new command. This approach is more efficient and maintains better backward compatibility.

**Key Changes:**
- Extend `ShowAppListCommand` with format options
- Keep existing behavior fully compatible
- Add JSON, table, and raw output formats
- Implement sensitive data masking
- Update documentation

**Key Deliverables:**
- Extended `ShowAppListCommand` with format support
- Support for single and multi-environment queries
- Multiple output formats (JSON, table, raw)
- Full test coverage for new functionality
- Updated documentation

---

## Tasks Breakdown

### Task 1: Analysis and Design
- **Status:** ✅ COMPLETED
- **Effort:** 2h
- **Description:** 
  - Analyze current `ShowAppListCommand` implementation
  - Review `SettingsRepository` and `EnvironmentSettings` structures
  - Design new command architecture
  - Define option classes and behavior

**Output:**
- `spec/env-info-spec.md` - Comprehensive specification
- Architecture diagrams
- Design decisions documented

---

### Task 2: Extend ShowAppListCommand with Format Options

**Status:** NOT STARTED  
**Effort:** 2h  
**Priority:** HIGH  
**Dependencies:** Task 1

**Approach:** Instead of creating a new command, extend the existing `ShowAppListCommand` and `AppListOptions` to support additional output formats while maintaining backward compatibility.

**Subtasks:**

2.1 Extend AppListOptions class
   - File: `clio/Command/ShowAppListCommand.cs`
   - Add new options to `AppListOptions`:
     - `-f|--format` output format option: `json` (default), `table`, `raw`
     - Keep existing `-s|--short` flag for backward compatibility
   - Keep positional `Name` parameter for environment selection

2.2 Extend ShowAppListCommand execution logic
   - Update `ShowSettingsTo()` method calls or implement new output logic
   - Support format parameter in display logic
   - Handle format precedence: explicit `-f` > existing behavior
   - Add JSON formatter (enhanced serialization)
   - Add table formatter (using existing `ConsoleTables`)
   - Add raw formatter (unformatted output)

2.3 Implement sensitive data masking
   - Mask `Password` field (show as `****`)
   - Mask `ClientSecret` field (show as `****`)
   - Keep other fields visible (Uri, Login, Maintainer, etc.)
   - Apply masking in all output formats

2.4 Maintain backward compatibility
   - `clio envs` - Show all environments (existing behavior)
   - `clio envs prod` - Show specific environment (existing)
   - `clio envs -s` - Show short format (existing)
   - `clio envs prod -f json` - NEW: JSON format
   - `clio envs prod -f table` - NEW: Table format
   - `clio envs --raw` - NEW: Raw output

**Acceptance Criteria:**
- [ ] All new command variations work
- [ ] Backward compatibility maintained (existing commands work as before)
- [ ] Options are properly parsed and validated
- [ ] Single environment retrieval works with new formats
- [ ] All environments retrieval works with new formats
- [ ] Output is properly formatted in all modes
- [ ] Sensitive data is masked in all formats

---

### Task 3: Register Extended Command in Dependency Injection

**Status:** NOT STARTED  
**Effort:** 0.5h  
**Priority:** LOW  
**Dependencies:** Task 2

**Note:** `ShowAppListCommand` is already registered in `BindingsModule`, so no new registration needed. If needed, verify existing registration is correct.

**Subtasks:**

3.1 Verify existing registration
   - File: `clio/BindingsModule.cs`
   - Confirm `ShowAppListCommand` is registered
   - Confirm `ISettingsRepository` is registered
   - No new changes typically needed

**Acceptance Criteria:**
- [ ] Command appears in `clio --help`
- [ ] `clio envs --help` shows new format options
- [ ] Dependencies are properly injected

---

### Task 4: Create Unit Tests

**Status:** NOT STARTED  
**Effort:** 3h  
**Priority:** HIGH  
**Dependencies:** Task 2, Task 3

**Subtasks:**

4.1 Create test fixture
   - File: `clio.tests/Command/ShowAppListCommand.Tests.cs` (extend existing if exists)
   - Inherit from `BaseCommandTests<AppListOptions>`
   - Set up mocks for `ISettingsRepository` and `ILogger`

4.2 Test option parsing
   - Positional argument parsing (environment name)
   - Format option (`-f|--format`)
   - Short flag (`-s|--short`)

4.3 Test environment resolution
   - Get single environment by name
   - Get all environments
   - Default environment selection
   - Error: environment not found

4.4 Test output formatting
   - JSON format produces valid JSON
   - Table format is readable
   - Raw format works
   - Password masking works in all formats
   - ClientSecret masking works in all formats

4.5 Test backward compatibility
   - `clio envs` - Still works as before
   - `clio envs prod` - Still works as before
   - `clio envs -s` - Still works as before

4.6 Test error scenarios
   - No environments configured
   - Invalid environment name
   - Invalid format option
   - Permission errors

**Test Coverage Target:** >= 85%

**Acceptance Criteria:**
- [ ] All tests pass
- [ ] Code coverage >= 85%
- [ ] Tests follow project conventions (Arrange-Act-Assert)
- [ ] Tests use NSubstitute for mocking
- [ ] Tests use FluentAssertions
- [ ] All test names descriptive with [Description] attributes
- [ ] Backward compatibility tests pass

---

### Task 5: Update Documentation

**Status:** NOT STARTED  
**Effort:** 2h  
**Priority:** MEDIUM  
**Dependencies:** Task 2, Task 4

**Subtasks:**

5.1 Update Commands.md
   - File: `clio/Commands.md`
   - Find "show-web-app-list" / "envs" command section
   - Add new format options to documentation
   - Add examples for new output formats
   - Document sensitive data masking

5.2 Update help text
   - File: `clio/Command/ShowAppListCommand.cs`
   - Update verb HelpText
   - Add format option descriptions
   - Update Option descriptions

5.3 Create usage examples
   - Show JSON output example
   - Show table output example
   - Show raw output example
   - Show masking examples

**Acceptance Criteria:**
- [ ] Documentation is complete and accurate
- [ ] All examples work as documented
- [ ] Markdown formatting is correct
- [ ] Consistent with existing documentation style

---

### Task 6: Integration Testing

**Status:** NOT STARTED  
**Effort:** 1.5h  
**Priority:** MEDIUM  
**Dependencies:** Task 2, Task 3, Task 4

**Subtasks:**

6.1 Manual integration testing
   - Test with real clio configuration
   - Test all command variations:
     - `clio envs`
     - `clio envs prod`
     - `clio envs prod -f json`
     - `clio envs prod -f table`
     - `clio envs -f json`
     - `clio envs -s`
   - Test all output formats

6.2 Edge case testing
   - Empty environment list
   - Single environment
   - Many environments (>20)
   - Special characters in environment names
   - Special characters in URLs

6.3 Performance testing
   - Measure response time for single environment
   - Measure response time for all environments
   - Verify no memory leaks

**Acceptance Criteria:**
- [ ] All variations work correctly
- [ ] Response time < 100ms
- [ ] No errors or exceptions
- [ ] Output is properly formatted in all cases

---

### Task 7: Code Review and Refinement

**Status:** NOT STARTED  
**Effort:** 1h  
**Priority:** MEDIUM  
**Dependencies:** Tasks 2-6

**Subtasks:**

7.1 Code review
   - Check Microsoft coding style compliance
   - Verify proper error handling
   - Check logging is appropriate
   - Verify security considerations

7.2 Performance optimization
   - Profile command execution
   - Optimize if needed

7.3 Refactoring
   - Extract common patterns
   - Improve readability
   - Add comments where needed

**Acceptance Criteria:**
- [ ] Code follows project conventions
- [ ] No code quality issues
- [ ] Performance is acceptable
- [ ] Security is maintained

---

## Implementation Details

### Revised Approach: Extend Existing Command

Instead of creating a new command, we extend the **existing** `ShowAppListCommand` to support additional output formats. This approach:

✅ **Advantages:**
- Minimal changes (only to one command class)
- No new file creation
- Backward compatibility guaranteed
- Less DI registration needed
- Simpler testing
- Cleaner codebase

**File Changes:**
```
clio/
├── Command/
│   └── ShowAppListCommand.cs                 [MODIFIED - Add format options]
├── Commands.md                               [MODIFIED - Update documentation]
└── clio/Environment/ConfigurationOptions.cs  [REFERENCE - Use existing SettingsRepository]

clio.tests/
└── Command/
    └── ShowAppListCommand.Tests.cs           [EXTENDED - Add format tests]
```

### Current Implementation

**ShowAppListCommand.cs** (Current State):
```csharp
[Verb("show-web-app-list", Aliases = new string[] { "envs" ,"show-web-app" })]
public class AppListOptions {
    [Value(0, MetaName = "App name", Required = false)]
    public string Name { get; set; }
    
    [Option('s', "short", Required = false)]
    public bool ShowShort { get; set; }
}

public class ShowAppListCommand : Command<AppListOptions> {
    private readonly ISettingsRepository _settingsRepository;
    
    public override int Execute(AppListOptions options) {
        _settingsRepository.ShowSettingsTo(Console.Out, options.Name, options.ShowShort);
        return 0;
    }
}
```

**Proposed Changes:**
- Add `Format` option to `AppListOptions`
- Implement format handlers (JSON, Table, Raw)
- Add sensitive data masking logic
- Keep all existing behavior intact
```

### Dependencies

**Internal:**
- `ISettingsRepository` - For environment data access
- `ILogger` - For console output
- `ConsoleTables` - For table formatting (already in project)

**External:**
- Newtonsoft.Json - For JSON serialization (already in project)
- CommandLine parser - For option parsing (already in project)

### Build Process

1. Code compiles in Debug and Release configurations
2. Unit tests pass
3. Code analysis passes (SonarLint rules)
4. No warnings or errors

### Deployment

1. Changes included in next NuGet release
2. Updated version number in `clio.csproj`
3. Changelog updated in `RELEASE.md`
4. Documentation published

---

## Risk Assessment

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Option parsing conflicts | Low | Medium | Thorough testing of all option combinations |
| Performance with large configs | Low | Low | Caching, lazy loading if needed |
| Sensitive data exposure | Low | High | Comprehensive masking, security review |

### Schedule Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|-----------|
| Scope creep (YAML, filters) | Medium | Medium | Focus on MVP first, defer enhancements |
| Testing takes longer | Medium | Low | Start testing early, use test templates |

---

## Success Criteria

### Functional
- ✅ `clio env {name}` retrieves specific environment
- ✅ `clio env -e {name}` alternative syntax works
- ✅ `clio env` shows all environments
- ✅ Multiple output formats supported
- ✅ Sensitive data is masked
- ✅ All test cases pass

### Quality
- ✅ Test coverage >= 85%
- ✅ Code follows project conventions
- ✅ No code quality issues
- ✅ Performance acceptable (< 100ms)

### Documentation
- ✅ Command documentation complete
- ✅ Examples work as documented
- ✅ Updated Commands.md
- ✅ Consistent with project style

### Backward Compatibility
- ✅ No changes to existing commands
- ✅ No breaking changes
- ✅ All existing functionality preserved

---

## Timeline Estimate

**Revised (Extending Existing Command):**

| Task | Hours | Dependencies | Priority |
|------|-------|--------------|----------|
| 1. Analysis | 2 | None | HIGH |
| 2. Extend ShowAppListCommand | 2 | Task 1 | HIGH |
| 3. Verify DI Registration | 0.5 | Task 2 | LOW |
| 4. Unit Tests | 3 | Task 2-3 | HIGH |
| 5. Update Documentation | 2 | Task 4 | MEDIUM |
| 6. Integration Testing | 1.5 | Task 4-5 | MEDIUM |
| 7. Code Review & Polish | 1 | Task 6 | MEDIUM |
| **Total** | **12** | | |

**Timeline: 4 weeks (part-time), 1 week (full-time)**

**Effort Reduction:** From 16h → 12h (25% reduction)
- No new command class needed
- Reduced DI configuration
- Fewer test cases (backward compat built-in)
- Simpler documentation updates

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| 2025-12-11 v2 | 1.1 | AI Assistant | Updated approach: extend existing command instead of creating new |
| 2025-12-11 v1 | 1.0 | AI Assistant | Initial plan created |

---

## Appendix: Why Extend vs. Create New?

### Analysis of Existing Command
The `ShowAppListCommand` already provides:
- ✅ Environment listing and filtering
- ✅ Access to `ISettingsRepository` 
- ✅ Support for single/multiple environments
- ✅ Basic output formatting (short form)
- ✅ Registered in DI container

### Decision: Extend vs. Create
**Option A: Create New Command** (Original Plan)
- Pros: Clean separation, new verb name (`env`)
- Cons: Duplication, more DI config, more testing, more docs

**Option B: Extend Existing** (Revised Plan) ✅ CHOSEN
- Pros: Less code, reuse infrastructure, simpler, less duplication
- Cons: Need backward compatibility (not a problem)

### Command After Enhancement
```bash
# Existing (still work)
clio envs                           # List all
clio envs prod                      # Show prod environment
clio envs -s                        # Short format

# New (after enhancement)
clio envs prod -f json              # JSON format
clio envs prod -f table             # Table format
clio envs -f json                   # All envs in JSON
clio envs --raw                     # Raw output
```

---

## Appendix: Related Issues/PRs

- Related Spec: `spec/env-info-spec.md`
- Depends on: Current codebase state
- Blocks: None

---

## Notes for Implementer

### Key Considerations

1. **Backward Compatibility First**
   - Don't modify `ShowAppListCommand`
   - Use different verb (`env` vs `envs`)
   - Preserve all existing behavior

2. **Security**
   - Always mask sensitive data
   - Consider audit logging
   - Review for information disclosure risks

3. **User Experience**
   - Keep command simple and intuitive
   - Follow existing command patterns
   - Provide helpful error messages

4. **Testing**
   - Use existing test patterns from codebase
   - Follow `BaseCommandTests<T>` pattern
   - Include both positive and negative cases

5. **Documentation**
   - Include practical examples
   - Document output formats
   - Explain option precedence

### Questions to Consider

- Should environment name be case-sensitive?
- Should we support wildcard matching for environment names?
- Should we add validation for environment connectivity?
- Should we support environment filtering by properties?
- Should we add JSON schema output for programmatic use?

### Future Enhancement Ideas

- [ ] YAML output format
- [ ] CSV export
- [ ] Environment comparison
- [ ] Property filtering
- [ ] Validation and connectivity checks
- [ ] Environment templates
- [ ] Auto-completion support
