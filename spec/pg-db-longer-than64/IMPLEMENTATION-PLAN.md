# Implementation Plan: PostgreSQL Database Name Length Fix

## Problem Statement

PostgreSQL has a hard limit of 63 characters for database names. The current implementation of `restore-db` and `deploy-creatio` commands creates template databases using the pattern `template_<zipfilename_without_extension>`, which can exceed the 63-character limit when processing zip files with long names.

**Current Implementation:**
- Template naming: `template_<original_filename>`
- Template lookup: By exact name match
- Risk: Database name truncation or errors when exceeding 63 characters

**Required Solution:**
- Template naming: `template_<GUID>` (maximum 45 characters including prefix)
- Template metadata: Store original zip file information in database comment
- Template lookup: Search by comment metadata instead of name
- Backward compatibility: Support both old and new naming schemes during transition

## Affected Code Locations

### 1. Core Database Operations (`clio\Common\db\Postgres.cs`)
**Current Methods:**
- `CheckTemplateExists(string templateName)` - Checks by name
- `SetDatabaseAsTemplate(string dbName)` - Sets template flag

**Required Changes:**
- Add method to set database comment with backup source metadata
- Add method to find template by comment (search for original zip file)
- Add method to get template comment
- Keep existing methods for backward compatibility

### 2. CreatioInstallerService (`clio\Command\CreatioInstallCommand\CreatioInstallerService.cs`)

**Affected Methods:**

#### `DoPgWork` (Line 359)
```csharp
string tmpDbName = string.IsNullOrWhiteSpace(templateName) 
    ? "template_" + unzippedDirectory.Name 
    : "template_" + templateName;
```
**Changes:**
- Generate GUID-based template name
- Store original filename in database comment
- Search for existing template by comment before creating new one

#### `CreatePgTemplate` (Line ~283)
**Changes:**
- Use GUID-based naming for new templates
- Set database comment with original backup file metadata
- Check for existing templates by comment first

#### `RestorePostgresToLocalServer` (Line ~497)
```csharp
string baseFileName = !string.IsNullOrEmpty(zipFilePath) 
    ? Path.GetFileNameWithoutExtension(zipFilePath) 
    : Path.GetFileNameWithoutExtension(backupPath);
string templateName = "template_" + baseFileName;
```
**Changes:**
- Generate GUID-based template name
- Store original filename in comment
- Search for existing template by original filename in comment

### 3. RestoreDb Command (`clio\Command\RestoreDb.cs`)

#### `RestorePg` (Line ~145)
```csharp
string templateName = Path.GetFileNameWithoutExtension(backupFilePath);
return _creatioInstallerService.DoPgWork(directoryInfo, dbName, templateName);
```
**Changes:**
- Pass original filename for metadata
- Let DoPgWork handle GUID generation and lookup

#### `RestorePostgresToLocalServer` (Line ~327)
**Changes:**
- Use metadata-based template lookup
- Generate GUID-based template names for new templates

## Implementation Steps

### Phase 1: Extend PostgreSQL Client Interface (IPostgres)

**File:** `clio\Common\db\Postgres.cs`

1. **Add new methods to IPostgres interface:**
   ```csharp
   bool SetDatabaseComment(string dbName, string comment);
   string GetDatabaseComment(string dbName);
   string FindTemplateBySourceFile(string sourceFileName);
   ```

2. **Implement new methods in Postgres class:**
   
   a. `SetDatabaseComment` - Uses SQL: 
   ```sql
   COMMENT ON DATABASE "<dbname>" IS '<comment>';
   ```
   
   b. `GetDatabaseComment` - Uses SQL:
   ```sql
   SELECT obj_description(oid, 'pg_database') 
   FROM pg_database 
   WHERE datname = '<dbname>';
   ```
   
   c. `FindTemplateBySourceFile` - Uses SQL:
   ```sql
   SELECT datname 
   FROM pg_database 
   WHERE datistemplate = true 
     AND obj_description(oid, 'pg_database') LIKE '%sourceFile:<filename>%'
   LIMIT 1;
   ```

