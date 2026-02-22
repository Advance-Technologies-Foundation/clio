# add-item

## Purpose
Creates new items in a Creatio project from templates or generates data models for working with Creatio entities through the ATF.Repository package. This command is essential for quickly scaffolding common code structures like services, entity listeners, or comprehensive entity models with support for lookups and detail relationships.

**Note:** This command requires [cliogate](../../Commands.md#install-gate) to be installed on your Creatio environment when generating models from Creatio schemas.

## Usage
```bash
clio add-item <ITEM_TYPE> [ITEM_NAME] [options]
```

## Arguments

### Required Arguments
| Argument  | Position | Description                                                      | Example |
|-----------|----------|------------------------------------------------------------------|---------|
| ITEM_TYPE | 0        | Type of item to create: `model`, `service`, or `entity-listener` | `model` |

### Optional Arguments
| Argument          | Short | Position | Default           | Description                                                           | Example                  |
|-------------------|-------|----------|-------------------|-----------------------------------------------------------------------|--------------------------|
| ITEM_NAME         |       | 1        |                   | Name of the item to create (required except when creating all models) | `Contact`                |
| --DestinationPath | -d    |          | Current directory | Path to destination directory for generated files                     | `-d C:\MyProject\Models` |
| --Namespace       | -n    |          |                   | Namespace for generated classes (required for models)                 | `-n MyCompany.Models`    |
| --Fields          | -f    |          |                   | Comma-separated list of fields for single model generation            | `-f Name,Email,Phone`    |
| --All             | -a    |          | true              | When true with `model` type, generates all entity models              | `-a true`                |
| --Culture         | -x    |          | en-US             | Culture code for schema descriptions in generated models              | `-x en-US`               |

### Inherited Environment Arguments
When generating models, this command inherits authentication options from `EnvironmentOptions`:

| Argument       | Short | Description                    |
|----------------|-------|--------------------------------|
| --Environment  | -e    | Environment name (recommended) |
| --uri          | -u    | Application URI                |
| --Login        | -l    | User login (for basic auth)    |
| --Password     | -p    | User password (for basic auth) |
| --clientId     |       | OAuth client ID                |
| --clientSecret |       | OAuth client secret            |
| --authAppUri   |       | OAuth authentication app URI   |

## Item Types

### 1. Model (ATF.Repository)
Generates C# model classes for Creatio entities compatible with ATF.Repository package. The generated models include:
- All entity columns with appropriate C# types
- Lookup properties (both ID and navigation properties)
- **Detail collections** - automatically generates collection properties for 1-to-many relationships
- **Validation DataAnnotations** - automatically adds validation attributes based on column metadata
- **Validation extension** - generates `BaseModelExtensions.ValidateModel()` helper for runtime model validation
- XML documentation comments from Creatio schema descriptions
- Proper attributes for ATF.Repository (`[Schema]`, `[SchemaProperty]`, `[LookupProperty]`, `[DetailProperty]`)

**Detail Relationship Support:**
When generating models, the command analyzes all entity relationships and automatically creates detail collection properties. For example, if `Activity` has a lookup to `Contact`, the generated `Contact` model will include a collection property `CollectionOfActivityByContact` representing all activities linked to that contact.

#### Single Entity Model
Generate a model for a specific entity with selected fields:

```bash
clio add-item model Contact -f Name,Email -n MyCompany.Models -d C:\Models -e MyEnvironment
```

This creates a `Contact.cs` file with properties for Name and Email fields, plus any lookup references and detail collections.

#### All Entity Models
Generate models for all entities in your Creatio instance:

```bash
clio add-item model -n MyCompany.Models -d C:\Models -e MyEnvironment
```

This creates a complete set of model classes for every entity in your Creatio environment, with:
- All columns mapped to appropriate C# types
- All lookup relationships as navigation properties
- All detail relationships as collection properties
- Comments in specified culture (default: en-US)

**Example generated detail property:**
```csharp
[DetailProperty(nameof(global::MyCompany.Models.Activity.ContactId))]
public virtual List<Activity> CollectionOfActivityByContact { get; set; }
```

#### Generated DataAnnotations
For generated model properties, clio adds DataAnnotations based on schema data type and required flags:

- `Required` for required columns
- `MinLength(1)` for required text columns
- `MaxLength(50)` for short text
- `MaxLength(250)` for medium text, phone, URL, and email
- `MaxLength(500)` for long text
- `Phone` for phone fields
- `Url` for URL fields
- `EmailAddress` for email fields

This allows immediate model validation compatibility with `System.ComponentModel.DataAnnotations`.

#### Generated BaseModel Extension
When generating models, clio also creates `BaseModelExtensions.cs` in the destination folder with:

```csharp
public static List<ValidationResult> ValidateModel(this BaseModel model)
```

The method runs DataAnnotations validation (`Validator.TryValidateObject`) and returns validation errors as a `List<ValidationResult>`.  
Use it before create/update operations to catch invalid model values locally.

### 2. Service
Adds a web service template to your project:

```bash
clio add-item service MyCustomService -n MyCompany.Services -d C:\MyProject\Services
```

### 3. Entity Listener
Adds an entity listener template to your project:

```bash
clio add-item entity-listener Contact -n MyCompany.Listeners -d C:\MyProject\Listeners
```

## Examples

### Basic Examples

#### Generate Single Model with Specific Fields
```bash
clio add-item model Contact -f Name,Email,Phone -n MyCompany.Models -d C:\Models -e production
```
Creates a `Contact.cs` model with Name, Email, and Phone properties.

#### Generate All Models with Environment Configuration
```bash
clio add-item model -n MyCompany.Models -d C:\Models -e production
```
Generates model classes for all entities in the production environment.

#### Generate Models in Current Directory
```bash
clio add-item model -n MyCompany.Models -e production
```
Models are created in the current working directory.

### Advanced Examples

#### Generate Models with Different Culture
```bash
clio add-item model -n MyCompany.Models -d C:\Models -e production -x ru-RU
```
Generates models with Russian schema descriptions.

#### Generate Service Template
```bash
clio add-item service ContactService -n MyCompany.Services -d C:\MyProject\Services
```
Creates a service class file from template.

#### Generate Entity Listener
```bash
clio add-item entity-listener Contact -n MyCompany.EventHandlers -d C:\MyProject\Listeners
```
Creates an entity listener class file from template.

### Using OAuth Authentication
```bash
clio add-item model -n MyCompany.Models -d C:\Models --uri https://mysite.creatio.com --clientId myClientId --clientSecret mySecret --authAppUri https://oauth.creatio.com
```

### Using Basic Authentication
```bash
clio add-item model -n MyCompany.Models -d C:\Models --uri https://mysite.creatio.com --Login admin --Password myPassword
```

## Output

### Model Generation
- **Console**: Progress indicator showing `Generated: X models from Y` and final directory path
- **Files**: Individual `.cs` files for each entity in the specified destination directory plus `BaseModelExtensions.cs`
- **Content**: 
  - Namespace declaration
  - Using statements for ATF.Repository
  - Class with `[Schema]` attribute
  - Properties with `[SchemaProperty]` attributes
  - DataAnnotations (`[Required]`, `[MinLength]`, `[MaxLength]`, `[Phone]`, `[Url]`, `[EmailAddress]`) where applicable
  - Lookup properties with `[LookupProperty]` attributes
  - **Detail collections with `[DetailProperty]` attributes for 1-to-many relationships**
  - XML documentation comments from schema descriptions

### Template Generation (Service/Entity Listener)
- **Console**: No specific output (command completes silently on success)
- **Files**: Single `.cs` file with the specified name
- **Content**: Template code with `<Name>` placeholder replaced with actual item name

## Custom Templates

You can add your own templates for frequently used code constructions:

1. Create a `.tpl` file in the `tpl` folder in your clio directory
2. Name it `{item-type}-template.tpl` (e.g., `controller-template.tpl`)
3. Use `<Name>` as a placeholder for the item name
4. Call: `clio add-item controller MyController -n MyNamespace -d C:\MyProject`

## Requirements

### For Model Generation
- **cliogate**: Must be installed on the target Creatio environment
- **ATF.Repository**: Required NuGet package for using generated models
- **Creatio Access**: Valid credentials (environment or direct authentication)
- **Network**: Connection to Creatio instance

### For Template-Based Items
- **Template Files**: Corresponding `.tpl` files must exist in `tpl` directory
- **Project Structure**: Destination path should be a valid project directory

## Notes

1. **Model Generation Performance**: The command uses parallel processing (up to 4 concurrent requests) to fetch schema information efficiently
2. **Detail Collections**: New feature automatically identifies and generates collection properties for all 1-to-many relationships between entities
3. **Validation Annotations**: Generated model properties include DataAnnotations inferred from Creatio schema metadata
4. **BaseModel Extension**: `BaseModelExtensions.cs` is generated with `ValidateModel()` to validate models locally before persistence
5. **Overwriting**: Existing files are overwritten without warning - use version control
6. **Namespace Validation**: For models, namespace is mandatory and must be a valid C# namespace
7. **Authentication Priority**: Using `-e` (environment) is recommended over direct credentials
8. **Culture Codes**: Use standard culture codes (en-US, ru-RU, de-DE, etc.) for schema descriptions
9. **Default Behavior**: With `-a true` (default), all entities are generated; set `-a false` and provide `ITEM_NAME` for single entity
10. **File Naming**: Generated files use the entity schema name (e.g., `Contact.cs`, `Account.cs`)
11. **Detail Naming Convention**: Detail properties follow the pattern `CollectionOf{DetailSchema}By{LookupColumn}`

## Troubleshooting

### "cliogate not found"
Install cliogate on your Creatio environment:
```bash
clio install-gate -e <environment-name>
```

### "Namespace is required for model generation"
Provide the `-n` or `--Namespace` argument:
```bash
clio add-item model -n MyCompany.Models -e production
```

### "Item name is required"
For non-model items or single model generation, provide the item name:
```bash
clio add-item service MyService -n MyNamespace
```

### Template Not Found
Ensure the template file exists in the `tpl` folder:
- Check `tpl/service-template.tpl` for services
- Check `tpl/entity-listener-template.tpl` for entity listeners

### Authentication Errors
Verify your environment configuration or credentials:
```bash
clio show-local-envs
```

## Related Commands
- [`install-gate`](../../Commands.md#install-gate) - Install cliogate on Creatio environment
- [`new-pkg`](../../Commands.md#new-pkg) - Create a new Creatio package
- [`generate-pkg-zip`](../../Commands.md#generate-pkg-zip) - Generate package archive

