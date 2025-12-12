# Clear Local Environment Command - Documentation Index

## 📋 Document Overview

This folder contains a complete specification and implementation plan for the `clio clear-local-env` command, which safely removes deleted local application environments from the system.

## 📁 Files in This Folder

| File | Purpose | Audience |
|------|---------|----------|
| **[local-env-clear.md](./local-env-clear.md)** | Core requirements and overview | Everyone - START HERE |
| **[QUICKSTART.md](./QUICKSTART.md)** | Fast reference for implementers | Developers starting implementation |
| **[local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md)** | Detailed task breakdown and timeline | Project managers, developers |
| **[local-env-clear-architecture.md](./local-env-clear-architecture.md)** | System design with diagrams | Architects, code reviewers |
| **[local-env-clear-examples.md](./local-env-clear-examples.md)** | Usage examples and scenarios | End users, QA, documentation |
| **[README.md](./README.md)** | This file | Navigation |

## 🎯 Quick Navigation by Role

### 👨‍💼 Project Manager / Team Lead
**Start here:**
1. Read [local-env-clear.md](./local-env-clear.md) - Understand requirements
2. Review "Implementation Sequence" in [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md)
3. Check "Implementation Tasks" section for timing and dependencies

**Key Info:**
- ⏱️ **Total Time**: 11-14 hours
- 📊 **Phases**: 3 phases (1h + 8h + 3h)
- 🔗 **Dependencies**: ISettingsRepository, IFileSystem, ISystemServiceManager
- ✅ **Success Criteria**: 10-point checklist included

---

### 👨‍💻 Developer / Implementer
**Start here:**
1. **Quick overview**: [QUICKSTART.md](./QUICKSTART.md) (5 min read)
2. **Detailed plan**: [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md) (Tasks 1-5)
3. **Architecture reference**: [local-env-clear-architecture.md](./local-env-clear-architecture.md) (for patterns)

**Key Sections:**
- Task breakdown (5 clear tasks with code patterns)
- Test case structure (8 test scenarios)
- Base test class pattern
- DI registration location
- Related code examples to study

**Study These First:**
- `clio/Command/ShowLocalEnvironmentsCommand.cs` - Environment enumeration
- `clio/Command/HostsCommand.cs` - Service management pattern
- `clio/Common/SystemServices/ISystemServiceManager.cs` - Service interface

---

### 🏗️ System Architect / Code Reviewer
**Start here:**
1. [local-env-clear-architecture.md](./local-env-clear-architecture.md) (complete system design)
2. [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md) (Architecture Analysis section)

**Key Diagrams:**
- Component diagram showing all dependencies
- Data flow from start to finish
- State machine for execution flow
- Class relationship diagram
- Sequence diagrams for normal and error flows
- Platform-specific behavior (Windows/Linux/macOS)

**Review Points:**
- Cross-platform service management strategy
- Error handling flow
- Dependency injection patterns
- Integration with existing commands
- Logging strategy

---

### 🧪 QA / Test Engineer
**Start here:**
1. [local-env-clear-examples.md](./local-env-clear-examples.md) - Real-world scenarios
2. [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md) - Task 4 (Test Cases)
3. [local-env-clear-architecture.md](./local-env-clear-architecture.md) - Error Handling Flow

**Test Scenarios (8+):**
- `--force` flag behavior
- Confirmation prompts (Y/N)
- Service deletion flow
- Directory deletion with errors
- Settings removal
- No deleted environments
- Multiple services
- Platform-specific behaviors

**Manual Testing:**
- See "Common Issues & Solutions" in examples document
- Exit codes: 0 (success), 1 (error), 2 (cancelled)

---

### 📚 Documentation Writer
**Start here:**
1. [local-env-clear.md](./local-env-clear.md) - Requirements
2. [local-env-clear-examples.md](./local-env-clear-examples.md) - Usage examples
3. [QUICKSTART.md](./QUICKSTART.md) - Quick reference

**Document Sections to Create:**
- User guide (basic and advanced usage)
- Troubleshooting guide
- Integration examples (CI/CD scripts)
- Configuration before/after
- Platform-specific notes

---

## 📖 Reading Paths

### "I need to understand this fully"
1. local-env-clear.md (requirements) - 10 min
2. QUICKSTART.md (overview) - 10 min
3. local-env-clear-architecture.md (design) - 20 min
4. local-env-clear-implementation-plan.md (details) - 30 min
5. local-env-clear-examples.md (usage) - 15 min
**Total: ~85 minutes**

