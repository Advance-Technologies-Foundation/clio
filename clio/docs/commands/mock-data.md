# mock-data

## Command Type

    Testing commands

## Name

mock-data - generates mock data for unit tests from Creatio OData models

## Description

Extracts schema names from model classes and downloads corresponding data
from a Creatio instance via OData endpoints. The data is saved as JSON files
that can be used for unit testing with ATF.Repository.

The command:
1. Scans model files in the specified directory for [Schema("")] attributes
2. Extracts schema names from the model classes
3. Downloads data from Creatio OData endpoints for each schema
4. Saves the data as JSON files in the specified data folder

This is useful for creating test fixtures with real data from Creatio.

## Aliases

data-mock

## Example

```bash
clio mock-data -m D:\Projects\MyProject\Models -d D:\Projects\MyProject\Tests\TestsData -e MyDevCreatio

clio mock-data --models C:\Dev\Models --data C:\Dev\Tests\Data -e prod

# Exclude system models
clio mock-data -m .\Models -d .\Tests\Data -e dev --exclude-models VwSys
```

## Options

```bash
-m, --models <PATH>
Path to the folder containing model classes (required)

-d, --data <PATH>
Path where the JSON data files will be saved (required)

-e, --environment <ENVIRONMENT_NAME>
Target Creatio environment name (required)

-x, --exclude-models <PATTERN>
Pattern to exclude models from data extraction (optional, default: "VwSys")
Models containing this pattern in their name will be skipped
```

## Prerequisites

- Creatio instance must be accessible
- Valid credentials for the target environment
- Model classes with [Schema("")] attributes
- ATF.Repository for unit testing (recommended)

## Notes

- The command processes up to 8 models in parallel for performance
- If data extraction fails for a model, a warning is logged but execution continues
- Extracted data is saved as <SchemaName>.json files
- System views (VwSys*) are excluded by default

## Related Commands

    execute-assembly-code - execute code against Creatio
    assert - run unit tests

## Reporting Bugs

    https://github.com/Advance-Technologies-Foundation/clio

- [Clio Command Reference](../../Commands.md#mock-data)