3. **Add helper method:**
   ```csharp
   private string GenerateTemplateName()
   {
       return $"template_{Guid.NewGuid():N}";
   }
   
   private string CreateTemplateMetadata(string sourceFile, string backupFile = null)
   {
       // Format: sourceFile:<name>|createdDate:<iso8601>|version:1.0
       return $"sourceFile:{sourceFile}|createdDate:{DateTime.UtcNow:o}|version:1.0";
   }
   
   private string ExtractSourceFileFromMetadata(string metadata)
   {
       // Parse metadata and extract sourceFile value
   }
   ```

### Phase 2: Update CreatioInstallerService

**File:** `clio\Command\CreatioInstallCommand\CreatioInstallerService.cs`

1. **Modify `DoPgWork` method (Line ~359):**
   ```csharp
   public int DoPgWork(DirectoryInfo unzippedDirectory, string destDbName, string sourceFileName = "")
   {
       // Use sourceFileName for metadata if provided, otherwise use directory name
       string actualSourceName = string.IsNullOrWhiteSpace(sourceFileName) 
           ? unzippedDirectory.Name 
           : sourceFileName;
       
       k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
       Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);
       
       // Try to find existing template by source file
       string existingTemplate = postgres.FindTemplateBySourceFile(actualSourceName);
       
       string templateName;
       if (!string.IsNullOrEmpty(existingTemplate))
       {
           _logger.WriteInfo($"[Database restore] - Found existing template '{existingTemplate}' for source '{actualSourceName}'");
           templateName = existingTemplate;
       }
       else
       {
           // Generate new GUID-based name
           templateName = $"template_{Guid.NewGuid():N}";
           CreatePgTemplate(unzippedDirectory, templateName, actualSourceName);
       }
       
       postgres.CreateDbFromTemplate(templateName, destDbName);
       _logger.WriteInfo($"[Database created] - {destDbName}");
       return 0;
   }
   ```

2. **Modify `CreatePgTemplate` method (Line ~283):**
   ```csharp
   private void CreatePgTemplate(DirectoryInfo unzippedDirectory, string tmpDbName, string sourceFileName)
   {
       k8Commands.ConnectionStringParams csp = _k8.GetPostgresConnectionString();
       Postgres postgres = new(csp.DbPort, csp.DbUsername, csp.DbPassword);
       
       // Check if this exact template already exists
       bool exists = postgres.CheckTemplateExists(tmpDbName);
       if (exists)
       {
           _logger.WriteInfo($"[Database restore] - Template '{tmpDbName}' already exists, skipping restore");
           return;
       }
       
       // Find backup file...
       FileInfo src = unzippedDirectory.GetDirectories("db").FirstOrDefault()?.GetFiles("*.backup").FirstOrDefault();
       if (src is null)
       {
           src = unzippedDirectory?.GetFiles("*.backup").FirstOrDefault();
       }
       
       if (src is null)
       {
           // ... existing error handling
           throw new FileNotFoundException("Backup file not found in the specified directory.");
       }
       
       _logger.WriteInfo($"[Starting Database restore] - {DateTime.Now:hh:mm:ss}");
       
       _k8.CopyBackupFileToPod(k8Commands.PodType.Postgres, src.FullName, src.Name);
       postgres.CreateDb(tmpDbName);
       _k8.RestorePgDatabase(src.Name, tmpDbName);
       
       // Set as template and add metadata comment
       postgres.SetDatabaseAsTemplate(tmpDbName);
       string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
       postgres.SetDatabaseComment(tmpDbName, metadata);
       
       _k8.DeleteBackupImage(k8Commands.PodType.Postgres, src.Name);
       _logger.WriteInfo($"[Completed Database restore] - {DateTime.Now:hh:mm:ss}");
       _logger.WriteInfo($"[Template metadata] - {metadata}");
   }
   ```

