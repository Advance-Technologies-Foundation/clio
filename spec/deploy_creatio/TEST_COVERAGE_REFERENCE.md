# Test Coverage Reference

## CreateDevEnvironmentCommandTests (14 test methods)

### Parameter Parsing Tests (5)
1. `Options_ShouldHave_AllRequiredParameters` - Verifies all 9 parameters exist
2. `Options_Zip_ShouldBeRequired` - Verifies Zip is marked as required
3. `Options_Port_ShouldHaveDefaultValue` - Verifies Port defaults to 8080
4. `Options_Username_ShouldHaveDefaultValue` - Verifies Username defaults to 'Supervisor'
5. `Options_Password_ShouldHaveDefaultValue` - Verifies Password defaults to 'Supervisor'
6. `Options_SkipInfra_ShouldHaveDefaultValueFalse` - Verifies SkipInfra defaults to false

### Validation Tests (2)
7. `Execute_WithNonexistentZipFile_ShouldReturnError` - Validates ZIP file existence
8. `Execute_WithoutEnvName_ShouldHandleInteractivePrompt` - Validates EnvName handling

### Infrastructure Tests (2)
9. `Execute_ShouldCheckExistingInfrastructure` - Verifies KubernetesService.CheckInfrastructureExists is called
10. `Execute_WithSkipInfra_ShouldNotCallKubernetesService` - Verifies SkipInfra option works

### Configuration Tests (3)
11. `Execute_ShouldPatchCookiesSameSiteMode` - Verifies ConfigPatcherService.PatchCookiesSameSiteMode is called
12. `Execute_ShouldUpdateConnectionString` - Verifies ConfigPatcherService.UpdateConnectionString is called with correct params
13. `Execute_ShouldConfigurePort` - Verifies ConfigPatcherService.ConfigurePort is called with correct port

### Database Tests (2)
14. `Execute_WithMaintainer_ShouldSetMaintainerInDatabase` - Verifies PostgresService.SetMaintainerSettingAsync is called when Maintainer is provided
15. `Execute_WithoutMaintainer_ShouldNotCallPostgresService` - Verifies PostgresService is not called when Maintainer is not provided

### ZIP Extraction Tests (1)
16. `Execute_ShouldExtractZipToTargetDirectory` - Verifies ZIP extraction workflow

### Error Handling Tests (1)
17. `Execute_OnException_ShouldReturnErrorCode` - Verifies exception handling returns error code

---

## ConfigPatcherServiceTests (12 test methods)

### PatchCookiesSameSiteMode Tests (5)
1. `PatchCookiesSameSiteMode_WithoutSameSiteAttribute_ShouldAdd` - Adds missing sameSite attribute
2. `PatchCookiesSameSiteMode_WithExistingSameSiteAttribute_ShouldUpdate` - Updates existing sameSite value
3. `PatchCookiesSameSiteMode_WithoutHttpCookiesElement_ShouldCreate` - Creates httpCookies element if missing
4. `PatchCookiesSameSiteMode_WithNonexistentFile_ShouldReturnFalse` - Handles missing files
5. `PatchCookiesSameSiteMode_WithInvalidXml_ShouldReturnFalse` - Handles invalid XML

### UpdateConnectionString Tests (5)
6. `UpdateConnectionString_WithExistingConnectionString_ShouldUpdate` - Updates existing connection string
7. `UpdateConnectionString_WithoutConnectionStringsSection_ShouldCreate` - Creates connectionStrings section
8. `UpdateConnectionString_WithSpecialCharactersInPassword_ShouldHandleCorrectly` - Handles special characters
9. `UpdateConnectionString_WithNonexistentFile_ShouldReturnFalse` - Handles missing files
10. `UpdateConnectionString_WithInvalidXml_ShouldReturnFalse` - Handles invalid XML
11. `UpdateConnectionString_WithMinimumPort_ShouldSucceed` - Boundary test: port 1

### ConfigurePort Tests (3)
12. `ConfigurePort_WithExistingPortSetting_ShouldUpdate` - Updates existing port value
13. `ConfigurePort_WithoutAppSettingsSection_ShouldCreate` - Creates appSettings section
14. `ConfigurePort_WithoutPortSetting_ShouldAdd` - Adds port setting if missing
15. `ConfigurePort_WithNonexistentFile_ShouldReturnFalse` - Handles missing files
16. `ConfigurePort_WithInvalidXml_ShouldReturnFalse` - Handles invalid XML
17. `ConfigurePort_WithHighPortNumber_ShouldSucceed` - Boundary test: port 65535

---

## PostgresServiceTests (13 test methods)

