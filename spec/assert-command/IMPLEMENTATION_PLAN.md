# Implementation Plan: `assert` Command

## Overview
This document outlines the implementation plan for the `assert` command - an agent-friendly validation CLI that provides deterministic assertion mechanisms for Kubernetes and filesystem resources.

---

## Implementation Phases

### Phase 1: Project Structure and Core Framework
**Duration:** 2-3 days

#### Tasks:
1. **Create Command Structure**
   - [ ] Create `AssertCommand.cs` in `clio/Command/`
   - [ ] Create `AssertOptions.cs` for command options
   - [ ] Register command in dependency injection container
   - [ ] Add basic help documentation

2. **Create Core Abstractions**
   - [ ] Create `IAssertionResult` interface for structured output
   - [ ] Create `AssertionPhase` enum (discovery, connectivity, capability)
   - [ ] Create `AssertionScope` enum (k8, fs)
   - [ ] Create base `AssertionResult` class with status, failedAt, reason, details

3. **Output Infrastructure**
   - [ ] Create JSON serializer for structured output
   - [ ] Implement exit code handling (0=pass, 1=fail, 2=invalid)
   - [ ] Create output formatter for human-readable and JSON modes

#### Deliverables:
- Basic command skeleton that can be invoked
- Core result types defined
- Output infrastructure ready

---

### Phase 2: Kubernetes Context Validation (Phase 0)
**Duration:** 2-3 days

#### Tasks:
1. **Kubernetes Client Abstraction**
   - [ ] Create `IKubernetesClient` interface
   - [ ] Implement wrapper around KubernetesClientConfiguration
   - [ ] Add context resolution methods
   - [ ] Add API server connectivity check

2. **Context Validation Logic**
   - [ ] Implement `--context` flag validation
   - [ ] Implement `--context-regex` pattern matching
   - [ ] Implement `--cluster` flag validation
   - [ ] Implement `--namespace` assertion
   - [ ] Add default context retrieval (kubectl config current-context)

3. **Phase 0 Execution**
   - [ ] Create `K8ContextValidator` class
   - [ ] Implement mandatory context check before all K8 assertions
   - [ ] Add structured error messages for context failures
   - [ ] Include context metadata in successful output

#### Deliverables:
- Kubernetes context validation working
- Context assertion flags functional
- Tests for context validation scenarios

---

### Phase 3: Kubernetes Database Assertions
**Duration:** 4-5 days

#### Tasks:
1. **Database Discovery Infrastructure**
   - [ ] Create `IK8DatabaseDiscovery` interface
   - [ ] Implement label-based detection logic
   - [ ] Create detection rules:
     - Namespace: `clio-infrastructure`
     - StatefulSet workload kind
     - Label selectors: `app=clio-postgres`, `app=clio-mssql`
   - [ ] Implement pod status checking (not permanently failed)

2. **Database Presence Check (`--db`)**
   - [ ] Parse comma-separated engine list
   - [ ] Implement Postgres detection
   - [ ] Implement MSSQL detection
   - [ ] Create `--db-min` enforcement logic
   - [ ] Add structured output for discovered databases

3. **Dynamic Port Resolution**
   - [ ] Create `IK8ServiceResolver` interface
   - [ ] Implement service discovery by engine type
   - [ ] Add internal vs LoadBalancer service selection
   - [ ] Implement port resolution from `Service.spec.ports`
   - [ ] Handle single/multiple port scenarios
   - [ ] Add validation for missing services

4. **Database Connectivity (`--db-connect`)**
   - [ ] Create `IDatabaseConnectivityChecker` interface
   - [ ] Implement Postgres TCP connection check
   - [ ] Implement MSSQL TCP connection check
   - [ ] Use dynamically resolved ports
   - [ ] Add timeout handling
   - [ ] Create structured error output

5. **Database Capability Check (`--db-check version`)**
   - [ ] Create `IDatabaseCapabilityChecker` interface
   - [ ] Implement Postgres version query (`SELECT version()`)
   - [ ] Implement MSSQL version query (`SERVERPROPERTY('ProductVersion')`)
   - [ ] Add credential management
   - [ ] Include version info in output

#### Deliverables:
- Database discovery working for Postgres and MSSQL
- Dynamic port resolution functional
- All database assertion flags operational
- Comprehensive test coverage

---

### Phase 4: Kubernetes Redis Assertions
**Duration:** 2-3 days

#### Tasks:
1. **Redis Discovery (`--redis`)**
   - [ ] Implement label-based detection
   - [ ] Detection rules:
     - Namespace: `clio-infrastructure`
     - Deployment workload kind
     - Label selector: `app=clio-redis`
   - [ ] Pod status validation

