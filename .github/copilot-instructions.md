## How# GitHub Copilot Custom Instructions for Clio Project

## Project Context
This is the Clio project - a command-line tool for Creatio (formerly bpm'online) development. The project includes CI/CD automation, package management, and development tools.

## Available Commands

### `/check_pr` - Enhanced PR Analysis and Release Planning
Provides comprehensive Pull Request monitoring and release readiness analysis.

**Basic Usage:** Analyzes current PR status, CI/CD pipelines, and action items
**Release Planning:** Uses 100-point scoring system to categorize PRs by release readiness

**Key Features:**
- **Release Readiness Scoring**: 100-point system based on mergeable status (40pts), review status (30pts), CI/CD results (20pts), and base points (10pts)
- **Three-Tier Classification**: Ready (≥70), Needs Attention (40-69), Not Ready (<40)
- **Cross-Platform**: Works on Windows (PowerShell) and macOS/Linux (Bash)
- **Filtering Options**: By author, label, state, or PR count
- **Export and Watch**: Save reports to files or monitor in real-time

**Usage Examples:**
- `/check_pr` - Standard PR analysis
- `/check_pr for release planning` - Enhanced scoring and categorization  
- `/check_pr by author kirillkrylov` - Filter by specific author
- `/check_pr watch mode` - Continuous monitoring

**Available Scripts:**
- `check-pr.sh` / `check-pr.ps1` - Basic PR monitoring
- `check-pr-release-final.sh` - Enhanced release planning analysis

### `/release` - Release Management
Automates version management and release creation with comprehensive changelog generation.

**Usage:** `/release` followed by release type or specific instructions
- Semantic versioning support (major.minor.patch.build)
- Automated changelog generation from commits and PRs
- Integration with CI/CD pipelines for NuGet publishing
- Cross-platform PowerShell and Bash scripts

## Code Analysis Guidelines

### When working with Clio codebase:
1. **Architecture Understanding**: Clio follows a command-based architecture with dependency injection
2. **Command Pattern**: New features should follow the existing command pattern in `/Command` folder
3. **Testing**: Maintain test coverage in `clio.tests` project
4. **Cross-Platform**: Consider Windows, macOS, and Linux compatibility
5. **Version Management**: Use X.Y.Z.W format for versioning

### Key Directories:
- `/clio/Command/` - Command implementations
- `/clio/Common/` - Shared utilities and services
- `/clio.tests/` - Unit tests
- `/.github/workflows/` - CI/CD pipelines
- `/.github/prompts/` - GitHub Copilot instructions

## Release Planning Best Practices

### Use Enhanced PR Analysis When:
- Planning a release or deployment
- Deciding which PRs to merge
- Assessing release readiness
- Prioritizing development work

### Scoring Interpretation:
- **Ready PRs (≥70)**: Include in next release
- **Needs Attention (40-69)**: Consider quick fixes for inclusion
- **Not Ready (<40)**: Defer to future releases

### Priority Factors:
1. **Security fixes**: Always prioritize regardless of score
2. **Bug fixes**: High priority for stability
3. **Features**: Balance business value with readiness
4. **Dependencies**: Carefully review for breaking changes
5. **Documentation**: Usually safe if basic checks pass

## CI/CD Integration

### Workflows:
- `reliase-to-nuget.yml` - Automated NuGet package publishing with version synchronization
- Version consistency between application and NuGet package ensured through tag-based versioning

### Version Management:
- Uses GitHub releases and tags for version control
- Automatic project file updates during release process
- Integration with package publishing pipelines

## Development Workflow

### PR Management:
1. Use `/check_pr` for regular monitoring
2. Apply enhanced analysis before releases
3. Address high-scoring PRs first
4. Maintain clean merge history

### Release Process:
1. Run release readiness analysis
2. Review and merge ready PRs
3. Use `/release` command for version management
4. Validate CI/CD pipeline execution

## Communication Style

### When providing PR analysis:
- Start with executive summary (ready/attention/not ready counts)
- Categorize PRs by readiness level
- Provide specific action items for each PR
- Include business impact assessment when relevant
- Suggest concrete next steps

### When discussing releases:
- Focus on risk assessment
- Highlight breaking changes
- Consider rollback strategies
- Document decisions and rationale

## Error Handling

### Common Issues:
- GitHub CLI authentication: Guide to `gh auth login`
- Script permissions: Provide `chmod +x` instructions
- Missing dependencies: Auto-detect and suggest installation
- Network issues: Provide troubleshooting steps

### Troubleshooting Approach:
1. Verify prerequisites (GitHub CLI, permissions)
2. Test basic connectivity (`gh repo view`)
3. Check script execution permissions
4. Provide alternative approaches when possibleto interact with a user:
- Answer all questions in the style of a friendly colleague, using informal language
- Use @terminal when answering questions about Git.

## Available Commands:
- `/release` - Automatically increment minor version from latest tag and create new release tag (e.g., 8.0.1.42 → 8.0.1.43)
- `/check_pr` - Check Pull Requests status, review state, and CI/CD pipeline results for the clio repository

## When generating code always follow these guidelines:
For commands that require cliogate to be installed, always include a check for its presence and display a message to the user if it is not installed. Follow the pattern used in existing commands.
Use Microsoft coding style for all code unless otherwise specified.

All filesystem operations must use the filesystem abstraction already used in the project. This ensures unit tests can be written without depending on the real filesystem.

Use Microsoft coding style for all code unless otherwise specified.


Every time you change code, always check the documentation in README.md and Commands.md. Update, add, or remove documentation as needed to keep it complete and up-to-date.

## Unit tests
- Tests should be compatible with Windows, MacOS, and Linux. 
- Command test in clio **MUST** inherit from BaseCommandTests<T> where T is the option for the command.
  This base test will check that the command has proper description in documentation.
  Make sure that command under test has a proper description in the documentation.
- Use NSubstitute for mocking dependencies.
- Use FluentAssertions for assertions.
- Always use Arrange-Act-Assert pattern in tests.
- Whenever possible, always use "because" argument for all assertions. For example:
  ```csharp
  result.Should().Be(0, "explain why the result should be 0");
  ```
- Tests should be decorated with Description attribute, explaining what the test is doing.
 ```csharp
[Test]
[Description("Descrites the tests purpose")]
public void TestName()
 ```
- When designing unit tests, always apply equivalence class partitioning, boundary value analysis, and pairwise testing principles to ensure comprehensive coverage of all possible input combinations and edge cases.


## Project structure
- Main clio console application [clio](./../clio/clio.csproj)
- [Clio.Tests](./../Clio.Tests/Clio.Tests.csproj) This project is a unit test project for the clio console application.
- [cliogate](./../../clio/cliogate/cliogate.csproj) is an extension that is installed on Creatio. This project extends Creatio's capabilities.
- [cliogate.tests](./../../clio/cliogate.tests/cliogate.tests.csproj) is a unit test project for the cliogate extension.



## CI/CD
- The CI/CD pipeline is configured in the `.github/workflows` directory.
- Repository has runners that run on windows in the corporate infrastructure.
- The CI/CD pipeline builds the solution, runs unit tests, and publishes the `clio