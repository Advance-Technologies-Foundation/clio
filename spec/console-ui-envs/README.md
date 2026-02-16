# Console UI Environment Manager - Feature Documentation

## Quick Links

- **[Specification](./env-manage-ui-spec.md)** - Detailed feature requirements and design
- **[Implementation Plan](./env-manage-ui-plan.md)** - Step-by-step development guide
- **[Technical Architecture](./env-manage-ui-architecture.md)** - System design and patterns
- **[QA Test Plan](./env-manage-ui-qa.md)** - Comprehensive testing strategy

## Feature Overview

Interactive console UI for managing Creatio environment configurations in clio's `appsettings.json` file. Built with Spectre.Console library for rich terminal experience.

### Key Capabilities

- ✅ **List Environments** - View all configured environments in a table
- ✅ **View Details** - Inspect complete environment configuration
- ✅ **Create** - Add new environments with guided prompts
- ✅ **Edit** - Modify existing environment settings
- ✅ **Delete** - Remove environments with confirmation
- ✅ **Set Active** - Change the default environment

### Why This Feature?

**Problem**: Manual JSON editing is error-prone and intimidating for users

**Solution**: Visual, interactive console UI with:
- Input validation
- Guided workflows
- Clear error messages
- Secure password handling

## Quick Start

### Installation

1. Feature will be available in clio v8.x.x+
2. No additional setup required

### Usage

```bash
# Launch interactive UI
clio env-ui

# Or use alias
clio ui
```

### Basic Workflow

1. **Launch**: `clio env-ui`
2. **Navigate**: Use ↑/↓ arrow keys
3. **Select**: Press Enter
4. **Return**: Press Esc
5. **Exit**: Select "Exit" or press 'q'

## Screenshots (ASCII Art)

### Main Menu
```
┌─────────────────────────────────────────────────────────┐
│           Clio Environment Manager                       │
├─────────────────────────────────────────────────────────┤
│ Config File: /Users/user/.clio/appsettings.json        │
└─────────────────────────────────────────────────────────┘

┌─────┬──────────────┬─────────────────────────┬──────────┐
│ #   │ Name         │ URL                     │ IsNetCore│
├─────┼──────────────┼─────────────────────────┼──────────┤
│ 1 * │ dev          │ https://dev.creatio.com │ Yes      │
│ 2   │ prod         │ https://app.creatio.com │ Yes      │
└─────┴──────────────┴─────────────────────────┴──────────┘

What would you like to do?
  > List Environments
    View Environment Details
    Create New Environment
    Edit Environment
    Delete Environment
    Set Active Environment
    Exit
```

## Technical Stack

### Dependencies

- **Spectre.Console** v0.49.1+ - Rich console UI
- **Existing clio infrastructure**:
  - `ISettingsRepository` - Configuration management
  - `EnvironmentSettings` - Data model
  - Command pattern - Consistent interface

### Architecture

```
EnvManageUiCommand (Entry Point)
    ↓
Spectre.Console (UI Layer)
    ↓
EnvManageUiService (Business Logic)
    ↓
ISettingsRepository (Data Access)
    ↓
appsettings.json (Persistence)
```

See [Technical Architecture](./env-manage-ui-architecture.md) for details.

## Development Status

| Phase | Status | Completion |
|-------|--------|------------|
| Specification | ✅ Complete | 100% |
| Implementation | 🔲 Planned | 0% |
| Testing | 🔲 Planned | 0% |
| Documentation | ✅ Complete | 100% |
| Release | 🔲 Planned | 0% |

## Project Timeline

- **Specification**: ✅ Complete
- **Implementation**: Estimated 21.5 hours
- **Testing**: Estimated 5 hours
- **Total**: ~27 hours (~4 working days)

## Contributing

### For Developers

1. Read [Implementation Plan](./env-manage-ui-plan.md)
2. Follow phase-by-phase approach
3. Write tests alongside code
4. Follow clio coding standards

### For QA

1. Use [QA Test Plan](./env-manage-ui-qa.md)
2. Test on all platforms (Windows, macOS, Linux)
3. Report bugs with test case ID
4. Verify security requirements

### For Documentation

1. Update Commands.md
2. Create user guide with examples
3. Add troubleshooting section
4. Include screenshots/GIFs