3. **Modify `RestorePostgresToLocalServer` method (Line ~497):**
   ```csharp
   private int RestorePostgresToLocalServer(LocalDbServerConfiguration config, string backupPath, 
       string dbName, bool dropIfExists, string zipFilePath = null)
   {
       // ... existing pg_restore path detection code ...
       
       try
       {
           Postgres postgres = _dbClientFactory.CreatePostgres(config.Hostname, config.Port, 
               config.Username, config.Password);
           
           // Determine source file name for metadata
           string sourceFileName = !string.IsNullOrEmpty(zipFilePath) 
               ? Path.GetFileNameWithoutExtension(zipFilePath) 
               : Path.GetFileNameWithoutExtension(backupPath);
           
           // Try to find existing template by source file
           string templateName = postgres.FindTemplateBySourceFile(sourceFileName);
           
           if (string.IsNullOrEmpty(templateName))
           {
               // No existing template found, create new one with GUID-based name
               templateName = $"template_{Guid.NewGuid():N}";
               
               _logger.WriteInfo($"Template for '{sourceFileName}' does not exist, creating '{templateName}'...");
               
               // Create template database
               bool templateCreated = postgres.CreateDb(templateName);
               if (!templateCreated)
               {
                   _logger.WriteError($"Failed to create template database {templateName}");
                   return 1;
               }
               
               _logger.WriteInfo($"Starting restore from {backupPath}...");
               // ... existing pg_restore execution code ...
               
               // Set as template and add metadata
               bool setAsTemplate = postgres.SetDatabaseAsTemplate(templateName);
               if (!setAsTemplate)
               {
                   _logger.WriteError($"Failed to set database {templateName} as template");
                   return 1;
               }
               
               string metadata = $"sourceFile:{sourceFileName}|createdDate:{DateTime.UtcNow:o}|version:1.0";
               postgres.SetDatabaseComment(templateName, metadata);
               
               _logger.WriteInfo($"Template database {templateName} created successfully with source reference: {sourceFileName}");
           }
           else
           {
               _logger.WriteInfo($"Found existing template '{templateName}' for source '{sourceFileName}', skipping restore");
           }
           
           // ... rest of method (create target database from template)
       }
       catch (Exception ex)
       {
           _logger.WriteError($"Error restoring PostgreSQL database: {ex.Message}");
           return 1;
       }
   }
   ```

### Phase 3: Update RestoreDb Command

**File:** `clio\Command\RestoreDb.cs`

1. **Modify `RestorePg` method (Line ~145):**
   ```csharp
   private int RestorePg(string dbName, string backupFilePath)
   {
       var fileInfo = new FileInfo(backupFilePath);
       DirectoryInfo directoryInfo = fileInfo.Directory;
       
       // Pass original filename for metadata
       string sourceFileName = Path.GetFileNameWithoutExtension(backupFilePath);
       
       return _creatioInstallerService.DoPgWork(directoryInfo, dbName, sourceFileName);
   }
   ```

2. **Update `RestorePostgresToLocalServer` method:**
   - Already covered in CreatioInstallerService changes
   - This method in RestoreDb.cs calls the CreatioInstallerService method via dependency

### Phase 4: Add Unit Tests

**New Test File:** `clio.tests\Common\db\PostgresTests.cs`

1. **Test new methods:**
   - `SetDatabaseComment_ValidComment_Success`
   - `GetDatabaseComment_ExistingComment_ReturnsComment`
   - `GetDatabaseComment_NoComment_ReturnsNull`
   - `FindTemplateBySourceFile_ExistingTemplate_ReturnsTemplateName`
   - `FindTemplateBySourceFile_NoTemplate_ReturnsNull`
   - `GenerateTemplateName_Always_DoesNotExceed63Characters`

**Update Existing Test Files:**

1. **`clio.tests\Command\RestoreDb.Tests.cs`:**
   - Update tests to verify GUID-based template naming
   - Add test for metadata storage

2. **`clio.tests\Command\RestoreDb.LocalServer.Tests.cs`:**
   - Add test for template lookup by source file
   - Add test for long filename handling

3. **`clio.tests\Command\CreatioInstallerServiceTests.cs`:**
   - Add tests for new DoPgWork behavior
   - Test backward compatibility scenarios

### Phase 5: Update Documentation

**Files to Update:**

1. **`clio\Commands.md`:**
   - Document the new template naming scheme
   - Explain metadata-based template lookup
   - Add examples with long filenames

2. **`README.md`:**
   - Add note about PostgreSQL template management improvements
   - Mention 63-character limit handling

## Backward Compatibility Strategy

The implementation will maintain backward compatibility by:

1. **Dual Lookup Strategy:**
   - First, search for template by source file in metadata
   - If not found, fall back to old naming pattern check: `template_<filename>`
   - Only create new template if neither exists

