# Integrating an Angular (Freedom UI) project into the .NET solution via an `.esproj`

> Reference for teaching the clio `new-ui-project` (and related) commands to wire a generated
> Angular project into `MainSolution.slnx` so that `dotnet build` also builds the JavaScript bundle.
>
> Everything here was validated end‑to‑end on a Creatio **File System Mode (FSM)** workspace with
> the Angular client output landing inside a Creatio package.

---

## 1. Goal / outcome

After this wiring, a single command builds **both** the C# package assembly and the Angular client bundle:

```bash
dotnet build MainSolution.slnx -c dev-n8     # builds UsrRssReader.dll AND runs `npm run build`
dotnet clean MainSolution.slnx -c dev-n8     # removes the bundle (npm run clean); next build regenerates it
```

This is achieved with **four** coordinated artifacts:

| # | File                                | Change                                             | Owner tool                  |
|---|-------------------------------------|----------------------------------------------------|-----------------------------|
| 1 | `projects/<ngproj>/<ngproj>.esproj` | **new** – MSBuild wrapper around the npm build     | MSBuild / VS JavaScript SDK |
| 2 | `global.json` (repo root)           | **new/updated** – pins the JavaScript SDK version  | MSBuild SDK resolver        |
| 3 | `MainSolution.slnx` (repo root)     | **updated** – add the esproj + `<Build />` element | Solution build              |
| 4 | `projects/<ngproj>/package.json`    | **updated** – add a `clean` npm script             | npm / esproj `CleanCommand` |

---

## 2. Template parameters

When generating these files, substitute the following. Example values are from the reference project.

| Placeholder               | Meaning                                                                                                               | Example                                               |
|---------------------------|-----------------------------------------------------------------------------------------------------------------------|-------------------------------------------------------|
| `{NgProjectName}`         | npm package name **and** project folder/file name                                                                     | `rss_reader`                                          |
| `{NgProjectDir}`          | Angular project dir, relative to repo root                                                                            | `projects/rss_reader`                                 |
| `{CreatioPackage}`        | Owning Creatio package                                                                                                | `UsrRssReader`                                        |
| `{OutputPathFromNgProj}`  | `angular.json` `outputPath`, relative to the Angular project dir                                                      | `../../packages/UsrRssReader/Files/src/js/rss_reader` |
| `{BundleDirFromRepoRoot}` | Same output folder, relative to repo root                                                                             | `packages/UsrRssReader/Files/src/js/rss_reader`       |
| `{BundleDirFromEsproj}`   | Same output folder, relative to the esproj dir (== `{OutputPathFromNgProj}` since esproj sits next to `angular.json`) | `..\..\packages\UsrRssReader\Files\src\js\rss_reader` |
| `{SolutionConfigs}`       | All solution build configs                                                                                            | `Debug;Release;dev-n8;dev-nf`                         |
| `{NonStandardConfigs}`    | Configs that are **not** `Debug`/`Release` (these are the ones that need the `<Build />` workaround)                  | `dev-n8`, `dev-nf`                                    |
| `{JsSdkVersion}`          | Pinned `Microsoft.VisualStudio.JavaScript.Sdk` version                                                                | `1.0.5581896`                                         |
| `{TestFramework}`         | JS test framework, if any                                                                                             | `Jest`                                                |

> **Key invariant:** the esproj lives **next to** `angular.json`/`package.json` (i.e. in `{NgProjectDir}`).
> The JavaScript SDK runs `npm` with the esproj's directory as the working directory, so all npm
> scripts and relative paths resolve from there.

---

## 3. File 1 — `{NgProjectDir}/{NgProjectName}.esproj`

```xml
<Project Sdk="Microsoft.VisualStudio.JavaScript.Sdk">

  <PropertyGroup>
    <!-- Declared so the project nominally knows the solution's configs. NOTE: this alone is NOT
         enough for the .slnx to select it under custom configs — see the <Build /> note in the .slnx. -->
    <Configurations>{SolutionConfigs}</Configurations>

    <!-- The npm script run on `dotnet build`. The SDK also runs `npm install` first, but only when
         package.json / package-lock.json changed since the last build. -->
    <BuildCommand>npm run build</BuildCommand>
    <StartupCommand>npm start</StartupCommand>
    <ShouldRunBuildScript>true</ShouldRunBuildScript>

    <!-- Where `ng build` writes the bundle (mirrors angular.json `outputPath`). Used by MSBuild/VS
         for up-to-date checks and the project's output model. ng build leaves deleteOutputPath at
         its default (true), so each build recreates this folder. -->
    <BuildOutputFolder>$(MSBuildProjectDirectory)\{BundleDirFromEsproj}</BuildOutputFolder>

    <!-- The JS SDK does NOT delete BuildOutputFolder on clean (npm outputs aren't MSBuild-tracked),
         so we run an explicit npm clean script. Safe because the next build regenerates the bundle. -->
    <CleanCommand>npm run clean</CleanCommand>

    <!-- Optional: only if the project has JS unit tests. -->
    <JavaScriptTestRoot>src\</JavaScriptTestRoot>
    <JavaScriptTestFramework>{TestFramework}</JavaScriptTestFramework>
  </PropertyGroup>

</Project>
```

