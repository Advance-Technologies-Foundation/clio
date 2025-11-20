# Web Service Call with Inline Body Specification

## Overview

This specification describes the enhancement to the `call-service` command that allows users to pass HTTP request body directly from the command line interface, eliminating the need for intermediate request files. This is particularly useful for AI agents and development workflows where quick testing of web services is needed without file management overhead.

## Requirements

**Objective:**
Enable the `call-service` command to accept inline JSON request body via command-line parameter instead of requiring external files.

**Core Requirements:**
- Add `--body` or `-b` parameter to `CallServiceCommandOptions` for passing inline JSON
- Maintain backward compatibility with existing `--input` or `-f` file-based parameter
- Both parameters must be mutually exclusive (best practice; actual enforcement optional as one takes precedence)
- Support variable substitution within inline body (same as file-based)
- Support all HTTP methods: POST (default), GET, PUT, DELETE, PATCH
- Body should work with `--service-path` for custom endpoints
- Body should work with `--destination` to save results to file
- Enable AI agents to validate code through web services without creating temporary files
- All changes must be covered by unit tests
- Usage documentation must be added to Commands.md

## Implementation

### Architecture

The implementation leverages existing `BaseServiceCommand` infrastructure with minimal changes:

**Core Components:**
1. **CallServiceCommandOptions** - Command options class with new `--body` parameter
2. **BaseServiceCommand** - Abstract base class with request execution logic (already handles both scenarios)
3. **CallServiceCommand** - Concrete implementation for generic service calls
4. **DataServiceQuery** - Specialized implementation for dataservice operations

### Key Classes and Methods

#### CallServiceCommandOptions Enhancement
```csharp
[Verb("call-service", Aliases = new[] {"cs"}, HelpText = "Call Service Request")]
public class CallServiceCommandOptions : RemoteCommandOptions {
    [Option('m', "method", Required = false, HelpText = "HTTP method", Separator = ';')]
    public string HttpMethodName { get; set; }

    [Option('f', "input", Required = false, HelpText = "Request file", Separator = ' ')]
    public string RequestFileName { get; set; }

    [Option('b', "body", Required = false, HelpText = "Request body JSON")]
    public string RequestBody { get; set; }

    [Option('d', "destination", Required = false, HelpText = "Destination file for result")]
    public string ResultFileName { get; set; }

    [Option("service-path", Required = false, HelpText = "Route service path")]
    public string ServicePath { get; set; }

    [Option('v', "variables", Required = false, HelpText = "Variables for substitution", Separator = ';')]
    public IEnumerable<string> Variables { get; set; }
}
```

#### BaseServiceCommand.Execute() Logic
```csharp
public override int Execute(T options) {
    IsSilent = options.IsSilent;
    if (string.IsNullOrWhiteSpace(options.RequestFileName) && string.IsNullOrWhiteSpace(options.RequestBody)) {
        // No input: call with empty body
        ExecuteServiceRequest(BuildUrl(options), string.Empty, options.ResultFileName, options.HttpMethodName);
    }
    else {
        // Prefer RequestBody if set, otherwise read from file
        string requestData = string.IsNullOrWhiteSpace(options.RequestBody) 
            ? GetRequestData(options.RequestFileName) 
            : options.RequestBody;
        
        // Apply variable substitution if provided
        if (options.Variables != null && options.Variables.Any()) {
            requestData = ReplaceVariablesInJson(requestData, options.Variables);
        }
        
        ExecuteServiceRequest(BuildUrl(options), requestData, options.ResultFileName, options.HttpMethodName);
    }
    return 0;
}
```

**Key Points:**
- Priority: `--body` > `--input` (if both provided, `--body` takes precedence)
- Variable substitution works with inline body
- No changes needed to core Execute logic; existing implementation already supports this
- HTTP method defaults to POST if not specified

### Behavior Specification

