# Entity MCP Tools

DB-first Creatio entity schema management tools for creating, updating, and managing entity schemas directly in the database.

## Available Tools

### entity-create-db
Create a new entity schema in the database.

**When to use:** Creating new entities without local files, automating entity creation, prototyping data models.

**Parameters:**
- `packageUId` (required): Target package GUID
- `name` (required): Entity schema name (e.g., "UsrCustomer")
- `caption` (required): Display caption (e.g., "Customer")
- `parentSchemaName` (optional): Parent schema name (default: "BaseEntity")
- `columnsJson` (optional): JSON array of column definitions
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "packageUId": "12345678-1234-1234-1234-123456789012",
  "name": "UsrCustomer",
  "caption": "Customer",
  "parentSchemaName": "BaseEntity",
  "columnsJson": "[{\"name\":\"UsrName\",\"caption\":\"Name\",\"dataValueType\":\"Text\",\"size\":250}]",
  "environmentName": "dev"
}
```

**Column definition format:**
```json
{
  "name": "UsrColumnName",
  "caption": "Display Caption",
  "dataValueType": "Text|Integer|Decimal|Date|DateTime|Lookup|...",
  "size": 250,
  "isRequired": false
}
```

**Best practices:**
- Use "Usr" prefix for custom entity names
- Choose appropriate parent schema (BaseEntity, BaseLookup, etc.)
- Define columns with proper data types
- Set reasonable size limits for Text fields

---

### entity-create-lookup-db
Create a lookup entity schema.

**When to use:** Creating reference data entities, building classification systems, managing dropdown lists.

**Parameters:**
- `packageUId` (required): Target package GUID
- `name` (required): Lookup schema name (e.g., "UsrPriority")
- `caption` (required): Display caption (e.g., "Priority")
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "packageUId": "12345678-1234-1234-1234-123456789012",
  "name": "UsrPriority",
  "caption": "Priority",
  "environmentName": "dev"
}
```

**Auto-created columns:**
- Id (GUID primary key)
- Name (Text, 250)
- Description (Text, 500)

---

### entity-update-db
Update existing entity schema by adding, updating, or deleting columns.

**When to use:** Adding fields to existing entities, modifying column properties, removing obsolete columns.

**Parameters:**
- `schemaName` (required): Entity schema name to update
- `packageUId` (required): Target package GUID
- `columnsJson` (required): JSON array of column operations
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "schemaName": "UsrCustomer",
  "packageUId": "12345678-1234-1234-1234-123456789012",
  "columnsJson": "[{\"operation\":\"ADD\",\"name\":\"UsrEmail\",\"dataValueType\":\"Text\"}]",
  "environmentName": "dev"
}
```

**Operation types:**
- `ADD`: Add new column
- `UPDATE`: Modify existing column
- `DELETE`: Remove column (careful - data loss!)

**Operation format:**
```json
{
  "operation": "ADD|UPDATE|DELETE",
  "name": "UsrColumnName",
  "caption": "Display Caption",
  "dataValueType": "Text",
  "size": 250
}
```

---

### entity-check-name-db
Check if entity schema name is already taken.

**When to use:** Validating entity names before creation, preventing naming conflicts.

**Parameters:**
- `name` (required): Entity schema name to check
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "name": "UsrCustomer",
  "environmentName": "dev"
}
```

**Returns:** `{"isTaken": true/false}`

---

### entity-list-packages-db
Get list of all packages in the system.

**When to use:** Discovering available packages, getting package GUIDs for entity operations.

**Parameters:**
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "environmentName": "dev"
}
```

**Returns:** Array of packages with id, name, and maintainer.

---

### entity-get-schema-db
Get detailed information about entity schema including all columns.

**When to use:** Inspecting entity structure, verifying schema configuration, documentation generation.

**Parameters:**
- `schemaName` (required): Entity schema name
- `packageUId` (optional): Filter by package
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "schemaName": "Contact",
  "environmentName": "dev"
}
```

**Returns:** Complete schema definition with columns, parent, package info.

---

## Common Patterns

### Creating Entity with Columns
```json
{
  "packageUId": "...",
  "name": "UsrProduct",
  "caption": "Product",
  "columnsJson": "[
    {\"name\":\"UsrName\",\"caption\":\"Name\",\"dataValueType\":\"Text\",\"size\":250,\"isRequired\":true},
    {\"name\":\"UsrPrice\",\"caption\":\"Price\",\"dataValueType\":\"Decimal\",\"precision\":10,\"scale\":2},
    {\"name\":\"UsrCategory\",\"caption\":\"Category\",\"dataValueType\":\"Lookup\",\"referenceSchemaName\":\"UsrCategory\"}
  ]"
}
```

### Updating Multiple Columns
```json
{
  "schemaName": "UsrProduct",
  "packageUId": "...",
  "columnsJson": "[
    {\"operation\":\"ADD\",\"name\":\"UsrDescription\",\"dataValueType\":\"Text\",\"size\":500},
    {\"operation\":\"UPDATE\",\"name\":\"UsrName\",\"size\":500},
    {\"operation\":\"DELETE\",\"name\":\"UsrOldColumn\"}
  ]"
}
```

### Safe Entity Creation Workflow
1. Check name availability: `entity-check-name-db`
2. Get target package: `entity-list-packages-db`
3. Create entity: `entity-create-db`
4. Verify creation: `entity-get-schema-db`

---

## DB-first vs File-first

**Use DB-first (these tools) when:**
- Building entities programmatically
- Rapid prototyping
- CI/CD automation
- No local file access

**Use file-first tools (create-entity-schema) when:**
- Version control integration needed
- Team collaboration on schema files
- Working with local development
- Complex schema with custom code

**Key difference:** DB-first creates schemas directly in database without intermediate files. File-first works with local .cs files and configuration.