### "I need to start implementing now"
1. QUICKSTART.md (Phase overview) - 10 min
2. local-env-clear-implementation-plan.md (Tasks 1-2) - 20 min
3. local-env-clear-architecture.md (Component diagram) - 5 min
4. Start with Task 1 code
**Total: ~35 minutes before coding**

### "I need to verify requirements"
1. local-env-clear.md (requirements) - 10 min
2. local-env-clear-implementation-plan.md (Success Criteria) - 5 min
**Total: ~15 minutes**

### "I need to test this"
1. local-env-clear-examples.md (scenarios) - 15 min
2. local-env-clear-implementation-plan.md (test cases) - 10 min
3. local-env-clear-architecture.md (error handling) - 10 min
**Total: ~35 minutes**

---

## 🔑 Key Information At A Glance

### Command Signature
```bash
clio clear-local-env [--force]
```

### Core Functionality
- ✅ Identify deleted environments
- ✅ Delete system services
- ✅ Remove directories
- ✅ Update settings
- ✅ Detailed logging

### Files to Create
1. `clio/Command/ClearLocalEnvironmentCommand.cs` (new)
2. `clio.tests/Command/ClearLocalEnvironmentCommandTests.cs` (new)

### Files to Modify
1. `clio/BindingsModule.cs` (add DI registration)
2. `clio/Commands.md` (add documentation)

### Dependencies to Inject
- `ISettingsRepository` - Manage environments
- `IFileSystem` - Delete directories
- `ISystemServiceManager` - Delete services
- `ILogger` - Output logging

### Test Coverage Required
- 8+ test cases minimum
- Base class: `BaseCommandTests<TOptions>`
- Mocking: NSubstitute
- Assertions: FluentAssertions

### Timeline
- **Phase 1 (Setup)**: 1 hour
- **Phase 2 (Core Logic)**: 8 hours
- **Phase 3 (Polish & Tests)**: 3 hours
- **Total**: 11-14 hours

---

## ✅ Success Criteria

- [ ] Command created with all options
- [ ] Environment deletion logic implemented
- [ ] Service management working on all platforms
- [ ] Directory deletion with error handling
- [ ] Settings file updated correctly
- [ ] Confirmation prompt working
- [ ] All unit tests passing (8+)
- [ ] Works on Windows, Linux, macOS
- [ ] Detailed logging at each step
- [ ] Documentation complete

---

## 🔗 Related Commands in Clio

These commands follow similar patterns you can study:

| Command | File | Learn About |
|---------|------|-------------|
| `show-local-envs` | ShowLocalEnvironmentsCommand.cs | Environment enumeration |
| `hosts` | HostsCommand.cs | Service management |
| `delete-pkg` | DeletePackageCommand.cs | Deletion with confirmation |
| `unreg-app` | UnregAppCommand.cs | Settings removal |

---

## 📞 Getting Help

### Question: Where do I start?
→ Read [QUICKSTART.md](./QUICKSTART.md)

### Question: How do I implement this?
→ See [local-env-clear-implementation-plan.md](./local-env-clear-implementation-plan.md)

### Question: What's the system design?
→ See [local-env-clear-architecture.md](./local-env-clear-architecture.md)

### Question: How do users run this command?
→ See [local-env-clear-examples.md](./local-env-clear-examples.md)

### Question: What are the requirements?
→ See [local-env-clear.md](./local-env-clear.md)

---

## 📊 Document Statistics

| Aspect | Details |
|--------|---------|
| **Total Pages** | 5 markdown files |
| **Total Lines** | ~2000+ lines of documentation |
| **Code Examples** | 20+ examples included |
| **Diagrams** | 10+ ASCII diagrams |
| **Test Cases** | 8+ defined test scenarios |
| **Implementation Tasks** | 5 detailed tasks |
| **Timeline** | 11-14 hours total |

---

## 🚀 Next Steps

1. **Choose your role** above and follow the recommended reading path
2. **Familiarize yourself** with the base classes and dependencies
3. **Review** similar implementations in the codebase
4. **Start implementing** using the detailed task breakdown
5. **Test thoroughly** using the provided test scenarios
6. **Document** for end users using the examples

---

**Last Updated**: December 2025  
**Status**: Ready for Implementation  
**Version**: 1.0  