## Key Design Decisions

### Why Spectre.Console?

- ✅ Rich terminal UI components
- ✅ Cross-platform support
- ✅ Excellent keyboard navigation
- ✅ Active development and support
- ✅ MIT license

### Why Interactive vs CLI Flags?

- **Interactive**: Better UX for exploration, guided workflows
- **CLI Flags**: Better for automation, scripting
- **Solution**: Keep both! This feature complements existing commands

### Why No Breaking Changes?

- Existing commands remain unchanged
- New feature is additive
- appsettings.json schema unchanged
- Backward compatible

## Security

### Sensitive Data Protection

- **Display**: Passwords always masked as `****`
- **Input**: Password fields use secret input (masked)
- **Logging**: No sensitive data in logs
- **Storage**: Uses existing secure storage mechanism

### Validation

- Environment name: Alphanumeric, underscore, hyphen only
- URL: Valid HTTP/HTTPS URLs only
- No path traversal in workspace paths
- SQL injection prevention (parameterized queries)

## Testing Strategy

### Unit Tests (95%+ Coverage)

- Input validation
- Business logic
- Error handling
- Mock external dependencies

### Integration Tests

- Full CRUD workflows
- File I/O operations
- Multi-step scenarios

### Manual Tests

- UI rendering on all platforms
- Keyboard navigation
- Edge cases (0 envs, 100+ envs)
- Performance testing

See [QA Test Plan](./env-manage-ui-qa.md) for 33 detailed test cases.

## Known Limitations

1. **Single-User**: No concurrent editing support
2. **Console-Only**: No graphical UI
3. **No Undo**: Changes are immediate
4. **No Backup**: Users should backup appsettings.json

## Future Enhancements (Post v1)

- Search/filter environments
- Import/export environments
- Environment templates
- Bulk operations
- Environment comparison
- Health check integration
- Clone environment
- Environment validation wizard

## Troubleshooting

### Issue: UI doesn't display correctly

**Solution**: Ensure terminal supports ANSI colors
```bash
# Test ANSI support
echo -e "\e[31mRed Text\e[0m"
```

### Issue: Permission denied saving config

**Solution**: Check file permissions
```bash
chmod 644 ~/.clio/appsettings.json
```

### Issue: Corrupted JSON

**Solution**: Restore from backup or reset
```bash
# Backup first
cp ~/.clio/appsettings.json ~/.clio/appsettings.json.backup

# Reset to defaults
clio env-ui  # Will offer to reset
```

## FAQ

**Q: Will this replace existing environment commands?**  
A: No, all existing commands (`reg-web-app`, `show-web-app-list`, etc.) remain for scripting.

**Q: Is this secure for production credentials?**  
A: Yes, same security as existing clio configuration. Passwords are masked in display.

**Q: Can I use this in CI/CD?**  
A: No, this is interactive. Use existing CLI commands for automation.

**Q: What terminals are supported?**  
A: Any terminal with ANSI color support. Tested on CMD, PowerShell, Terminal, iTerm2, bash, zsh.

**Q: How do I report bugs?**  
A: Use GitHub issues with `env-ui` label and include test case ID if applicable.

## References

### External

- [Spectre.Console Documentation](https://spectreconsole.net/)
- [ANSI Escape Codes](https://en.wikipedia.org/wiki/ANSI_escape_code)
- [Repository Pattern](https://martinfowler.com/eaaCatalog/repository.html)

### Internal

- [Commands.md](../../clio/Commands.md) - All clio commands
- [ISettingsRepository](../../clio/Environment/ISettingsRepository.cs)
- [EnvironmentSettings](../../clio/Environment/ConfigurationOptions.cs)
- [ShowAppListCommand](../../clio/Command/ShowAppListCommand.cs)

## Changelog

### v1.0.0 (Planned)
- Initial release
- Core CRUD operations
- Cross-platform support
- Security features

## License

Same as clio project (MIT License)

## Contacts

- **Feature Owner**: [TBD]
- **Lead Developer**: [TBD]
- **QA Lead**: [TBD]

---

**Last Updated**: 2026-01-29  
**Status**: Specification Complete  
**Next Step**: Implementation Phase 1
