# Implementation Plan: Restore Database to Local Server

## Overview
Enhance the `restore-db` command to support restoring database backups to non-Kubernetes database servers while maintaining backward compatibility with existing Kubernetes cluster functionality.

## Current State Analysis

### Existing Implementation
- **Location**: `clio\Command\RestoreDb.cs`
- **Command**: `restore-db` (alias: `rdb`)
- **Current Features**:
  - Restores PostgreSQL databases from `.backup` files
  - Supports MSSQL restore via `DbServer` configuration from environment settings
  - Uses `IDbClientFactory` to create database clients
  - Integrates with Kubernetes for PostgreSQL operations via `ICreatioInstallerService`
  
### Existing Database Clients
- **Mssql** (`clio\Common\db\Mssql.cs`): Implements `IMssql` interface
  - Supports connection testing, database creation, dropping, renaming
  - Currently requires host, port, username, password
  
- **Postgres** (`clio\Common\db\Postgres.cs`): Implements `IPostgres` interface
  - Supports database creation from template, template management
  - Currently requires host, port, username, password
  - Has silent mode for connection testing

### Current Configuration
- **Location**: `clio\appsettings.json`
- **Structure**: Contains environment configurations with URI-based database connections
- **No JSON Schema Found**: Need to create one for appsettings.json validation

## Implementation Tasks

### 1. Configuration Schema Design

**File**: `clio\appsettings.json`

Add new `db` section to support local database server configurations:

```json
{
  "environments": { ... },
  "db": {
    "my-local-mssql": {
      "dbType": "mssql",
      "hostname": "localhost",
      "port": 1433,
      "username": "sa",
      "password": "YourPassword",
      "description": "Local MSSQL Server for development"
    },
    "my-local-postgres": {
      "dbType": "postgres", 
      "hostname": "localhost",
      "port": 5432,
      "username": "postgres",
      "password": "postgres",
      "pgToolsPath": "",
      "description": "Local PostgreSQL Server for development"
    }
  }
}
```

**Improvements to proposed schema**:
- Use `dbType` (camelCase) instead of `dbtype` for consistency
- Add optional `description` field for documentation
- Add optional `pgToolsPath` field for PostgreSQL client tools location
- Make all fields required except `description` and `pgToolsPath`
- Support case-insensitive dbType values (MSSQL, MsSql, mssql, etc.)

