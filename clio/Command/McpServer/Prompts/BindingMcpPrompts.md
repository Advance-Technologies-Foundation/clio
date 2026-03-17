# Binding MCP Tools

DB-first data binding tools for populating entity schemas with data directly in the database.

## Available Tools

### binding-create-db
Create data bindings (insert rows) for entity schema.

**When to use:** Populating lookup data, creating test data, initial data migration, seeding reference tables.

**Parameters:**
- `schemaName` (required): Target entity schema name
- `packageUId` (required): Target package GUID
- `rowsJson` (required): JSON array of row data
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "schemaName": "UsrPriority",
  "packageUId": "12345678-1234-1234-1234-123456789012",
  "rowsJson": "[
    {\"UsrName\":\"High\",\"UsrDescription\":\"High priority items\",\"UsrValue\":3},
    {\"UsrName\":\"Medium\",\"UsrDescription\":\"Medium priority items\",\"UsrValue\":2},
    {\"UsrName\":\"Low\",\"UsrDescription\":\"Low priority items\",\"UsrValue\":1}
  ]",
  "environmentName": "dev"
}
```

**Row format:**
- Keys: Column names (including "Usr" prefix for custom columns)
- Values: Data values matching column types
- GUID columns: Can use string GUIDs or omit (auto-generated)

**Data type examples:**
```json
{
  "UsrName": "Text value",
  "UsrNumber": 123,
  "UsrDecimal": 123.45,
  "UsrDate": "2024-03-17",
  "UsrDateTime": "2024-03-17T14:30:00Z",
  "UsrBoolean": true,
  "UsrLookupId": "12345678-1234-1234-1234-123456789012"
}
```

**Best practices:**
- Validate data before binding
- Use proper data types
- Include required columns
- Handle lookup references correctly
- Test with small batches first

---

### binding-get-columns-db
Get list of columns available for binding in entity schema.

**When to use:** Discovering schema structure, validating column names before binding, building dynamic data import.

**Parameters:**
- `schemaName` (required): Entity schema name
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "schemaName": "Contact",
  "environmentName": "dev"
}
```

**Returns:** Array of column names available for data binding.

**Use case:**
```
1. Get columns: binding-get-columns-db
2. Prepare data matching columns
3. Create binding: binding-create-db
```

---

## Common Patterns

### Populating Lookup Table
```json
{
  "schemaName": "UsrStatus",
  "packageUId": "...",
  "rowsJson": "[
    {\"UsrName\":\"New\",\"UsrCode\":\"NEW\",\"UsrColor\":\"#007BFF\"},
    {\"UsrName\":\"In Progress\",\"UsrCode\":\"PROGRESS\",\"UsrColor\":\"#FFA500\"},
    {\"UsrName\":\"Completed\",\"UsrCode\":\"DONE\",\"UsrColor\":\"#28A745\"},
    {\"UsrName\":\"Cancelled\",\"UsrCode\":\"CANCELLED\",\"UsrColor\":\"#DC3545\"}
  ]"
}
```

### Creating Test Data
```json
{
  "schemaName": "UsrProduct",
  "packageUId": "...",
  "rowsJson": "[
    {\"UsrName\":\"Laptop\",\"UsrPrice\":999.99,\"UsrCategoryId\":\"...\",\"UsrInStock\":true},
    {\"UsrName\":\"Mouse\",\"UsrPrice\":29.99,\"UsrCategoryId\":\"...\",\"UsrInStock\":true},
    {\"UsrName\":\"Keyboard\",\"UsrPrice\":79.99,\"UsrCategoryId\":\"...\",\"UsrInStock\":false}
  ]"
}
```

### Safe Binding Workflow
1. Get schema columns: `binding-get-columns-db`
2. Prepare row data matching column names and types
3. Validate data format
4. Create binding: `binding-create-db`
5. Verify inserted data (use entity-get-schema-db to check)

---

## DB-first vs File-first

**Use DB-first (these tools) when:**
- Direct database population needed
- No local file system
- API-driven data import
- Automated data seeding
- CI/CD data setup

**Use file-first tools (create-data-binding) when:**
- Working with local CSV/Excel files
- Manual data preparation
- File-based data sources
- Version control for data files

**Key difference:**
- **DB-first**: Data provided as JSON in API call, inserted directly to DB
- **File-first**: Data read from local files (CSV, Excel), processed and inserted

---

## Data Type Mapping

| Column Type | JSON Value Example | Notes |
|-------------|-------------------|-------|
| Text | `"string value"` | Max length per schema |
| Integer | `123` | Whole numbers |
| Decimal | `123.45` | Floating point |
| Boolean | `true` or `false` | No strings |
| Date | `"2024-03-17"` | ISO 8601 date |
| DateTime | `"2024-03-17T14:30:00Z"` | ISO 8601 with time |
| Lookup | `"guid-value"` | Reference to lookup ID |
| GUID | `"12345678-..."` | Standard GUID format |

---

## Error Handling

Common errors:
- **Column not found**: Check column names with `binding-get-columns-db`
- **Data type mismatch**: Verify JSON value types match schema
- **Required column missing**: Include all required columns
- **Invalid GUID**: Use proper GUID format for lookup references
- **Duplicate key**: Check for unique constraints on columns

---

## Performance Tips

- Batch multiple rows in single binding call
- Avoid very large batches (>1000 rows) - split if needed
- Test with small data set first
- Use transactions for related data
- Monitor database performance during large imports
