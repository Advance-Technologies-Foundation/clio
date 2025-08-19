---
description: Validate a user guide against CLI tool source code for accuracy and completeness
allowed-tools: Read, Grep, Bash, Write, mcp__context7__resolve-library-id, mcp__context7__get-library-docs, mcp__microsoft-docs__microsoft_docs_search, mcp__microsoft-docs__microsoft_docs_fetch
---

# Validate CLIO Tool Guide

Thoroughly validate the provided guide against the actual clio tool source code to ensure accuracy, completeness, and usability.

## Guide to Validate

Please provide the guide content (paste it, reference a file with @filename, or specify a URL to fetch).

Guide content: $ARGUMENTS

## Validation Process

Execute this comprehensive validation workflow:

### 1. Guide Analysis
- Read and parse the entire guide content
- Extract all CLIO commands, subcommands, flags, and arguments mentioned
- Identify claimed features, behaviors, outputs, and examples
- Note any version-specific information or requirements

### 2. Source Code Discovery
- Locate the main CLIO entry point clio/Program.cs
- Find command parsing logic, usually a command is implement in a command class with arguments in the options class
```csharp
public class AddSchemaCommand : Command<AddSchemaOptions>
```
- Map the complete command structure from source code
- Identify help text, error messages, and output formatting

### 3. Systematic Verification

#### Command Structure Validation
- Verify each command and subcommand exists in the source
- Check command hierarchy and nesting is correctly documented
- Validate command aliases and shortcuts

#### Argument & Flag Verification
- Confirm all flags (short and long forms) are implemented
- Verify required vs. optional argument classification
- Check default values match source code
- Validate argument types and acceptable values
- Identify any mutual exclusivity or dependency rules

#### Example Testing
- Test all provided examples for syntax correctness
- Verify example outputs match actual behavior (when safe)
- Check that examples progress logically from basic to advanced

#### Output & Behavior Checks
- Compare documented outputs with actual command results
- Verify error messages and scenarios
- Check exit codes and return values
- Validate any mentioned configuration files or environment variables

### 4. Generate Comprehensive Report

Provide detailed feedback including:

**✅ Verified Accurate Content**
- Commands, flags, and behaviors confirmed correct
- Well-crafted examples and explanations

**⚠️ Issues Requiring Correction**
- Specific location of each issue in the guide
- Clear description of the problem
- Exact correction needed with source code reference
- Severity level (critical/moderate/minor)

**💡 Enhancement Suggestions**
- Missing important commands or flags
- Opportunities for clearer examples
- Additional use cases to cover
- Version compatibility notes

**🔍 Manual Verification Needed**
- Elements requiring external dependencies
- Platform-specific features
- Destructive operations requiring caution

### 5. Priority Recommendations

Rank findings by impact:
1. **Critical**: Incorrect syntax that would cause user failures
2. **Important**: Missing key functionality or misleading information  
3. **Minor**: Style improvements or additional helpful details

## Expected Deliverables

- Complete validation report with specific, actionable feedback
- Reference to relevant source code files for each finding
- Suggested corrections with proper CLI syntax
- Overall assessment of guide quality and reliability

Focus on ensuring users following this guide will have an accurate, smooth experience with the CLI tool.