**PostgreSQL Client Tools Requirement**:
- **Critical**: For local PostgreSQL restore, `pg_restore` must be available on the machine running clio
- In Kubernetes: Commands execute inside the PostgreSQL pod where tools are pre-installed
- Locally: Tools must be installed separately and accessible via PATH or explicitly configured
- Auto-detection will attempt to find `pg_restore` in PATH
- `pgToolsPath` allows explicit specification if not in PATH (e.g., `C:\Program Files\PostgreSQL\16\bin\` on Windows or `/usr/lib/postgresql/16/bin/` on Linux)

### 2. Create Configuration Models

**New File**: `clio\Common\db\LocalDbServerConfiguration.cs`

```csharp
namespace Clio.Common.db;

public class LocalDbServerConfiguration
{
    public string DbType { get; set; }
    public string Hostname { get; set; }
    public int Port { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
    public string Description { get; set; }
    
    // PostgreSQL-specific: Path to pg_restore and other client tools
    // If not specified, will attempt to find in PATH
    public string PgToolsPath { get; set; }
}

public class DbServersConfiguration
{
    public Dictionary<string, LocalDbServerConfiguration> Servers { get; set; }
}
```

### 3. Create JSON Schema for appsettings.json

**New File**: `clio\appsettings.schema.json`

Create a JSON schema file to enable IntelliSense and validation for appsettings.json:
- Define schema for existing `environments` section
- Define schema for new `db` section with validation rules
- Add enum constraints for `dbType` (mssql, postgres)
- Mark required fields
- Add descriptions for all properties

**Link schema in appsettings.json**:
```json
{
  "$schema": "./appsettings.schema.json",
  ...
}
```

### 4. Enhance Database Client Factory

**File**: `clio\Common\db\DbClientFactory.cs`

Add new methods to support host parameter:
```csharp
public interface IDbClientFactory
{
    IMssql CreateMssql(string host, int port, string username, string password);
    IMssql CreateMssql(int port, string username, string password);
    Postgres CreatePostgres(string host, int port, string username, string password); // NEW
    Postgres CreatePostgres(int port, string username, string password);
    Postgres CreatePostgresSilent(string host, int port, string username, string password); // NEW
    Postgres CreatePostgresSilent(int port, string username, string password);
}
```

Update implementation to support the new overloads.

### 5. Add Connection Testing

**New File**: `clio\Common\db\IDbConnectionTester.cs`

```csharp
public interface IDbConnectionTester
{
    ConnectionTestResult TestConnection(LocalDbServerConfiguration config);
    ConnectionTestResult TestMssqlConnection(string host, int port, string username, string password);
    ConnectionTestResult TestPostgresConnection(string host, int port, string username, string password);
}

public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public string DetailedError { get; set; }
    public string Suggestion { get; set; } // AI/Human-friendly troubleshooting suggestion
}
```

**Implementation**: Create connection tester that:
- Attempts to connect to database
- Captures specific error types (network, authentication, timeout, etc.)
- Provides actionable error messages
- Returns suggestions based on error type (check firewall, verify credentials, etc.)

### 6. Add PostgreSQL Tools Path Detector

**New File**: `clio\Common\db\PostgresToolsPathDetector.cs`

```csharp
public interface IPostgresToolsPathDetector
{
    string FindPgRestore();
    bool IsPgRestoreAvailable(string pgToolsPath = null);
    string GetPgRestorePath(string pgToolsPath = null);
}
```

**Detection Logic**:
- Check explicitly configured `pgToolsPath` first
- Search in PATH environment variable
- Check common installation locations:
  - Windows: `C:\Program Files\PostgreSQL\{version}\bin\`
  - Linux: `/usr/bin/`, `/usr/lib/postgresql/{version}/bin/`
  - macOS: `/Library/PostgreSQL/{version}/bin/`, `/usr/local/bin/`
- Verify executable exists and is callable
- Cache result for performance

### 7. Add Backup File Type Detection

**New File**: `clio\Common\db\BackupFileDetector.cs`

```csharp
public interface IBackupFileDetector
{
    BackupFileType DetectBackupType(string filePath);
    bool IsValidBackupFile(string filePath);
}

public enum BackupFileType
{
    Unknown,
    PostgresBackup,  // .backup extension
    MssqlBackup      // .bak extension
}
```

**Detection Logic**:
- `.backup` → PostgreSQL
- `.bak` → MSSQL
- Validate file exists and has appropriate magic bytes/header if possible

### 8. Update RestoreDbCommandOptions

**File**: `clio\Command\RestoreDb.cs`

Add new option:
```csharp
[Option("dbServerName", Required = false, 
    HelpText = "Name of database server configuration from appsettings.json")]
public string DbServerName { get; set; }
```

### 9. Refactor RestoreDbCommand.Execute

**File**: `clio\Command\RestoreDb.cs`

Update execution flow:

```
1. Check if DbServerName is specified
   └─ Yes: Use local server configuration
      ├─ Load configuration from appsettings.json
      ├─ Validate configuration exists
      ├─ Test connection (fail-fast)
      ├─ Detect backup file type (if not specified)
      ├─ Route to appropriate restore method
      └─ Return result
   └─ No: Use existing behavior
      ├─ Check if BackupPath is specified
      │  └─ Yes: Direct file restore (current PG behavior)
      └─ No: Use environment settings (current behavior)
```

### 10. Implement Restore Methods

**File**: `clio\Command\RestoreDb.cs`

Create new methods:
- `RestoreMssqlToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName)`
  - Uses existing `Mssql` class with `CreateDb()` method
  - Backup file must be accessible to SQL Server (copy to accessible location if needed)
  
- `RestorePostgresToLocalServer(LocalDbServerConfiguration config, string backupPath, string dbName)`
  - **Critical Difference from K8s**: Runs `pg_restore` locally, not in a pod
  - Steps:
    1. Verify `pg_restore` is available (using `IPostgresToolsPathDetector`)
    2. Create empty database using `Postgres` class
    3. Execute `pg_restore` command locally via process execution
    4. Command: `pg_restore -h {hostname} -p {port} -U {username} -d {dbName} {backupPath}`
    5. Set `PGPASSWORD` environment variable for authentication
    6. Capture stdout/stderr for error reporting
    7. Return appropriate error if pg_restore fails
    
- `TestConnectionBeforeRestore(LocalDbServerConfiguration config)` - fail-fast implementation
- `ExecutePgRestoreCommand(string pgRestorePath, LocalDbServerConfiguration config, string backupPath, string dbName)` - wrapper for process execution

### 11. Enhanced Error Handling

Add comprehensive error messages for:
- **Configuration not found**: "Database server configuration '{name}' not found in appsettings.json. Available configurations: {list}"
- **Connection failed**: "{error type}: {error message}. Suggestion: {actionable advice}"
- **Invalid backup file**: "Backup file type {type} is not compatible with database type {dbType}"
- **File not found**: "Backup file not found at {path}"
- **Unsupported database type**: "Database type '{type}' is not supported. Supported types: mssql, postgres"
- **PostgreSQL tools not found**: "pg_restore not found. Please install PostgreSQL client tools and ensure they are in PATH, or specify pgToolsPath in configuration. Download from: https://www.postgresql.org/download/"
- **PostgreSQL tools path invalid**: "pg_restore not found at specified path '{path}'. Please verify PostgreSQL client tools installation."
- **pg_restore execution failed**: "pg_restore failed with exit code {code}: {error}. Suggestion: {suggestion based on error}"

### 12. Update Dependency Injection

**File**: `clio\BindingsModule.cs`

Register new services:
```csharp
containerBuilder.RegisterType<DbConnectionTester>().As<IDbConnectionTester>();
containerBuilder.RegisterType<BackupFileDetector>().As<IBackupFileDetector>();
containerBuilder.RegisterType<PostgresToolsPathDetector>().As<IPostgresToolsPathDetector>().SingleInstance(); // Singleton for caching
```

### 13. Unit Tests

**New File**: `clio.tests\Command\RestoreDb.LocalServer.Tests.cs`

Test cases:
1. ✓ Load local server configuration from appsettings.json
2. ✓ Fail when configuration name doesn't exist
3. ✓ Test connection before restore (success)
4. ✓ Test connection before restore (failure - should abort)
5. ✓ Auto-detect backup file type (.backup → postgres)
6. ✓ Auto-detect backup file type (.bak → mssql)
7. ✓ Fail when backup type doesn't match db type
8. ✓ Restore to local MSSQL server
9. ✓ Restore to local Postgres server with pg_restore available
10. ✓ Fail when pg_restore is not available
11. ✓ Use explicit pgToolsPath when provided
12. ✓ Fall back to PATH when pgToolsPath not provided
13. ✓ Backward compatibility: existing behavior works without dbServerName
14. ✓ Provide meaningful error messages for various failure scenarios
15. ✓ Handle missing backup file
16. ✓ Handle invalid database type in configuration
17. ✓ Handle pg_restore execution failures
18. ✓ Properly set PGPASSWORD environment variable

**New File**: `clio.tests\Common\PostgresToolsPathDetectorTests.cs`

Test cases:
1. ✓ Find pg_restore in PATH
2. ✓ Find pg_restore at explicit path
3. ✓ Return false when pg_restore not available
4. ✓ Check common installation locations
5. ✓ Cache results for performance

**Update File**: `clio.tests\Command\RestoreDb.Tests.cs`
- Ensure existing tests still pass
- Add tests for backward compatibility

### 14. Integration Tests (Optional but Recommended)

**New File**: `clio.tests\Integration\RestoreDb.Integration.Tests.cs`

If possible, test against real database servers:
- Docker-based MSSQL instance
- Docker-based PostgreSQL instance
- Test full restore workflow

### 15. Documentation Updates

**File**: `clio\Commands.md`

Update restore-db command documentation:
- Add new `--dbServerName` option
- Provide examples of appsettings.json configuration
- Document backup file type auto-detection
- Document connection testing and error messages
- Add troubleshooting section
- **Add Prerequisites section**:
  - For PostgreSQL: Requires PostgreSQL client tools (pg_restore) installed locally
  - Installation instructions for Windows, Linux, macOS
  - How to configure pgToolsPath if not in PATH

**File**: `README.md`

Update if restore-db command is mentioned in main documentation.

### 16. Configuration Template

**File**: `clio\tpl\workspace\.clio\appsettings.json.template` (if exists)

Add db configuration section to template with commented examples.

## Implementation Order

### Phase 1: Foundation (Prerequisites)
1. Create configuration models (Task 2)
2. Create JSON schema (Task 3)
3. Create PostgreSQL tools path detector (Task 6)
4. Create backup file detector (Task 7)
5. Create connection tester (Task 5)

### Phase 2: Database Client Updates
6. Enhance database client factory (Task 4)
7. Update DI registrations (Task 12)

### Phase 3: Command Implementation
8. Update command options (Task 8)
9. Refactor Execute method (Task 9)
10. Implement restore methods (Task 10)
11. Enhance error handling (Task 11)

### Phase 4: Testing
12. Write unit tests (Task 13)
13. Update existing tests (Task 13)
14. Write integration tests (Task 14)

### Phase 5: Documentation
15. Update command documentation (Task 15)
16. Update configuration examples (Task 1, 16)

## Backward Compatibility Checklist

- ✓ Existing command without `--dbServerName` works as before
- ✓ Environment-based configuration still works
- ✓ Direct file restore with `--backupPath` still works
- ✓ All existing unit tests pass
- ✓ No breaking changes to public APIs

## Testing Strategy

### Unit Tests
- Mock all external dependencies (file system, database clients)
- Test configuration loading and validation
- Test connection testing logic
- Test backup file detection
- Test error message generation
- Test routing logic (local vs K8s)

### Integration Tests
- Use test database instances (Docker)
- Test actual restore operations
- Test connection failures
- Test invalid configurations

### Manual Testing Scenarios
1. Restore MSSQL backup to local MSSQL server
2. Restore PostgreSQL backup to local PostgreSQL server (with pg_restore in PATH)
3. Restore PostgreSQL backup with explicit pgToolsPath configuration
4. Test pg_restore not available in PATH (should fail gracefully)
5. Test connection failure handling
6. Test missing configuration
7. Test wrong backup type for database type
8. Test backward compatibility (no dbServerName specified)
9. Test on Windows, Linux, and macOS (different pg_restore locations)

## Success Criteria

- [ ] Command can restore to local database server using appsettings.json configuration
- [ ] Connection is tested before restore (fail-fast)
- [ ] Backup file type is auto-detected correctly
- [ ] **PostgreSQL client tools (pg_restore) are properly detected and used**
- [ ] **Explicit pgToolsPath configuration works when provided**
- [ ] **Clear error message when pg_restore is not available**
- [ ] Comprehensive error messages aid troubleshooting
- [ ] JSON schema provides IntelliSense for configuration
- [ ] Backward compatibility maintained
- [ ] All unit tests pass (existing + new)
- [ ] Documentation is updated with PostgreSQL prerequisites
- [ ] Code follows project coding standards
- [ ] Works cross-platform (Windows, Linux, macOS)

## Risk Assessment

### Low Risk
- Adding new configuration section (additive change)
- Adding new command option (optional parameter)
- Creating new services (IDbConnectionTester, IBackupFileDetector)

### Medium Risk
- Refactoring RestoreDbCommand.Execute (complex logic, needs careful testing)
- Auto-detection logic (needs validation)
- **PostgreSQL tools path detection across different operating systems**
- **Process execution for pg_restore with proper error handling**

### Mitigation
- Comprehensive unit test coverage
- Maintain existing code paths
- Thorough manual testing
- Code review focusing on backward compatibility

## Estimated Effort

- **Configuration & Schema**: 2-3 hours
- **PostgreSQL Tools Detection**: 3-4 hours (cross-platform complexity)
- **Core Implementation**: 8-10 hours (including pg_restore execution)
- **Testing**: 5-7 hours (including cross-platform testing)
- **Documentation**: 2-3 hours
- **Total**: 20-27 hours

## Dependencies

- No external package dependencies required
- Existing database client implementations support needed functionality
- Configuration system already in place (Microsoft.Extensions.Configuration)

## Future Enhancements (Out of Scope)

- Support for other database types (MySQL, Oracle, etc.)
- Encrypted password storage
- Connection pooling configuration
- Backup file compression/decompression
- Progress reporting for large backups
- Rollback capability on restore failure
- Auto-download PostgreSQL client tools if not available
- Support for pg_restore options/flags customization
- Parallel restore for PostgreSQL (pg_restore -j option)
