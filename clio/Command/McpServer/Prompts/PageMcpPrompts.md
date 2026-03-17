# Page MCP Tools

DB-first Freedom UI page management tools for reading, updating, and listing page schemas directly in the database.

## Available Tools

### page-get-db
Get Freedom UI page schema from database.

**When to use:** Retrieving page configuration, inspecting page structure, exporting page definitions, backup/restore scenarios.

**Parameters:**
- `pageName` (required): Page schema name (e.g., "AccountPageV2", "ContactPageV2")
- `packageUId` (optional): Filter by specific package
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "pageName": "AccountPageV2",
  "environmentName": "dev"
}
```

**Returns:** Complete page schema including:
- Page metadata (name, caption, package)
- View model structure
- View configuration
- Business rules
- Attributes and methods

**Best practices:**
- Use exact page schema names
- Check package if multiple versions exist
- Store retrieved schemas for documentation
- Use for migration planning

---

### page-update-db
Update existing Freedom UI page schema in database.

**When to use:** Modifying page layouts, updating view configurations, changing business rules, applying page changes programmatically.

**Parameters:**
- `pageName` (required): Page schema name to update
- `packageUId` (required): Target package GUID
- `schemaJson` (required): Complete page schema as JSON string
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "pageName": "AccountPageV2",
  "packageUId": "12345678-1234-1234-1234-123456789012",
  "schemaJson": "{\"views\":[...],\"viewModel\":{...}}",
  "environmentName": "dev"
}
```

**Schema JSON structure:**
```json
{
  "views": [
    {
      "type": "crt.Page",
      "name": "PageName",
      "children": [...]
    }
  ],
  "viewModel": {
    "attributes": {},
    "handlers": []
  }
}
```

**⚠️ Important:**
- Schema JSON must be complete and valid
- Invalid JSON will fail validation
- Test updates on non-production first
- Backup original schema before updating
- Use proper package for changes

---

### page-list-db
Get list of all Freedom UI pages in the system.

**When to use:** Discovering available pages, finding page names, inventory management, documentation generation.

**Parameters:**
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "environmentName": "dev"
}
```

**Returns:** Array of all Freedom UI pages with:
- Page name
- Caption/title
- Package information
- Schema type

**Use cases:**
- Page discovery
- Documentation generation
- Migration planning
- Audit and inventory

---

## Common Patterns

### Reading and Updating Page
```javascript
// 1. Get current page schema
const currentSchema = await pageGetDb({
  pageName: "AccountPageV2",
  environmentName: "dev"
});

// 2. Modify schema (parse JSON, modify, stringify)
const schema = JSON.parse(currentSchema);
schema.viewModel.attributes.UsrNewField = {
  type: "string",
  caption: "New Field"
};

// 3. Update page
await pageUpdateDb({
  pageName: "AccountPageV2",
  packageUId: "...",
  schemaJson: JSON.stringify(schema),
  environmentName: "dev"
});
```

### Safe Update Workflow
```
1. List pages: page-list-db
2. Get current schema: page-get-db
3. Backup schema locally
4. Modify schema
5. Validate JSON
6. Update page: page-update-db
7. Verify changes: page-get-db
8. Test in UI
```

### Batch Page Export
```javascript
// Get all pages
const pages = await pageListDb({ environmentName: "dev" });

// Export each page
for (const page of pages) {
  const schema = await pageGetDb({
    pageName: page.name,
    environmentName: "dev"
  });
  // Save to file or database
}
```

---

## DB-first vs File-first

**Use DB-first (these tools) when:**
- Direct database access needed
- No local file system
- Automated page updates
- CI/CD page deployment
- Remote environment management
- API-driven page configuration

**Use file-first tools when:**
- Working with local page files
- Version control for pages
- IDE-based development
- Complex page editing with code editor
- Team collaboration on page files

**Key difference:**
- **DB-first**: Pages read/written directly to/from database as JSON
- **File-first**: Pages managed as local files, processed before deployment

---

## Freedom UI Schema Structure

### View Structure
```json
{
  "views": [
    {
      "type": "crt.Page",
      "name": "PageName",
      "children": [
        {
          "type": "crt.Container",
          "children": [
            {
              "type": "crt.Input",
              "propertyName": "AttributeName"
            }
          ]
        }
      ]
    }
  ]
}
```

### ViewModel Structure
```json
{
  "viewModel": {
    "attributes": {
      "AttributeName": {
        "type": "string",
        "caption": "Display Caption"
      }
    },
    "handlers": [
      {
        "request": "RequestName",
        "handler": "async (request, next) => { ... }"
      }
    ]
  }
}
```

---

## Common Page Components

| Component | Type | Usage |
|-----------|------|-------|
| Input | `crt.Input` | Text input field |
| Dropdown | `crt.Dropdown` | Select/dropdown list |
| Button | `crt.Button` | Clickable button |
| Container | `crt.Container` | Layout container |
| GridContainer | `crt.GridContainer` | Grid layout |
| FlexContainer | `crt.FlexContainer` | Flex layout |
| Label | `crt.Label` | Static text label |
| Link | `crt.Link` | Clickable link |

---

## Error Handling

Common errors:
- **Page not found**: Verify page name exists (use page-list-db)
- **Invalid JSON**: Validate schema JSON before update
- **Package mismatch**: Ensure correct package GUID
- **Schema validation error**: Check schema structure matches Freedom UI format
- **Missing required fields**: Include all required schema properties

---

## Best Practices

1. **Always backup before updating**
   - Use page-get-db to retrieve current schema
   - Store backup locally or in version control

2. **Validate schema JSON**
   - Parse and validate before sending
   - Check for syntax errors
   - Verify required fields present

3. **Test in non-production first**
   - Apply changes to dev/test environment
   - Verify functionality
   - Then promote to production

4. **Use proper package**
   - Get package GUID from entity-list-packages-db
   - Ensure package is correct for environment
   - Don't mix package changes

5. **Document changes**
   - Keep changelog of page modifications
   - Note why changes were made
   - Track which package contains which version

6. **Version control**
   - Export page schemas to files
   - Commit to version control
   - Use as rollback reference

---

## Performance Tips

- Cache page list results if querying frequently
- Retrieve only needed pages (don't fetch all unnecessarily)
- Use package filter to narrow results
- Compress large schema JSON for transport
- Batch multiple page operations when possible

---

## Troubleshooting

**Page changes don't appear in UI:**
- Clear browser cache
- Refresh application workspace
- Check package compilation status
- Verify changes saved (use page-get-db)

**Schema validation fails:**
- Validate JSON syntax
- Check component type names
- Verify attribute references
- Ensure handler signatures correct

**Update fails silently:**
- Check package permissions
- Verify environment connectivity
- Review backend logs
- Confirm package GUID correct
