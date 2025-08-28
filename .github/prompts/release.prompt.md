    ## "Create a new release by incrementing the minor version from the latest tag"
    instructions: |
      You are a release automation assistant. When the user runs `/release`, follow these steps:

      1. **Check and setup GitHub CLI first**:
         ```bash
         # Check if gh CLI is installed
         if ! command -v gh >/dev/null 2>&1; then
           echo "GitHub CLI not found. Installing..."
           # Auto-detect OS and install
         fi
         
         # Verify authentication
         gh auth status
         ```
         
         **Auto-install GitHub CLI (if missing):**
         - **macOS:** `brew install gh`
         - **Windows:** `winget install --id GitHub.cli` or `choco install gh`
         - **Linux:** Check https://github.com/cli/cli/blob/trunk/docs/install_linux.md
         
         **If installation fails:** Provide manual link and continue with tag creation only

      2. **Get the latest release tag** from the repository:
         ```bash
         git describe --tags --abbrev=0
         ```

      3. **Parse and validate the current version** - it should be in format X.Y.Z.W (e.g., 8.0.1.42):
         - Remove 'v' prefix if present (v8.0.1.42 → 8.0.1.42)
         - Validate format with regex: ^\d+\.\d+\.\d+\.\d+$

      4. **Increment the build number** (the last number) by 1:
         - Current: 8.0.1.42
         - Next: 8.0.1.43
         - Logic: Split by '.', increment PARTS[3], rejoin

      5. **Update project version** in clio.csproj:
         - Update AssemblyVersion to match the new version
         - This ensures the compiled application shows the correct version

      6. **Create and push the new tag**:
         ```bash
         git tag [NEW_VERSION]
         git push origin [NEW_VERSION]
         ```

      7. **Create GitHub release** (should work now since we checked CLI in step 1):
         ```bash
         gh release create [NEW_VERSION] --title "Release [NEW_VERSION]" --notes "Automated release [NEW_VERSION]"
         ```

      8. **CI/CD Automation**: Once the GitHub release is created, the CI/CD workflow will automatically:
         - Extract version from the release tag
         - Build clio with the extracted version (overriding project file version)
         - Run tests with the release version
         - Pack and publish NuGet package with the release version
         - This ensures both the compiled application and NuGet package have the same version from the tag

      9. **Provide confirmation** and next steps

      **Example workflow:**
      ```
      # Step 1: Setup GitHub CLI
      if ! command -v gh >/dev/null 2>&1; then
        echo "Installing GitHub CLI..."
        # macOS: brew install gh
        # Windows: winget install --id GitHub.cli
      fi
      gh auth status
      
      # Step 2-4: Version management
      Current tag: 8.0.1.42
      Next version: 8.0.1.43
      
      # Step 5-7: Update and create release
      Commands:
        # Update clio.csproj version
        sed -i 's|<AssemblyVersion[^>]*>[^<]*</AssemblyVersion>|<AssemblyVersion Condition="'\''$(AssemblyVersion)'\'' == '\'''\''">8.0.1.43</AssemblyVersion>|g' clio/clio.csproj
        git tag 8.0.1.43
        git push origin 8.0.1.43
        gh release create 8.0.1.43 --title "Release 8.0.1.43" --notes "Automated release 8.0.1.43"
      ```

      **Error handling:**
      - **Step 1:** If GitHub CLI installation fails, note this but continue with tag creation
      - If no tags exist, start with 8.0.1.1
      - If tag format is invalid, report error with expected format
      - If git operations fail, provide helpful error messages
      - If GitHub CLI is available but not authenticated, prompt for `gh auth login`
      - If GitHub CLI unavailable after installation attempt, provide manual release creation link
      - Always confirm before creating tags and releases

      **Implementation note:** 
      - **Always start with GitHub CLI check and installation** - this is the most important step
      - Detect OS (macOS/Windows/Linux) and provide appropriate installation command
      - Check authentication status: `gh auth status` after installation
      - Use the same version logic as in create-release.sh and create-release.ps1 scripts for consistency
      - Handle authentication gracefully with helpful error messages
      - Even if CLI setup fails, continue with tag creation and provide manual release link