### Property reference

| Property | Required? | Purpose |
|---|---|---|
| `Sdk` attr (no version) | yes | Loads the JavaScript SDK; version comes from `global.json` (File 2). |
| `Configurations` | recommended | Lists valid project configs. Does **not** by itself fix custom-config selection in `.slnx`. |
| `BuildCommand` | yes | npm script run on Build (default would be `npm run build`; set explicitly for clarity). |
| `ShouldRunBuildScript` | yes | Must be `true` or Build does nothing. |
| `StartupCommand` | optional | Used by VS "Run"/debug. |
| `BuildOutputFolder` | recommended | Output location for MSBuild/VS tooling & incremental checks. Mirrors `angular.json` `outputPath`. |
| `CleanCommand` | recommended | npm script run on `dotnet clean`. Needed because the SDK won't delete the bundle itself. |
| `JavaScriptTestRoot` / `JavaScriptTestFramework` | optional | Enables `dotnet test` discovery for the JS project. Omit if no tests. |

---

## 4. File 2 — `global.json` (repo root)

```json
{
  "msbuild-sdks": {
    "Microsoft.VisualStudio.JavaScript.Sdk": "{JsSdkVersion}"
  }
}
```

- **Why centralize here instead of inline (`Sdk="...JavaScript.Sdk/{version}"`)?** It keeps the version
  in one place, applies to *every* esproj added later, and avoids the multi-version warning (MSB4241/MSB3780).
  The esproj's `Sdk` attribute is therefore version-less.