#### Parameter Priority and Precedence
| Scenario | Behavior | Result |
|----------|----------|--------|
| `--body` provided | Uses inline body | Executes with provided JSON |
| `--input` provided | Reads from file | Executes with file content |
| Both `--body` and `--input` | Uses `--body`, ignores file | Executes with inline body |
| Neither provided | Empty body request | GET-like request with no data |
| `--body` with `--variables` | Substitutes variables in body | Variables replaced before execution |
| `--input` with `--variables` | Substitutes variables from file | Variables replaced in file content |

#### HTTP Method Support
```
Default method:  POST
Supported:       POST, GET, PUT, DELETE, PATCH (via -m/--method)
Examples:
  -m POST       # Default if not specified
  -m GET        # Retrieve data
  -m PUT        # Update resource
  -m DELETE     # Delete resource
```

#### Service Path Handling
```
--service-path can be:
- Relative:     ServiceModel/ApplicationInfoService.svc/GetApplicationInfo
- Absolute URL: http://localhost:8080/ServiceModel/YourService.svc
- Custom route: rest/CreatioApiGateway/GetData
```

#### Variable Substitution Rules
- Format: `{{variableName}}`
- Case-sensitive variable names
- Variables provided via `-v` or `--variables` as `key=value` pairs
- Multiple variables separated by `;`
- Non-existent variables remain unchanged in body

## Usage Examples

### Basic Inline Body with Service Call

**Simple GET request:**
```bash
clio call-service --service-path ServiceModel/ApplicationInfoService.svc/GetApplicationInfo -e myEnv
```

**POST with inline JSON body:**
```bash
clio call-service --service-path ServiceModel/YourService.svc/YourMethod \
  --body '{"key":"value","number":123}' \
  -e myEnv
```

**Save result to file:**
```bash
clio call-service --service-path ServiceModel/YourService.svc/YourMethod \
  --body '{"key":"value"}' \
  --destination result.json \
  -e myEnv
```

### Using Different HTTP Methods

**PUT request with inline body:**
```bash
clio call-service --service-path ServiceModel/UpdateService.svc/UpdateData \
  --method PUT \
  --body '{"id":1,"name":"Updated"}' \
  -e myEnv
```

**DELETE request:**
```bash
clio call-service --service-path ServiceModel/DeleteService.svc/RemoveItem \
  --method DELETE \
  --body '{"id":123}' \
  -e myEnv
```

### Variable Substitution with Inline Body

**Single variable:**
```bash
clio call-service --service-path ServiceModel/UserService.svc/GetUser \
  --body '{"userId":"{{userId}}"}' \
  --variables userId=12345 \
  -e myEnv
```

**Multiple variables:**
```bash
clio call-service --service-path ServiceModel/SearchService.svc/Search \
  --body '{"firstName":"{{firstName}}","lastName":"{{lastName}}","age":"{{age}}"}' \
  --variables firstName=John;lastName=Doe;age=30 \
  -e myEnv
```

### File-Based Request (Backward Compatibility)

**Traditional file-based approach still works:**
```bash
clio call-service --service-path ServiceModel/YourService.svc/YourMethod \
  --input request.json \
  --destination result.json \
  -e myEnv
```

Where `request.json` contains:
```json
{
  "key": "value",
  "nested": {
    "property": "data"
  }
}
```

### AI Agent Workflow Example

**Code validation scenario - AI writing and testing code:**
```bash
# Step 1: AI writes code
# (generates some C# or JavaScript code)

# Step 2: AI wants to validate via web service
clio call-service --service-path ServiceModel/ValidationService.svc/ValidateCode \
  --body '{"code":"var x = 5; console.log(x);","language":"javascript"}' \
  --destination validation-result.json \
  -e dev-environment

# Step 3: Check result and iterate if needed
cat validation-result.json
```

## Testing Coverage

**Test Scenarios:**

1. **CallServiceCommand_WithInlineBody_ExecutesSuccessfully**
   - Verifies POST request with inline JSON body is sent correctly
   - Asserts response is returned and processed

2. **CallServiceCommand_WithInlineBodyAndDestination_SavesResultToFile**
   - Tests that result file is created when `--destination` is provided
   - Verifies beautified JSON is written

