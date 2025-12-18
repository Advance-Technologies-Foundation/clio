# IIS Scanner Fix Summary

## Problem Description
When running `clio-dev reg-web-app --add-from-iis`, three incorrect behaviors were observed:

1. **"default" site** was registered even though it's NOT a Creatio site
2. **"default/d_nf" nested app** had the wrong physical path:
   - **Incorrect**: `C:\inetpub\wwwroot\default` (parent site's path)
   - **Correct**: `C:\inetpub\wwwroot\clio\d_nf` (actual app's path)
3. **NetFramework environment path** included `Terrasoft.WebApp` folder:
   - **Incorrect**: `C:\inetpub\wwwroot\clio\d_nf\Terrasoft.WebApp`
   - **Correct**: `C:\inetpub\wwwroot\clio\d_nf` (parent of Terrasoft.WebApp)

## Root Causes & Fixes

### Issue 1: Non-Creatio Sites Being Registered
**Location**: `IISScannerRequest.cs`, line ~353 (in `GetSites` method)

**Problem**:
```csharp
sites.Where(site => !result.ContainsKey(site.Name)).ToList().ForEach(site => {
    result.Add(site.Name, new App(site.PhysicalPath, site.Uris.FirstOrDefault()));
});
```
This was adding ALL top-level sites without checking if they were Creatio sites.

**Fix**:
```csharp
sites.Where(site => !result.ContainsKey(site.Name) && DetectSiteType(site.PhysicalPath) != SiteType.NotCreatioSite)
    .ToList()
    .ForEach(site => {
        result.Add(site.Name, new App(site.PhysicalPath, site.Uris.FirstOrDefault()));
    });
```
Now filters sites using `DetectSiteType()` to only include actual Creatio sites (those with `Terrasoft.WebApp` or `Terrasoft.Configuration` folders).

### Issue 2: NetFramework Apps Environment Path Includes Terrasoft.WebApp
**Location**: `IISScannerRequest.cs`, line ~345 (in `GetSites` method)

**Problem**:
```csharp
result.Add(rootSite.Name + newPath, new App(webApp.PhysicalPath, value));
```
For NetFramework apps, `webApp.PhysicalPath` points to the `Terrasoft.WebApp` folder itself (e.g., `C:\...\Terrasoft.WebApp`), but the environment path should be its parent directory.

**Fix**:
```csharp
// For NetFramework apps, the environment path should be the parent of Terrasoft.WebApp
// webApp.PhysicalPath points to "C:\...\Terrasoft.WebApp", we need its parent
string environmentPath = Directory.GetParent(webApp.PhysicalPath)?.FullName ?? webApp.PhysicalPath;
result.Add(rootSite.Name + newPath, new App(environmentPath, value));
```
Now extracts the parent directory of `Terrasoft.WebApp` as the environment path.

## How It Works

### DetectSiteType Function
The `DetectSiteType` function checks the physical path for:
- **NetFramework sites**: Contain a `Terrasoft.WebApp` folder
- **Core sites**: Contain a `Terrasoft.Configuration` folder  
- **NotCreatioSite**: Neither folder exists

### NetFramework Path Resolution
For NetFramework applications (identified by `/0` path ending with `Terrasoft.WebApp`):
1. PowerShell `Get-WebApplication` returns the path to `Terrasoft.WebApp` folder
2. The code uses `Directory.GetParent()` to get the parent directory
3. This parent directory is the actual Creatio environment root

### NetCore Applications
NetCore applications work correctly as-is because their physical path already points to the environment root (which contains `Terrasoft.Configuration`).

## Expected Results
After these fixes, running `clio-dev reg-web-app --add-from-iis` will:
1. ✅ **Skip "default" site** (not a Creatio site - no Terrasoft folders)
2. ✅ **Register "default/d_nf"** with correct environment path: `C:\inetpub\wwwroot\clio\d_nf` (parent of Terrasoft.WebApp)
3. ✅ **NetFramework apps** will have correct environment paths (parent of Terrasoft.WebApp)
4. ✅ **NetCore apps** continue to work correctly (already have correct paths)

## Example Scenarios

### Before Fix:
```json
{
  "default": {
    "EnvironmentPath": "C:\\inetpub\\wwwroot\\default"  // ❌ Not a Creatio site
  },
  "default/d_nf": {
    "EnvironmentPath": "C:\\inetpub\\wwwroot\\default"  // ❌ Wrong path (parent site)
  }
}
```

### After Fix:
```json
{
  "default/d_nf": {
    "EnvironmentPath": "C:\\inetpub\\wwwroot\\clio\\d_nf"  // ✅ Correct path
  }
}
```
Note: "default" is not registered (correctly excluded as non-Creatio site)

## Testing
To verify the fix:
1. Run: `clio-dev reg-web-app --add-from-iis`
2. Check the registered environments in your settings file
3. Verify:
   - ✅ Non-Creatio sites (without Terrasoft folders) are not registered
   - ✅ Nested NetFramework applications have correct environment paths (parent of Terrasoft.WebApp)
   - ✅ NetCore applications continue to work correctly
   - ✅ Environment paths do not end with "Terrasoft.WebApp"

## Related Files Changed
- `c:\Projects\clio\clio\Requests\IISScannerRequest.cs` - Fixed `GetSites` method (lines ~345 and ~353)
- `c:\Projects\clio\clio.tests\Requests\IISScannerHandler.Tests.cs` - Added integration tests

