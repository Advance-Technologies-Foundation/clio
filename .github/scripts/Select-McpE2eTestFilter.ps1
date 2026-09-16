# Selects the clio.mcp.e2e fixtures a pull request has to run on TeamCity and prints the
# selection as JSON: { mode, filter, fixtures, decisions }.
#
#   mode    - "full"   : the whole suite minus the categories the base filter excludes and, unless
#                        -IncludeNoEnvironment is set, minus the McpE2E.NoEnvironment tier
#             "subset" : only the listed fixtures
#             "none"   : every selected fixture is NoEnvironment-only and that tier runs on GitHub,
#                        so a TeamCity build would deploy a Creatio and run zero tests - do not queue
#   filter  - the exact `dotnet test --filter` expression to hand TeamCity as McpE2eTestFilter.
#             Empty when -IncludeNoEnvironment is set and mode is "full", meaning the TeamCity
#             default (baseFilter) applies unchanged.
#   decisions - one line per changed file: the rule that classified it and the fixtures it
#             selected, so the choice is auditable from the workflow log.
#
# The rules live in clio.mcp.e2e/TestSelection/mcp-e2e-selection.json (read its _comment).
# With -Inventory the script prints what it sees instead of a selection:
# { fixtures: { <file base name>: [fixture names] }, reachability: { <fixture>: [tool files that select it] } }.
# clio.tests/McpE2eSelectionCoverageTests.cs compares that inventory with reflection over the compiled
# e2e assembly, so this script is the single owner of the textual rules and the guard only checks that
# the text-based view and the compiled view agree.
#
# ASCII only: see queue-teamcity-build.ps1 for why.
# PositionalBinding is off so that `-ChangedFiles a b c` fails loudly instead of binding b and c to
# BaseRef and HeadRef; pass several files as `-ChangedFiles a,b,c` or as an array.
[CmdletBinding(PositionalBinding = $false)]
param(
    # Changed paths, repository-relative with forward slashes. Either this or BaseRef/HeadRef.
    [string[]] $ChangedFiles,

    # When ChangedFiles is not given, the diff is `git diff --name-only <BaseRef>...<HeadRef>`.
    [string] $BaseRef,
    [string] $HeadRef = 'HEAD',

    [string] $RepositoryRoot = (Get-Location).Path,
    [string] $ManifestPath = 'clio.mcp.e2e/TestSelection/mcp-e2e-selection.json',

    # Keep McpE2E.NoEnvironment tests in the TeamCity run (they otherwise run on GitHub).
    [switch] $IncludeNoEnvironment,

    # Print the fixture and reachability inventory instead of classifying a diff.
    [switch] $Inventory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-GlobRegex([string] $Glob) {
    # Supports `**` (any depth), `*` (within one segment) and literal paths. Anchored.
    $escaped = [regex]::Escape($Glob)
    $escaped = $escaped.Replace('\*\*/', '(?:.*/)?').Replace('\*\*', '.*').Replace('\*', '[^/]*')
    return "^$escaped$"
}

function Test-GlobMatch([string] $Path, [string[]] $Globs) {
    # Later entries win; a leading `!` negates, as in GitHub's `paths:` filter.
    $matched = $false
    foreach ($glob in $Globs) {
        $negate = $glob.StartsWith('!')
        $pattern = ConvertTo-GlobRegex ($(if ($negate) { $glob.Substring(1) } else { $glob }))
        if ($Path -cmatch $pattern) { $matched = -not $negate }
    }
    return $matched
}

function Read-Text([string] $Path) { [System.IO.File]::ReadAllText($Path) }

$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$manifestFullPath = Join-Path $root $ManifestPath
if (-not (Test-Path -LiteralPath $manifestFullPath)) { throw "Selection manifest not found: $manifestFullPath" }
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
if ($manifest.version -ne 1) { throw "Selection manifest version $($manifest.version) is not supported by this script (expected 1)." }

if ($null -eq $ChangedFiles -and -not $Inventory) {
    if ([string]::IsNullOrWhiteSpace($BaseRef)) { throw 'Pass -ChangedFiles or -BaseRef.' }
    $ChangedFiles = @(& git -C $root diff --name-only "$BaseRef...$HeadRef")
    if ($LASTEXITCODE -ne 0) { throw "git diff $BaseRef...$HeadRef failed with exit code $LASTEXITCODE." }
}
# `pwsh -File ... -ChangedFiles "a,b"` arrives as ONE string; split it so the CLI form works too.
$ChangedFiles = @($ChangedFiles | ForEach-Object { $_ -split '[,\r\n]' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim().Replace('\', '/') })

# --- fixture inventory: top-level clio.mcp.e2e/*.cs; a file may declare several fixtures --------
$fixtureRoot = Join-Path $root $manifest.fixtureRoot
# Column 0 only (a nested class is indented) and modifiers listed explicitly, so neither a nested
# helper nor an abstract base (never a runnable fixture on its own) is emitted into the filter.
$fixtureDeclaration = '(?m)^(?:public|internal)(?:[ \t]+(?:sealed|static|partial))*[ \t]+class[ \t]+([A-Za-z_]\w*)\b'
$fixtureSources = @{}     # fixture class name -> source text of its file
$fixturesByFile = @{}     # file base name     -> fixture class names declared in it
$fixtureHasSandbox = @{}  # fixture class name -> $true when its file declares any McpE2E.Sandbox test
foreach ($file in Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.cs' -File) {
    $text = Read-Text $file.FullName
    if ($text -cnotmatch '\[\s*(Test|TestFixture|TestCase|TestCaseSource|Theory)\b') { continue }
    $classes = @([regex]::Matches($text, $fixtureDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($classes.Count -eq 0) { continue }
    $fixturesByFile[$file.BaseName] = $classes
    $hasSandbox = $text.Contains('McpE2E.Sandbox') -or $text.Contains('McpE2ECategories.Sandbox')
    foreach ($class in $classes) { $fixtureSources[$class] = $text; $fixtureHasSandbox[$class] = $hasSandbox }
}

# --- tool inventory: what each Tools/**/*.cs declares ----------------------------------------------
function Get-ToolDeclarations([string] $ToolFilePath) {
    # Classes: only declarations (a modifier precedes the keyword, so a comment saying "a record"
    # does not count) whose name ends in Tool/Tools - the identifiers fixtures qualify constants
    # with (`PageSyncTool.ToolName`). Literals: only the values bound to [McpServerTool(Name = ...)],
    # either inline or through a `const string` declared in the same file.
    $text = Read-Text $ToolFilePath
    $classes = @([regex]::Matches($text, '(?m)^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected|static|sealed|abstract|partial)[\w\s]*\b(?:class|record)\s+([A-Za-z_]\w*Tools?)\b') | ForEach-Object { $_.Groups[1].Value })
    # One file may declare `ToolName` several times (one per nested tool class); keep every value.
    $constants = @{}
    foreach ($m in [regex]::Matches($text, 'const\s+string\s+(\w+)\s*=\s*"([^"]+)"')) {
        if (-not $constants.ContainsKey($m.Groups[1].Value)) { $constants[$m.Groups[1].Value] = @() }
        $constants[$m.Groups[1].Value] += $m.Groups[2].Value
    }
    $literals = @()
    foreach ($m in [regex]::Matches($text, 'McpServerTool\(\s*Name\s*=\s*("([^"]+)"|[\w.]+)')) {
        if ($m.Groups[2].Success) { $literals += $m.Groups[2].Value; continue }
        $identifier = ($m.Groups[1].Value -split '\.')[-1]
        if ($constants.ContainsKey($identifier)) { $literals += $constants[$identifier] }
    }
    return @{ Classes = @($classes | Select-Object -Unique); Literals = @($literals | Select-Object -Unique) }
}

function Select-FixturesForTool([string] $ToolFileRelative) {
    $toolFile = Join-Path $root $ToolFileRelative
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($ToolFileRelative)
    foreach ($name in $fixtureSources.Keys) {
        if ($name -cmatch "^$([regex]::Escape($stem))\w*E2ETests$") { [void]$selected.Add($name) }
    }
    if (Test-Path -LiteralPath $toolFile) {
        $decl = Get-ToolDeclarations $toolFile
        foreach ($name in $fixtureSources.Keys) {
            $source = $fixtureSources[$name]
            foreach ($class in $decl.Classes) {
                if ($source -cmatch "\b$([regex]::Escape($class))\b") { [void]$selected.Add($name); break }
            }
            foreach ($literal in $decl.Literals) {
                if ($source.Contains('"' + $literal + '"')) { [void]$selected.Add($name); break }
            }
        }
    }
    # A deleted tool file still selects fixtures by name (they will fail to compile, which is the point).
    return @($selected)
}

# --- product sources: who names a type declared in a changed non-tool file? (loaded on first use) ---
$typeDeclaration = '(?m)^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected|static|sealed|abstract|partial|readonly)[\w\s]*\b(?:class|record|interface|struct|enum)\s+([A-Za-z_]\w*)\b'
$productSources = $null
function Get-ProductSources() {
    if ($null -eq $script:productSources) {
        $script:productSources = @{}
        $productRoot = Join-Path $root $manifest.productSourceRoot
        foreach ($file in Get-ChildItem -LiteralPath $productRoot -Filter '*.cs' -File -Recurse) {
            $relative = $file.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
            if ($relative -cmatch '/(bin|obj)/') { continue }
            $script:productSources[$relative] = Read-Text $file.FullName
        }
    }
    return $script:productSources
}

function Select-FixturesForProductFile([string] $FileRelative, [ref] $Reason) {
    # Sound only when the consumer set is closed: every file under clio/ that names a type declared
    # in the changed file is a tool file (registration files excepted). A consumer elsewhere, or no
    # consumer at all (the type is reached through DI or reflection), means the blast radius is
    # unknown and the whole suite runs.
    $sources = Get-ProductSources
    if (-not $sources.ContainsKey($FileRelative)) { $Reason.Value = 'full run (file not in the tree)'; return @() }
    $types = @([regex]::Matches($sources[$FileRelative], $typeDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($types.Count -eq 0) { $Reason.Value = 'full run (declares no type)'; return @() }
    $pattern = '\b(?:' + (($types | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'
    $registration = @($manifest.registrationFiles)
    $consumers = @($sources.Keys | Where-Object { $_ -ne $FileRelative -and $registration -notcontains $_ -and $sources[$_] -cmatch $pattern } | Sort-Object)
    if ($consumers.Count -eq 0) { $Reason.Value = 'full run (no direct consumer under clio/, reached through DI or reflection)'; return @() }
    $outside = @($consumers | Where-Object { -not $_.StartsWith($manifest.toolSourceRoot) })
    if ($outside.Count -gt 0) { $Reason.Value = "full run (also used outside MCP tools: $($outside[0]))"; return @() }
    $selected = New-Object System.Collections.Generic.HashSet[string]
    foreach ($toolFile in $consumers) { foreach ($n in @(Select-FixturesForTool $toolFile)) { [void]$selected.Add($n) } }
    if ($selected.Count -eq 0) { $Reason.Value = 'full run (consuming tool files select no fixture)'; return @() }
    $Reason.Value = "product file used only by $($consumers.Count) tool file(s)"
    return @($selected)
}

# --- inventory mode ------------------------------------------------------------------------------
if ($Inventory) {
    $toolRootFull = Join-Path $root $manifest.toolSourceRoot
    $reachability = @{}
    foreach ($name in $fixtureSources.Keys) { $reachability[$name] = New-Object System.Collections.Generic.List[string] }
    foreach ($toolFile in Get-ChildItem -LiteralPath $toolRootFull -Filter '*.cs' -File -Recurse) {
        $relative = $toolFile.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
        # A tool file under fullRunPaths is classified by rule 2 and never selects anything itself.
        if (Test-GlobMatch $relative $manifest.fullRunPaths) { continue }
        foreach ($name in @(Select-FixturesForTool $relative)) { $reachability[$name].Add($relative) }
    }
    $fixturesOut = [ordered]@{}
    foreach ($key in ($fixturesByFile.Keys | Sort-Object)) { $fixturesOut[$key] = @($fixturesByFile[$key] | Sort-Object) }
    $reachOut = [ordered]@{}
    foreach ($key in ($reachability.Keys | Sort-Object)) { $reachOut[$key] = @($reachability[$key] | Sort-Object) }
    [pscustomobject]@{ fixtures = $fixturesOut; reachability = $reachOut } | ConvertTo-Json -Depth 4
    return
}

# --- classify every changed file ----------------------------------------------------------------
$mode = 'subset'
$fixtures = New-Object System.Collections.Generic.HashSet[string]
$decisions = New-Object System.Collections.Generic.List[string]
$relevantCount = 0

function Add-Decision([string] $File, [string] $Rule, [string[]] $Selected) {
    $suffix = if ($Selected.Count -gt 0) { ' -> ' + (($Selected | Sort-Object) -join ', ') } else { '' }
    $decisions.Add("$File : $Rule$suffix")
}

foreach ($file in $ChangedFiles) {
    if (-not (Test-GlobMatch $file $manifest.relevantPaths)) { Add-Decision $file 'ignored (outside relevantPaths)' @(); continue }
    $relevantCount++

    if (Test-GlobMatch $file $manifest.fullRunPaths) { $mode = 'full'; Add-Decision $file 'full run (fullRunPaths)' @(); continue }

    $fixtureRel = $manifest.fixtureRoot
    if ($file.StartsWith($fixtureRel) -and $file.EndsWith('.cs') -and -not $file.Substring($fixtureRel.Length).Contains('/')) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($file)
        if ($fixturesByFile.ContainsKey($name)) {
            $declared = @($fixturesByFile[$name])
            foreach ($n in $declared) { [void]$fixtures.Add($n) }
            Add-Decision $file 'fixture file' $declared
            continue
        }
        $mode = 'full'; Add-Decision $file 'full run (e2e file that is not a fixture)' @(); continue
    }

    $mapped = @($manifest.explicitMappings | Where-Object { Test-GlobMatch $file @($_.paths) })
    if ($mapped.Count -gt 0) {
        $names = @($mapped | ForEach-Object { $_.fixtures })
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file 'explicit mapping' $names
        continue
    }

    if ($file.StartsWith($manifest.toolSourceRoot) -and $file.EndsWith('.cs')) {
        $names = @(Select-FixturesForTool $file)
        if ($names.Count -eq 0) { $mode = 'full'; Add-Decision $file 'full run (tool file selects no fixture)' @(); continue }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file 'tool file' $names
        continue
    }

    if ($file.StartsWith($manifest.productSourceRoot) -and $file.EndsWith('.cs')) {
        $reason = ''
        $names = @(Select-FixturesForProductFile $file ([ref]$reason))
        if ($names.Count -eq 0) { $mode = 'full'; Add-Decision $file $reason @(); continue }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file $reason $names
        continue
    }

    $mode = 'full'; Add-Decision $file 'full run (no rule)' @()
}

if ($relevantCount -eq 0) { $mode = 'full'; $decisions.Add('no relevant file changed -> full run (safe default)') }
if ($mode -eq 'subset' -and $fixtures.Count -eq 0) { $mode = 'full'; $decisions.Add('subset resolved to zero fixtures -> full run') }
if ($mode -eq 'subset' -and $fixtures.Count -gt [int]$manifest.maxSubsetFixtures) {
    $decisions.Add("subset of $($fixtures.Count) fixtures exceeds maxSubsetFixtures=$($manifest.maxSubsetFixtures) -> full run")
    $mode = 'full'
}

if ($mode -eq 'subset' -and -not $IncludeNoEnvironment) {
    $withSandbox = @($fixtures | Where-Object { $fixtureHasSandbox[$_] })
    if ($withSandbox.Count -eq 0) {
        $decisions.Add('every selected fixture is NoEnvironment-only and that tier runs on GitHub -> nothing to run on TeamCity')
        $mode = 'none'
    }
}

# --- compose the filter --------------------------------------------------------------------------
$base = [string]$manifest.baseFilter
if (-not $IncludeNoEnvironment) { $base = "TestCategory!=$($manifest.noEnvironmentCategory)&$base" }

$sortedFixtures = @($fixtures | Sort-Object)
if ($mode -eq 'none') {
    $filter = ''
}
elseif ($mode -eq 'subset') {
    $names = @($sortedFixtures | ForEach-Object { "FullyQualifiedName~$($manifest.fixtureNamespace).$_" })
    $filter = '(' + ($names -join '|') + ')&' + $base
}
elseif ($IncludeNoEnvironment) {
    $filter = ''   # TeamCity default applies unchanged.
    $sortedFixtures = @()
}
else {
    $filter = $base
    $sortedFixtures = @()
}

# Defence in depth: everything in the filter comes from identifiers and the manifest, and the value
# ends up on a dotnet command line on the TeamCity agent. Refuse anything outside the filter grammar.
if ($filter -notmatch '^[A-Za-z0-9_.~=!&|()]*$') { throw "Filter contains characters outside the allowed set: $filter" }

[pscustomobject]@{
    mode      = $mode
    filter    = $filter
    fixtures  = $sortedFixtures
    decisions = @($decisions)
} | ConvertTo-Json -Depth 4