3. **CallServiceCommand_WithInlineBodyAndVariables_SubstitutesVariables**
   - Tests variable substitution works with inline body
   - Validates multiple variables are replaced correctly
   - Ensures non-existent variables remain unchanged

4. **CallServiceCommand_WithFileInput_WorksAsBeforeRegression**
   - Ensures backward compatibility with `--input` file parameter
   - Validates file is read and request executed

5. **CallServiceCommand_WithBothBodyAndFile_BodyTakesPrecedence**
   - When both `--body` and `--input` provided, inline body used
   - File parameter ignored

6. **CallServiceCommand_WithoutBodyOrFile_ExecutesWithEmptyPayload**
   - Calling service without any input works correctly
   - Useful for GET requests without parameters

7. **CallServiceCommand_WithDifferentHttpMethods_ExecutesCorrectly**
   - Tests POST (default), GET, PUT, DELETE methods
   - Verifies correct method is used in request

8. **CallServiceCommand_WithCustomServicePath_BuildsUrlCorrectly**
   - Tests relative and absolute service paths
   - Validates URL construction for custom endpoints

9. **DataServiceQuery_WithInlineBodyAndOperationType_ExecutesDataServiceRequest**
   - Tests that DataServiceQuery inheriting functionality works
   - Validates operation type (SELECT, INSERT, UPDATE, DELETE) with inline body

10. **CallServiceCommand_WithSilentFlag_SupressesConsoleOutput**
    - Tests that `--silent` flag suppresses output
    - Verifies file is still written if destination provided

## Integration with Existing Commands

**Affected Commands:**
- `call-service` (cs) - Primary command enhanced with `--body` parameter
- `dataservice` (ds) - Inherits enhancement through inheritance chain

**Backward Compatibility:**
- All existing file-based workflows continue to work unchanged
- No breaking changes to command structure
- Existing scripts and automation unaffected

**User Feedback:**
- Inline body requests logged same as file-based: `[Request] POST /ServicePath`
- Result output beautified with JSON formatting
- Error messages consistent across both input methods

## Cross-Platform Usage

### Linux / macOS (Bash, Zsh)
```bash
# Single quotes preserve JSON formatting
clio call-service --body '{"key":"value"}' --service-path ServicePath -e env
```

### PowerShell (Windows)
```powershell
# Escape double quotes with backslash
clio call-service --body "{`"key`":`"value`"}" --service-path ServicePath -e env

# Or use single quotes with escaped quotes inside
clio call-service --body '{"key":"value"}' --service-path ServicePath -e env
```

### Command Line Escaping Guidelines
- **Bash/Zsh**: Use single quotes for JSON: `'{"key":"value"}'`
- **PowerShell**: Use backtick escaping: `"{`"key`":`"value`"}"`  or single quotes
- **CMD.exe**: Escape quotes with backslash: `"{\"key\":\"value\"}"`
- **Recommendation**: For complex JSON, use `--input` with file instead

## Technical Implementation Details

### Code Changes Checklist
- [ ] Add `[Option('b', "body")]` attribute to `RequestBody` property in `CallServiceCommandOptions`
- [ ] Update `Commands.md` with `--body` parameter documentation
- [ ] Create comprehensive unit tests covering all scenarios
- [ ] Verify DI registration in `BindingsModule.cs`
- [ ] Update README.md cross-reference if needed

### No Breaking Changes
- Existing method signatures unchanged
- Execute logic in `BaseServiceCommand` already supports scenario
- Variable substitution logic reused from existing implementation
- HTTP method support leverages existing switch statement

### Performance Impact
- Minimal: inline body parsing same complexity as file read
- No additional allocations beyond existing request handling
- Variable substitution unchanged from current implementation

## Future Enhancements

Potential improvements (not in current scope):
- Input validation/schema validation for JSON body
- Request body templates with standard variables
- Response transformation/filtering
- Request/response logging to files
- Header support via command line (`-H/--header Name:Value`)
- Environment variable interpolation in body
- Conditional requests based on response values
