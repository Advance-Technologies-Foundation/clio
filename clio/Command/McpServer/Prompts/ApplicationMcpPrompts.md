# Application MCP Tools

DB-first Creatio application management tools that work directly with the database through backend MCP endpoint.

## Available Tools

### application-create-db
Create a new Creatio application in the database.

**When to use:** Creating new applications without local file system access, automating application creation, CI/CD pipelines.

**Parameters:**
- `name` (required): Application display name
- `code` (required): Application code (e.g., "UsrMyApp")
- `templateCode` (required): Template code (e.g., "AppFreedomUI")
- `iconBackground` (required): Icon background color (e.g., "#FF5733")
- `iconId` (optional): Icon GUID (random if omitted)
- `description` (optional): Application description
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "name": "My Application",
  "code": "UsrMyApp",
  "templateCode": "AppFreedomUI",
  "iconBackground": "#FF5733",
  "environmentName": "dev"
}
```

**Best practices:**
- Use meaningful application codes with "Usr" prefix
- Choose appropriate template for your use case
- Verify icon background color format

**Comparison with file-first tools:**
- **DB-first (this tool)**: Creates application directly in database, no local files
- **File-first**: Works with local configuration files, requires file system access

---

### application-get-info-db
Get detailed information about a specific application.

**When to use:** Checking application configuration, verifying application exists, retrieving application metadata.

**Parameters:**
- `appId` or `appCode` (one required): Application identifier
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "appCode": "UsrMyApp",
  "environmentName": "dev"
}
```

**Returns:** Application details including ID, name, code, icon, description, client type.

---

### application-get-list-db
Get list of all applications in the system.

**When to use:** Discovering available applications, listing applications for selection, inventory management.

**Parameters:**
- `environmentName` or `uri+login+password` (required): Target environment

**Example:**
```json
{
  "environmentName": "dev"
}
```

**Returns:** Array of all applications with their metadata.

**Best practices:**
- Use for application discovery and inventory
- Filter results programmatically if needed
- Cache results if querying frequently

---

## Common Patterns

### Environment Configuration
All tools support two authentication modes:

**1. Using environment name:**
```json
{
  "environmentName": "dev"
}
```

**2. Using direct credentials:**
```json
{
  "uri": "http://localhost:5001",
  "login": "Supervisor",
  "password": "Supervisor"
}
```

### Error Handling
All tools return:
- `exitCode: 0` on success
- `exitCode: 1` on error with detailed error message

### DB-first vs File-first
**Use DB-first tools (these) when:**
- No local file system access
- CI/CD automation
- Remote environment management
- Quick database operations

**Use file-first tools when:**
- Working with local configuration files
- Need version control integration
- Prefer file-based workflows