- If a `global.json` already exists, **merge** the `msbuild-sdks` key (don't overwrite the `sdk` node or others).
- **There is no "latest"/floating option** for MSBuild project SDKs — the version must be exact. The
  `rollForward` feature only applies to the `.NET SDK` (`sdk` node), not to `msbuild-sdks`.

---

## 5. File 3 — `MainSolution.slnx` (repo root)

Add the project reference **with an empty `<Build />` child**:

```xml
<Solution>
  <Configurations>
    <BuildType Name="Debug" />
    <BuildType Name="Release" />
    <BuildType Name="dev-n8" />
    <BuildType Name="dev-nf" />
  </Configurations>

  <Project Path="packages\{CreatioPackage}\Files\{CreatioPackage}.csproj" />

  <Project Path="{NgProjectDir-backslashes}\{NgProjectName}.esproj">
    <!-- JS SDK project has no dev-n8/dev-nf configs; force it to participate in every solution config. -->
    <Build />
  </Project>
</Solution>
```

### ⚠️ The single most important gotcha: `<Build />`

The solution defines **non-standard** build configs (`dev-n8`, `dev-nf`). The JavaScript SDK project
only understands `Debug`/`Release`, so under `dev-n8` the solution build prints:

```
The project "<ngproj>" is not selected for building in solution configuration "dev-n8|Any CPU".
```

…and **silently skips the npm build**. Things that were tried and **did NOT fix it**:

- Adding `<Configurations>Debug;Release;dev-n8;dev-nf</Configurations>` to the esproj.
- An explicit per-project mapping `<Configuration Solution="dev-n8|*" Project="Debug|Any CPU" />` in the `.slnx`.

What **does** fix it: add an empty **`<Build />`** element under the `<Project>` in the `.slnx`
(the same idiom Microsoft uses for `docker-compose.dcproj`). It forces the project to participate in
**every** solution configuration. Because `ng build` is configuration-agnostic, all configs behave the same.

> Rule of thumb for the clio command: **whenever the solution has any config other than `Debug`/`Release`,
> emit `<Build />` for the esproj.** It's harmless to always include it.

Path separators in `.slnx` use Windows backslashes (e.g. `projects\rss_reader\rss_reader.esproj`).

---

## 6. File 4 — `{NgProjectDir}/package.json` clean script

Add a `clean` script so `dotnet clean` can remove the bundle (cross-platform, no extra dependency):

```jsonc
{
  "scripts": {
    "build": "ng build",
    "clean": "node -e \"require('fs').rmSync('{OutputPathFromNgProj}', { recursive: true, force: true })\""
  }
}
```

- The path is **relative to the Angular project dir** (npm's working directory), i.e. the same value as
  `angular.json` `outputPath` → `{OutputPathFromNgProj}`.
- `force: true` makes it a no-op when the folder is already gone (idempotent).

---

## 7. How SDK install/restore actually works (no manual install needed)

- The `Sdk` reference is resolved by MSBuild's **NuGet-based SDK resolver** at *evaluation time*. When a
  version is present (here via `global.json`), it downloads the package from the configured NuGet feed
  (nuget.org by default) into the global packages cache (`~/.nuget/packages`) automatically.
- A fresh clone therefore needs **no separate install step** — the first `dotnet build`/`dotnet restore`
  fetches it; subsequent builds are offline-fast from cache.
- Failure mode if the version can't be fetched (offline / wrong feed): `MSB4236: The SDK '...' could not be found`.

### Real prerequisites for a contributor (document these in a README)

| Prerequisite | Auto-installed by build? |
|---|---|
| .NET SDK (`dotnet` CLI) | No — install once |
| **Node.js + npm** (the esproj shells out to `npm`; the SDK does **not** install Node) | No — install once |
| Network to nuget.org on first build (to fetch the JS SDK package) | Package: yes; feed access: no |

> Tip: keep `engines` in `package.json` (e.g. `"node": ">=18.19.1"`) as a contributor hint.
> For air-gapped/offline teams, host the SDK package on an internal feed or pre-populate the NuGet cache,
> and add a `NuGet.config` pinning the feed.

---

## 8. Generation algorithm for the clio command

1. Resolve parameters (Section 2). `{OutputPathFromNgProj}` comes from the generated `angular.json` `outputPath`.
2. **Write** `{NgProjectDir}/{NgProjectName}.esproj` from the Section 3 template.
3. **Ensure** `global.json` at repo root contains `msbuild-sdks["Microsoft.VisualStudio.JavaScript.Sdk"] = {JsSdkVersion}`
   (create if missing; merge the key if present).
4. **Add** the `<Project Path="...esproj">` entry to `MainSolution.slnx`. If the solution has any config
   beyond `Debug`/`Release`, include the `<Build />` child (recommended: always include it).
5. **Add** the `clean` script to `package.json` (and confirm a `build` script exists).
6. Leave the esproj **version-less** (`Sdk="Microsoft.VisualStudio.JavaScript.Sdk"`) since the version is in `global.json`.

---

## 9. Verification

```bash
# Build: should run `npm run build` and regenerate the bundle under the package.
dotnet build MainSolution.slnx -c dev-n8 -v n
#   expect: "...esproj ... npm run build ... Browser application bundle generation complete"
#   must NOT see: "is not selected for building in solution configuration"

# Clean: should remove the bundle folder.
dotnet clean MainSolution.slnx -c dev-n8
#   then: the bundle directory ({BundleDirFromRepoRoot}) should be gone, and a rebuild restores it.
```

Confirm the bundle file (e.g. `{BundleDirFromRepoRoot}/remoteModuleEntry.js`) disappears after clean and
reappears after build.

---

## 10. Gotchas summary (hard-won)

1. **`<Build />` in `.slnx` is mandatory** when the solution has non-`Debug`/`Release` configs, or the
   esproj is silently excluded ("not selected for building") and npm never runs.
2. **No floating SDK versions** — `Sdk` references need an exact version; centralize it in `global.json`.
3. **The SDK doesn't clean `BuildOutputFolder`** — npm outputs aren't MSBuild-tracked, so wire an explicit
   `CleanCommand` → `npm run clean`.
4. **Two output paths, two tools** — `angular.json` `outputPath` (for `ng build`) and `BuildOutputFolder`
   (for MSBuild/VS) must be kept in sync; neither tool reads the other's file.
5. **Node.js is a hard prerequisite** — the JS SDK runs `npm` but never installs Node.
6. **esproj must sit next to `package.json`** — it defines the npm working directory.
7. **FSM note (Creatio-specific):** with the Angular `outputPath` pointing into the package, `ng build`
   deploys straight to the File-System-Mode location; a browser hard-reload (Ctrl+Shift+R) picks up the
   new bundle. (Deployment/restart is orthogonal to this solution-build wiring.)
