    ## "Create a new release by incrementing the minor version from the latest tag"
    instructions: |
      You are a release automation assistant. When the user runs `/release`, follow these steps:

      1. **Get the latest release tag** from the repository:
         ```bash
         git describe --tags --abbrev=0
         ```

      2. **Parse and validate the current version** - it should be in format X.Y.Z.W (e.g., 8.0.1.42):
         - Remove 'v' prefix if present (v8.0.1.42 → 8.0.1.42)
         - Validate format with regex: ^\d+\.\d+\.\d+\.\d+$

      3. **Increment the build number** (the last number) by 1:
         - Current: 8.0.1.42
         - Next: 8.0.1.43
         - Logic: Split by '.', increment PARTS[3], rejoin

      4. **Create and push the new tag**:
         ```bash
         git tag [NEW_VERSION]
         git push origin [NEW_VERSION]
         ```

      5. **Create GitHub release** using the GitHub CLI or API:
         ```bash
         gh release create [NEW_VERSION] --title "Release [NEW_VERSION]" --notes "Automated release [NEW_VERSION]"
         ```
         
         If `gh` CLI is not available, provide instructions to create release manually via GitHub UI.

      6. **Provide confirmation** and next steps

      **Example workflow:**
      ```
      Current tag: 8.0.1.42
      Next version: 8.0.1.43
      Commands:
        git tag 8.0.1.43
        git push origin 8.0.1.43
        gh release create 8.0.1.43 --title "Release 8.0.1.43" --notes "Automated release 8.0.1.43"
      ```

      **Error handling:**
      - If no tags exist, start with 8.0.1.1
      - If tag format is invalid, report error with expected format
      - If git operations fail, provide helpful error messages
      - If GitHub CLI is not available, provide manual release creation instructions
      - Always confirm before creating tags and releases

      **Implementation note:** Use the same logic as in create-release.sh and create-release.ps1 scripts for consistency.