2. **Redis Service Resolution**
   - [ ] Extend service resolver for Redis
   - [ ] Dynamic port resolution for Redis
   - [ ] Internal vs LoadBalancer service handling

3. **Redis Connectivity (`--redis-connect`)**
   - [ ] Implement TCP connection check to Redis
   - [ ] Use dynamically resolved port
   - [ ] Add timeout handling

4. **Redis Ping (`--redis-ping`)**
   - [ ] Implement Redis client wrapper
   - [ ] Execute PING command
   - [ ] Validate PONG response
   - [ ] Handle auth scenarios

#### Deliverables:
- Complete Redis assertion chain
- Redis flags functional
- Integration tests with real Redis instance

---

### Phase 5: Filesystem Assertions (Windows)
**Duration:** 3-4 days

#### Tasks:
1. **Filesystem Path Validation (`--path`)**
   - [ ] Create `IFilesystemValidator` interface (use existing FS abstraction)
   - [ ] Implement path existence check
   - [ ] Support for both files and directories
   - [ ] Path normalization

2. **Windows Identity Resolution (`--user`)**
   - [ ] Create `IWindowsIdentityResolver` interface
   - [ ] Implement local user resolution
   - [ ] Implement domain user resolution
   - [ ] Implement IIS AppPool identity resolution (`IIS APPPOOL\*`)
   - [ ] Add identity validation

3. **Windows ACL Evaluation (`--perm`)**
   - [ ] Create `IWindowsAclEvaluator` interface
   - [ ] Implement permission level mapping:
     - `read` → Read permissions
     - `write` → Write permissions
     - `modify` → Modify permissions
     - `full` → Full Control
   - [ ] Implement ACL rule evaluation:
     - Inheritance handling
     - Deny overrides allow
     - Effective permissions calculation
   - [ ] Add detailed failure output

4. **Platform Detection**
   - [ ] Add Windows platform check
   - [ ] Return appropriate error on non-Windows for `assert fs`

#### Deliverables:
- Filesystem assertions working on Windows
- ACL evaluation accurate
- Tests covering various permission scenarios

---

### Phase 6: Integration and Error Handling
**Duration:** 2-3 days

#### Tasks:
1. **Phase Orchestration**
   - [ ] Implement phase execution order for K8
   - [ ] Implement phase execution order for FS
   - [ ] Add early termination on failure
   - [ ] Create phase dependency graph

2. **Error Attribution**
   - [ ] Standardize error messages across all phases
   - [ ] Ensure failedAt accurately identifies phase
   - [ ] Add detailed error context in details object
   - [ ] Create error message catalog

3. **Namespace Validation**
   - [ ] Add `clio-infrastructure` namespace existence check
   - [ ] Fail immediately if namespace missing
   - [ ] Include in Phase 0 validation

4. **Timeout and Resilience**
   - [ ] Add configurable timeouts for all checks
   - [ ] Implement graceful degradation
   - [ ] Add connection pooling where appropriate

#### Deliverables:
- Complete error handling framework
- Phase orchestration working correctly
- Proper error attribution in all scenarios

---

### Phase 7: Testing
**Duration:** 3-4 days

#### Tasks:
1. **Unit Tests**
   - [ ] Flag parsing tests
   - [ ] Assertion graph normalization tests
   - [ ] Engine detection logic tests
   - [ ] Port resolution tests
   - [ ] ACL evaluation tests
   - [ ] Context validation tests

2. **Integration Tests**
   - [ ] Set up test Kubernetes cluster (kind/k3s)
   - [ ] Deploy test Postgres instance
   - [ ] Deploy test MSSQL instance
   - [ ] Deploy test Redis instance
   - [ ] Test all K8 assertion scenarios
   - [ ] Test filesystem scenarios on Windows
   - [ ] Test with auth/no-auth Redis

3. **Test Coverage**
   - [ ] Ensure >80% code coverage
   - [ ] Cover all error paths
   - [ ] Test edge cases (multiple ports, missing services, etc.)

4. **Cross-Platform Testing**
   - [ ] Test on Windows
   - [ ] Test on Linux (K8 only)
   - [ ] Test on macOS (K8 only)

#### Deliverables:
- Comprehensive test suite
- High code coverage
- All scenarios validated

---

### Phase 8: Documentation
**Duration:** 2 days

#### Tasks:
1. **Command Help**
   - [ ] Write detailed `assert --help` output
   - [ ] Document exact semantics of each flag
   - [ ] Include "what it checks" and "what it does NOT check"
   - [ ] Add 3-5 canonical examples per subcommand

2. **README Documentation**
   - [ ] Add assert command to Commands.md
   - [ ] Include detailed usage examples
   - [ ] Document output format and exit codes
   - [ ] Add troubleshooting section

3. **Code Documentation**
   - [ ] Add XML comments to all public interfaces
   - [ ] Document design decisions in code
   - [ ] Add examples in comments