2. **Gradual Migration:**
   - Old templates continue to work
   - New templates use GUID-based naming
   - Over time, old templates can be manually cleaned up or migrated

3. **No Breaking Changes:**
   - All existing commands continue to work
   - Existing templates remain usable
   - New behavior only affects template creation

## Metadata Format Specification

Template database comments will use a pipe-delimited format:

```
sourceFile:<original_zip_filename>|createdDate:<ISO8601_UTC>|version:1.0
```

**Example:**
```
sourceFile:8.1.3.5678_Studio_Softkey_PostgreSQL_ENU|createdDate:2026-01-18T10:30:00.000Z|version:1.0
```

**Parsing Rules:**
- Split by `|` to get key-value pairs
- Each pair is `key:value`
- `sourceFile` is required for template matching
- `createdDate` is informational
- `version` allows future format changes

## Validation Criteria

### Success Criteria

1. **Database Name Length:**
   - Template names never exceed 63 characters
   - Format: `template_<32_char_guid>` = 41 characters total

2. **Functionality:**
   - Templates are correctly reused when same zip file is restored multiple times
   - Target databases are created successfully from GUID-based templates
   - Metadata is correctly stored and retrieved

3. **Backward Compatibility:**
   - Existing old-style templates continue to work
   - No errors when processing existing environments

4. **Error Handling:**
   - Clear error messages when template creation fails
   - Proper cleanup on failures

### Test Scenarios

1. **Long Filename Test:**
   - Zip file: `CreatioStudio_8.1.3.5678_Enterprise_Marketing_Sales_ServiceEnterpriseEdition_PostgreSQL_Full.zip`
   - Expected: Template created with GUID name, metadata contains full original name
   - Expected: Subsequent restore finds existing template by metadata

2. **Reuse Test:**
   - First restore: Creates template
   - Second restore with same zip: Finds and reuses template
   - Verify: Only one template exists, second restore is faster

3. **Backward Compatibility Test:**
   - Old template exists: `template_shortname`
   - New restore with same file: Reuses old template
   - Verify: No duplicate templates created

4. **K8s Environment Test:**
   - Test with k8Commands integration
   - Verify template creation in containerized PostgreSQL

5. **Local Server Test:**
   - Test with local PostgreSQL server configuration
   - Verify pg_restore integration

## Risk Assessment

### Low Risk
- Adding new database methods (isolated changes)
- Metadata storage (non-invasive)

### Medium Risk
- Template lookup logic changes (affects restore behavior)
- Multiple code paths to update (deploy-creatio, restore-db)

### Mitigation
- Comprehensive unit tests with mocking
- Integration tests with real PostgreSQL
- Gradual rollout with backward compatibility
- Clear error messages for debugging

## Estimated Effort

- **Phase 1:** 2-3 hours (Database methods)
- **Phase 2:** 3-4 hours (CreatioInstallerService updates)
- **Phase 3:** 1 hour (RestoreDb updates)
- **Phase 4:** 3-4 hours (Unit tests)
- **Phase 5:** 1 hour (Documentation)
- **Testing & Validation:** 2-3 hours

**Total Estimated Time:** 12-15 hours

## Dependencies

- No new external dependencies required
- Uses existing Npgsql library for PostgreSQL operations
- All changes are internal to clio codebase

## Rollout Plan

1. Implement Phase 1 (database methods)
2. Add unit tests for Phase 1
3. Implement Phase 2 (CreatioInstallerService)
4. Add unit tests for Phase 2
5. Implement Phase 3 (RestoreDb)
6. Add integration tests
7. Update documentation
8. Code review
9. Merge to development branch
10. User acceptance testing
11. Release in next version

## Future Enhancements (Post-Implementation)

1. **Template Management Commands:**
   - `clio list-templates` - Show all templates with metadata
   - `clio clean-templates` - Remove unused templates
   - `clio migrate-template <old-name>` - Migrate old template to new format

2. **Template Versioning:**
   - Track Creatio version in metadata
   - Support multiple templates for different versions

3. **Template Compression:**
   - Investigate PostgreSQL database compression for templates
   - Reduce storage requirements

4. **Cloud Storage Integration:**
   - Store template backups in cloud storage
   - Download and restore on demand