### TestConnectionAsync Tests (5)
1. `TestConnectionAsync_WithValidParameters_ShouldReturnTrue` - Valid connection test
2. `TestConnectionAsync_WithInvalidServer_ShouldReturnFalse` - Invalid server handling
3. `TestConnectionAsync_WithInvalidPort_ShouldReturnFalse` - Invalid port handling
4. `TestConnectionAsync_WithInvalidCredentials_ShouldReturnFalse` - Invalid credentials handling
5. `TestConnectionAsync_WithConnectionTimeout_ShouldReturnFalse` - Timeout handling

### SetMaintainerSettingAsync Tests (5)
6. `SetMaintainerSettingAsync_WithValidParameters_ShouldReturnTrue` - Valid parameters test
7. `SetMaintainerSettingAsync_WithUnreachableDatabase_ShouldReturnFalse` - Unreachable DB handling
8. `SetMaintainerSettingAsync_WithEmptyMaintainerName_ShouldHandleGracefully` - Empty string handling
9. `SetMaintainerSettingAsync_WithSpecialCharactersInMaintainer_ShouldHandleCorrectly` - Special characters
10. `SetMaintainerSettingAsync_WithLongRunningQuery_ShouldRespectTimeout` - Timeout validation

### ExecuteInitializationScriptsAsync Tests (3)
11. `ExecuteInitializationScriptsAsync_WithValidParameters_ShouldReturnTrue` - Valid parameters test
12. `ExecuteInitializationScriptsAsync_WithUnreachableDatabase_ShouldReturnFalse` - Unreachable DB handling
13. `ExecuteInitializationScriptsAsync_WithConnectionTimeout_ShouldReturnFalse` - Timeout handling
14. `ExecuteInitializationScriptsAsync_WithLongRunningScripts_ShouldRespectTimeout` - Timeout validation

### GetDatabaseVersionAsync Tests (4)
15. `GetDatabaseVersionAsync_WithValidConnection_ShouldReturnVersionString` - Valid connection test
16. `GetDatabaseVersionAsync_WithUnreachableDatabase_ShouldReturnEmpty` - Unreachable DB handling
17. `GetDatabaseVersionAsync_WithConnectionTimeout_ShouldReturnEmpty` - Timeout handling
18. `GetDatabaseVersionAsync_ValidConnection_ShouldContainVersionInfo` - Version string validation
19. `GetDatabaseVersionAsync_WithLongRunningQuery_ShouldRespectTimeout` - Timeout validation

### Concurrency Tests (1)
20. `MultipleAsyncCalls_ShouldExecuteConcurrently` - Concurrent async execution

---

## Test Metrics Summary

| Category | Count | Details |
|----------|-------|---------|
| **Total Test Methods** | 39 | Across 3 test files |
| **Parameter Parsing Tests** | 6 | Verify all parameters and defaults |
| **Validation Tests** | 2 | Input validation and error scenarios |
| **Infrastructure Tests** | 2 | Kubernetes service integration |
| **Configuration Tests** | 8 | XML patching and configuration |
| **Database Tests** | 5 | PostgreSQL operations |
| **ZIP Extraction Tests** | 1 | Application deployment |
| **Error Handling Tests** | 12+ | Exception handling across all services |
| **Timeout Tests** | 6 | Timeout validation (30-60 seconds) |
| **Edge Case Tests** | 8+ | Boundary values, special characters, etc |
| **Concurrency Tests** | 1 | Async operation testing |

---

## Running Tests

### All Tests
```bash
dotnet test Clio.Tests
```

### Specific Test File
```bash
dotnet test Clio.Tests/Command/CreateDevEnvironmentCommandTests.cs
dotnet test Clio.Tests/Common/ConfigPatcherServiceTests.cs
dotnet test Clio.Tests/Common/PostgresServiceTests.cs
```

### Specific Test
```bash
dotnet test --filter "Options_Port_ShouldHaveDefaultValue"
```

### With Coverage
```bash
dotnet test Clio.Tests --collect:"XPlat Code Coverage"
```

### Verbose Output
```bash
dotnet test Clio.Tests --verbosity detailed
```

---

## Test Categories

### Unit Tests
- All tests are marked with `[Category("Unit")]`
- Isolated from external dependencies
- Use mocking for service dependencies
- Use MockFileSystem for file operations

### Test Patterns Used
- **Arrange-Act-Assert**: Standard pattern throughout
- **Description Attribute**: Every test has [Description] for clarity
- **FluentAssertions**: Readable assertion syntax
- **NSubstitute**: Service mocking
- **MockFileSystem**: Isolated file testing

### Dependencies Mocked
- IKubernetesService
- IConfigPatcherService
- IPostgresService
- IFileSystem (via MockFileSystem)
- ILogger

---

## Notes

- All async tests use `async Task` pattern
- Timeout tests validate operations complete within 40 seconds (service has 30-sec timeout)
- File operation tests use MockFileSystem to avoid disk I/O
- Database tests can run without actual PostgreSQL (mocked in unit tests)
- Command tests verify service call counts and parameters using NSubstitute.Received()
