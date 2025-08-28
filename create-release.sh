#!/bin/bash

# create-release.sh - Automatically creates a new release by incrementing the minor version

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

# Default values
FORCE=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -f|--force)
            FORCE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  -f, --force    Skip confirmation prompt"
            echo "  -h, --help     Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

get_latest_tag() {
    git describe --tags --abbrev=0 2>/dev/null || echo ""
}

test_version_format() {
    local version=$1
    if [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        return 0
    else
        return 1
    fi
}

get_next_version() {
    local current_version=$1
    
    if ! test_version_format "$current_version"; then
        echo "Invalid version format: $current_version. Expected format: X.Y.Z.W" >&2
        exit 1
    fi
    
    IFS='.' read -ra PARTS <<< "$current_version"
    local major=${PARTS[0]}
    local minor=${PARTS[1]}
    local patch=${PARTS[2]}
    local build=${PARTS[3]}
    
    # Increment the build number (minor version)
    local next_build=$((build + 1))
    
    echo "$major.$minor.$patch.$next_build"
}

create_release_tag() {
    local version=$1
    local force=$2
    
    echo -e "${GREEN}🏷️  Creating new tag: $version${NC}"
    
    if [[ "$force" != "true" ]]; then
        echo -n "Do you want to create and push tag '$version' and create GitHub release? (y/N): "
        read -r confirmation
        if [[ ! $confirmation =~ ^[Yy] ]]; then
            echo -e "${YELLOW}❌ Tag creation cancelled${NC}"
            return 1
        fi
    fi
    
    # Create tag
    if ! git tag "$version"; then
        echo -e "${RED}❌ Failed to create tag${NC}" >&2
        return 1
    fi
    
    # Push tag
    if ! git push origin "$version"; then
        echo -e "${RED}❌ Failed to push tag${NC}" >&2
        return 1
    fi
    
    echo -e "${GREEN}✅ Successfully created and pushed tag: $version${NC}"
    
    # Try to create GitHub release using gh CLI
    echo -e "${CYAN}🚀 Creating GitHub release...${NC}"
    if command -v gh >/dev/null 2>&1; then
        if gh release create "$version" --title "Release $version" --notes "Automated release $version" >/dev/null 2>&1; then
            echo -e "${GREEN}✅ Successfully created GitHub release: $version${NC}"
        else
            echo -e "${YELLOW}⚠️  Could not create GitHub release (authentication issue or release already exists)${NC}"
            echo -e "${BLUE}📝 Please check: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version${NC}"
        fi
    else
        echo -e "${YELLOW}⚠️  GitHub CLI (gh) not found. Skipping automatic release creation.${NC}"
        echo -e "${BLUE}📝 Please create release manually at: https://github.com/Advance-Technologies-Foundation/clio/releases/new?tag=$version${NC}"
    fi
    
    echo -e "${CYAN}🚀 Release workflow will be triggered automatically${NC}"
    return 0
}

# Main execution
main() {
    echo -e "${CYAN}🔍 Getting latest release tag...${NC}"
    
    current_tag=$(get_latest_tag)
    
    if [[ -z "$current_tag" ]]; then
        echo -e "${YELLOW}⚠️  No existing tags found. Starting with version 8.0.1.1${NC}"
        new_version="8.0.1.1"
    else
        echo -e "${WHITE}📍 Current latest tag: $current_tag${NC}"
        
        # Remove 'v' prefix if present
        clean_version=${current_tag#v}
        
        if ! test_version_format "$clean_version"; then
            echo -e "${RED}Current tag '$current_tag' has invalid format. Expected: X.Y.Z.W${NC}" >&2
            exit 1
        fi
        
        new_version=$(get_next_version "$clean_version")
    fi
    
    echo -e "${GREEN}🎯 Next version will be: $new_version${NC}"
    
    if create_release_tag "$new_version" "$FORCE"; then
        echo ""
        echo -e "${CYAN}📋 Summary:${NC}"
        echo -e "${WHITE}   ✅ Tag '$new_version' created and pushed${NC}"
        echo -e "${WHITE}   ✅ GitHub release created (if gh CLI available)${NC}"
        echo -e "${WHITE}   🚀 NuGet package will be published automatically${NC}"
        echo ""
        echo -e "${BLUE}🔗 Monitor progress at: https://github.com/Advance-Technologies-Foundation/clio/releases${NC}"
    else
        exit 1
    fi
}

main "$@"