#### Deliverables:
- Complete help text
- Updated documentation files
- Well-documented code

---

## Technical Architecture

### Project Organization
```
clio/
├── Command/
│   ├── AssertCommand.cs
│   └── AssertOptions.cs
├── Common/
│   ├── Assertions/
│   │   ├── IAssertionResult.cs
│   │   ├── AssertionResult.cs
│   │   ├── AssertionPhase.cs
│   │   └── AssertionScope.cs
│   ├── Kubernetes/
│   │   ├── IKubernetesClient.cs
│   │   ├── KubernetesClient.cs
│   │   ├── IK8DatabaseDiscovery.cs
│   │   ├── K8DatabaseDiscovery.cs
│   │   ├── IK8ServiceResolver.cs
│   │   ├── K8ServiceResolver.cs
│   │   ├── K8ContextValidator.cs
│   │   └── Redis/
│   │       └── IRedisChecker.cs
│   ├── Database/
│   │   ├── IDatabaseConnectivityChecker.cs
│   │   ├── DatabaseConnectivityChecker.cs
│   │   ├── IDatabaseCapabilityChecker.cs
│   │   └── DatabaseCapabilityChecker.cs
│   └── Filesystem/
│       ├── IWindowsIdentityResolver.cs
│       ├── WindowsIdentityResolver.cs
│       ├── IWindowsAclEvaluator.cs
│       └── WindowsAclEvaluator.cs
└── tests/
    ├── Command/
    │   └── AssertCommandTests.cs
    ├── Kubernetes/
    │   ├── K8DatabaseDiscoveryTests.cs
    │   ├── K8ServiceResolverTests.cs
    │   └── K8ContextValidatorTests.cs
    └── Filesystem/
        ├── WindowsIdentityResolverTests.cs
        └── WindowsAclEvaluatorTests.cs
```

### Key Dependencies
- **Kubernetes Client**: KubernetesClient or similar .NET Kubernetes library
- **Database Clients**: Npgsql (Postgres), System.Data.SqlClient (MSSQL)
- **Redis Client**: StackExchange.Redis
- **Testing**: NUnit, NSubstitute, FluentAssertions

---

## Risk Assessment

### High Risk
1. **Windows ACL Evaluation Complexity**
   - Mitigation: Start with simple cases, iterate
   - May require Windows-specific security APIs

2. **Kubernetes Client Compatibility**
   - Mitigation: Use stable, well-tested K8s client library
   - Extensive integration testing

3. **Port Resolution Edge Cases**
   - Mitigation: Clear specification of behavior for multiple ports
   - Comprehensive test coverage

### Medium Risk
1. **Cross-Platform Testing**
   - Mitigation: Set up CI/CD for all platforms
   - Use Docker for consistent test environments

2. **Credential Management**
   - Mitigation: Design for pluggability from start
   - Document credential requirements clearly

### Low Risk
1. **Output Format Stability**
   - Already well-defined in spec
   - Easy to version if needed

---

## Success Criteria

### Functional
- [ ] All flags work as specified
- [ ] Output format matches contract
- [ ] Exit codes correct in all scenarios
- [ ] Phase 0 context validation mandatory and working
- [ ] Dynamic port resolution functional
- [ ] Error attribution accurate

### Quality
- [ ] Test coverage >80%
- [ ] All integration tests pass
- [ ] Cross-platform validation complete
- [ ] Documentation complete and accurate

### Performance
- [ ] Assertions complete within 30 seconds for typical scenarios
- [ ] No memory leaks
- [ ] Proper resource cleanup

---

## Timeline

| Phase                   | Duration | Dependencies |
|-------------------------|----------|--------------|
| Phase 1: Core Framework | 2-3 days | None         |
| Phase 2: K8 Context     | 2-3 days | Phase 1      |
| Phase 3: K8 Database    | 4-5 days | Phase 2      |
| Phase 4: K8 Redis       | 2-3 days | Phase 2      |
| Phase 5: Filesystem     | 3-4 days | Phase 1      |
| Phase 6: Integration    | 2-3 days | Phases 2-5   |
| Phase 7: Testing        | 3-4 days | All previous |
| Phase 8: Documentation  | 2 days   | All previous |

**Total Estimated Duration:** 20-27 days (~4-5 weeks)

---

## Next Steps

1. Review and approve this implementation plan
2. Set up development environment with Kubernetes cluster
3. Begin Phase 1 implementation
4. Schedule regular progress reviews after each phase

---

## Notes

- This is an iterative plan; adjustments may be needed as implementation progresses
- Future extensions (listed in spec) are explicitly NOT included in this plan
- Focus is on core functionality with excellent test coverage
- Platform-specific features (Windows FS) are isolated and clearly marked
