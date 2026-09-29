# Selects the clio.mcp.e2e fixtures a pull request has to run on TeamCity and prints the
# selection as JSON: { mode, filter, fixtures, decisions }.
#
#   mode    - "full"   : the whole suite minus the categories the base filter excludes and, unless
#                        -IncludeNoEnvironment is set, minus the McpE2E.NoEnvironment tier
#             "subset" : only the listed fixtures
#             "none"   : no fixture in this suite can observe the change - either nothing the diff
#                        touches is reachable from an MCP entry point, or every selected fixture is
#                        NoEnvironment-only and that tier runs on GitHub. Do not queue a build.
#   filter  - the exact `dotnet test --filter` expression to hand TeamCity as McpE2eTestFilter.
#             Empty when -IncludeNoEnvironment is set and mode is "full", meaning the TeamCity
#             default (baseFilter) applies unchanged.
#   decisions - one line per changed file: the rule that classified it and the fixtures it
#             selected, so the choice is auditable from the workflow log.
#
# The rules live in clio.mcp.e2e/TestSelection/mcp-e2e-selection.json (read its _comment).
# With -Inventory the script prints what it sees instead of a selection:
# { fixtures: { <file base name>: [fixture names] }, reachability: { <fixture>: [tool files that select it] },
#   uncoveredTools: [tool files no fixture names],
#   uncoveredEntryPoints: [MCP resource/prompt files no fixture names],
#   unreachableProductFiles: [files no fixture can observe], lexerResidue: [files that survived blanking],
#   noEnvironmentOnly: { <fixture>: true when none of its tests survives the TeamCity subset filter },
#   survivesTeamCity: { <fixture>: true when at least one visible test survives that filter } }.
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
if ($manifest.version -ne 2) { throw "Selection manifest version $($manifest.version) is not supported by this script (expected 2)." }

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
$fixtureNoEnvironmentOnly = @{}  # fixture class name -> $true when none of its tests survives the TeamCity subset filter

# The tier is read from Category attributes, never from free text: a doc comment that mentions a tier
# must not flip the verdict. A constant argument resolves only through McpE2ECategories; any other
# constant stays unknown, which reads as "not NoEnvironment" - the safe direction.
$baseFilterExcluded = @([regex]::Matches([string]$manifest.baseFilter, 'TestCategory!=([^&|()\s]+)') | ForEach-Object { $_.Groups[1].Value })
$categoryConstants = @{}
$categoryConstantsPath = Join-Path $fixtureRoot 'Support/Configuration/McpE2ECategories.cs'
if (Test-Path -LiteralPath $categoryConstantsPath) {
    foreach ($m in [regex]::Matches((Read-Text $categoryConstantsPath), 'const\s+string\s+(\w+)\s*=\s*"([^"]+)"')) {
        $categoryConstants["McpE2ECategories.$($m.Groups[1].Value)"] = $m.Groups[2].Value
    }
}
$testAttribute = '[\[,]\s*(?:NUnit\.Framework\.)?(?:Test|TestCase|TestCaseSource|Theory)\s*[\],(]'

function Remove-Comments([string] $Text) {
    # Block comments, then line comments not preceded by ':' so a URL literal keeps its tail.
    $withoutBlocks = [regex]::Replace($Text, '(?s)/\*.*?\*/', ' ')
    return [regex]::Replace($withoutBlocks, '(?m)(?<!:)//.*$', '')
}

function Get-Categories([string] $AttributeText) {
    @([regex]::Matches($AttributeText, '\bCategory(?:Attribute)?\s*\(\s*(?:"([^"]*)"|([\w.]+))\s*\)') | ForEach-Object {
        if ($_.Groups[1].Success) { $_.Groups[1].Value }
        elseif ($categoryConstants.ContainsKey($_.Groups[2].Value)) { $categoryConstants[$_.Groups[2].Value] }
        else { '?' + $_.Groups[2].Value }
    })
}

# Category sets of the test methods in a class body: one entry per run of attribute lines that carries
# a test attribute. A run ends at the first line that is not attribute-only, so a one-line
# `[Test] public void A() => ...;` is its own run. A multi-line attribute ends the run early and can
# only lose categories, which reads as "not NoEnvironment".
function Get-TestMethodCategories([string] $Body) {
    $methods = New-Object System.Collections.Generic.List[object]
    $run = New-Object System.Text.StringBuilder
    foreach ($line in ($Body -split '\r?\n')) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('[')) {
            [void]$run.Append($trimmed).Append("`n")
            if ($trimmed.EndsWith(']')) { continue }
        } elseif ($trimmed.Length -eq 0 -and $run.Length -gt 0) {
            continue
        }
        if ($run.Length -gt 0) {
            $attributes = $run.ToString()
            if ($attributes -cmatch $testAttribute) { $methods.Add(@(Get-Categories $attributes)) }
            [void]$run.Clear()
        }
    }
    return , $methods
}

# NoEnvironment-only means no test of the fixture survives the TeamCity subset filter
# (TestCategory!=McpE2E.NoEnvironment&<baseFilter>): the class itself carries McpE2E.NoEnvironment, or
# every test method carries it or a category baseFilter excludes, and at least one carries it.
# McpE2E.Sandbox anywhere keeps the fixture on TeamCity, as before. A fixture with no visible test
# (all inherited from a base in another file) is never NoEnvironment-only.
function Test-NoEnvironmentOnly([string] $ClassAttributes, [string] $Body) {
    $classCategories = @(Get-Categories $ClassAttributes)
    $methods = Get-TestMethodCategories $Body
    $all = @($classCategories) + @($methods | ForEach-Object { $_ })
    if ($all -contains 'McpE2E.Sandbox') { return $false }
    if ($classCategories -contains 'McpE2E.NoEnvironment') { return $true }
    if ($methods.Count -eq 0) { return $false }
    $anyNoEnvironment = $false
    foreach ($categories in $methods) {
        if (@($categories) -contains 'McpE2E.NoEnvironment') { $anyNoEnvironment = $true; continue }
        if (-not (@($categories) | Where-Object { $baseFilterExcluded -contains $_ })) { return $false }
    }
    return $anyNoEnvironment
}

# True when at least one visible test of the fixture survives a TeamCity filter that excludes the
# given categories - its class and method categories together intersect none of them. A fixture with
# no visible test (all inherited from a base in another file) is assumed to run. Unlike
# Test-NoEnvironmentOnly this says nothing about GitHub: a fixture whose tests are all
# McpE2E.ProcessDesigner or McpE2E.Manual runs on no pull-request lane, and a TeamCity build queued
# for it deploys a Creatio to execute zero tests.
function Test-SurvivesTeamCityFilter([string] $ClassAttributes, [string] $Body, [string[]] $Excluded) {
    $classCategories = @(Get-Categories $ClassAttributes)
    if (@($classCategories | Where-Object { $Excluded -contains $_ }).Count -gt 0) { return $false }
    $methods = Get-TestMethodCategories $Body
    if ($methods.Count -eq 0) { return $true }
    foreach ($categories in $methods) {
        if (@(@($categories) | Where-Object { $Excluded -contains $_ }).Count -eq 0) { return $true }
    }
    return $false
}

$fixtureSurvivesTeamCity = @{}  # fixture class name -> $true when a test survives the pull-request TeamCity filter
$teamCityExcluded = @($baseFilterExcluded)
if (-not $IncludeNoEnvironment) { $teamCityExcluded += [string]$manifest.noEnvironmentCategory }

foreach ($file in Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.cs' -File) {
    $text = Read-Text $file.FullName
    if ($text -cnotmatch '\[\s*(Test|TestFixture|TestCase|TestCaseSource|Theory)\b') { continue }
    $classes = @([regex]::Matches($text, $fixtureDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($classes.Count -eq 0) { continue }
    $fixturesByFile[$file.BaseName] = $classes
    # The tier is read per fixture, not per file: one file may declare a NoEnvironment fixture next to
    # a Creatio one (EmailTemplateToolE2ETests.cs), and a file-wide verdict would skip TeamCity for
    # both. The verdict is read from a comment-free copy of the file. A fixture's class attributes run from just after the column-0 closing brace that ends the
    # previous top-level type to its declaration; its body runs from the declaration to the same point
    # before the next declaration, so the next fixture's attributes are not included.
    $code = Remove-Comments $text
    $declarations = @([regex]::Matches($code, $fixtureDeclaration))
    $segmentStarts = @(foreach ($declaration in $declarations) {
        $closingBraces = @([regex]::Matches($code.Substring(0, $declaration.Index), '(?m)^\}'))
        if ($closingBraces.Count -eq 0) { 0 } else { $closingBraces[-1].Index + 1 }
    })
    $verdicts = @{}
    $survives = @{}
    for ($i = 0; $i -lt $declarations.Count; $i++) {
        $segmentEnd = if ($i + 1 -lt $declarations.Count) { $segmentStarts[$i + 1] } else { $code.Length }
        $classAttributes = $code.Substring($segmentStarts[$i], $declarations[$i].Index - $segmentStarts[$i])
        $body = $code.Substring($declarations[$i].Index, $segmentEnd - $declarations[$i].Index)
        $name = $declarations[$i].Groups[1].Value
        # A partial class declared twice in one file is NoEnvironment-only only if every part is.
        $verdicts[$name] = (Test-NoEnvironmentOnly $classAttributes $body) -and ($verdicts[$name] -ne $false)
        # ...and it runs on TeamCity if any part does.
        $survives[$name] = (Test-SurvivesTeamCityFilter $classAttributes $body $teamCityExcluded) -or ($survives[$name] -eq $true)
    }
    foreach ($class in $classes) {
        $fixtureSources[$class] = $text
        $fixtureNoEnvironmentOnly[$class] = [bool]$verdicts[$class]
        $fixtureSurvivesTeamCity[$class] = [bool]$survives[$class]
    }
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

# True when the fixture source names one of the tool classes declared in the changed file, or
# contains one of its [McpServerTool(Name = ...)] literals - either is enough to select it.
function Test-FixtureNamesDeclaration([string] $Source, $Declarations) {
    foreach ($class in $Declarations.Classes) {
        if ($Source -cmatch "\b$([regex]::Escape($class))\b") { return $true }
    }
    foreach ($literal in $Declarations.Literals) {
        if ($Source.Contains('"' + $literal + '"')) { return $true }
    }
    return $false
}

$toolFixtureCache = @{}
$entryPointFixtureCache = @{}
# An MCP resource or prompt type is an entry point exactly as a tool is: a session calls it
# directly, so a fixture reaches it without any type reference the graph could follow. Rooting the
# closure only in toolSourceRoot left all of Prompts/** and Resources/** with no root at all, and
# the graph then reported them unreachable - the same verdict it would give a file that genuinely
# has no fixture, so the pin could not tell the two apart. MultiSourceKnowledgeResource is the
# proof they differ: KnowledgeGuidanceNuGetE2ETests and KnowledgeGuidanceGitHubReleaseE2ETests call
# ReadResourceAsync on its own URI templates.
function Get-McpEntryPointDeclarations([string] $FilePath) {
    $text = Read-Text $FilePath
    if (-not ($text.Contains('[McpServerResourceType') -or $text.Contains('[McpServerPromptType'))) {
        return @{ Classes = @(); Literals = @(); IsEntryPoint = $false }
    }
    $classes = @([regex]::Matches($text, '(?m)^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected|static|sealed|abstract|partial)[\w\s]*\b(?:class|record)\s+([A-Za-z_]\w*)\b') | ForEach-Object { $_.Groups[1].Value })
    $constants = @{}
    foreach ($m in [regex]::Matches($text, 'const\s+string\s+(\w+)\s*=\s*"([^"]+)"')) {
        if (-not $constants.ContainsKey($m.Groups[1].Value)) { $constants[$m.Groups[1].Value] = @() }
        $constants[$m.Groups[1].Value] += $m.Groups[2].Value
    }
    # Both the routed URI template and the advertised name are things a fixture spells out, and a
    # fixture that does either is exercising this file.
    $literals = @()
    foreach ($m in [regex]::Matches($text, '(?:UriTemplate|Name)\s*=\s*("([^"]+)"|[\w.]+)')) {
        if ($m.Groups[2].Success) { $literals += $m.Groups[2].Value; continue }
        $identifier = ($m.Groups[1].Value -split '\.')[-1]
        if ($constants.ContainsKey($identifier)) { $literals += $constants[$identifier] }
    }
    return @{
        Classes = @($classes | Select-Object -Unique)
        Literals = @($literals | Select-Object -Unique)
        IsEntryPoint = $true
    }
}

# True when the file declares an MCP resource or prompt type at all. Get-Graph already read every
# file under the product source root into memory and precomputed the set, so this is a lookup. The
# disk path below is only reached for a file the graph does not hold - one outside that root, or a
# call made before the graph exists.
function Test-McpEntryPointFile([string] $FileRelative) {
    if (-not $FileRelative.EndsWith('.cs')) { return $false }
    if (($null -ne $script:graph) -and $script:graph.Texts.ContainsKey($FileRelative)) {
        return $script:graph.EntryPointFiles.Contains($FileRelative)
    }
    $path = Join-Path $root $FileRelative
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    return (Get-McpEntryPointDeclarations $path).IsEntryPoint
}

# Same shape as Select-FixturesForTool: the fixture named after the file, plus any fixture naming
# one of its declared classes or spelling one of its URI templates / advertised names.
function Select-FixturesForEntryPoint([string] $FileRelative) {
    if ($entryPointFixtureCache.ContainsKey($FileRelative)) { return $entryPointFixtureCache[$FileRelative] }
    $path = Join-Path $root $FileRelative
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($FileRelative)
    foreach ($name in $fixtureSources.Keys) {
        if ($name -cmatch "^$([regex]::Escape($stem))\w*E2ETests$") { [void]$selected.Add($name) }
    }
    if (Test-Path -LiteralPath $path) {
        $decl = Get-McpEntryPointDeclarations $path
        foreach ($name in $fixtureSources.Keys) {
            if (Test-FixtureNamesDeclaration $fixtureSources[$name] $decl) { [void]$selected.Add($name) }
        }
    }
    $result = @($selected)
    $entryPointFixtureCache[$FileRelative] = $result
    return $result
}

function Select-FixturesForTool([string] $ToolFileRelative) {
    if ($toolFixtureCache.ContainsKey($ToolFileRelative)) { return $toolFixtureCache[$ToolFileRelative] }
    $toolFile = Join-Path $root $ToolFileRelative
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($ToolFileRelative)
    foreach ($name in $fixtureSources.Keys) {
        if ($name -cmatch "^$([regex]::Escape($stem))\w*E2ETests$") { [void]$selected.Add($name) }
    }
    if (Test-Path -LiteralPath $toolFile) {
        $decl = Get-ToolDeclarations $toolFile
        foreach ($name in $fixtureSources.Keys) {
            if (Test-FixtureNamesDeclaration $fixtureSources[$name] $decl) { [void]$selected.Add($name) }
        }
    }
    # A deleted tool file still selects fixtures by name (they will fail to compile, which is the point).
    $result = @($selected)
    $toolFixtureCache[$ToolFileRelative] = $result
    return $result
}

# --- the product reference graph ------------------------------------------------------------------
# Built once, lazily. Nodes are repository-relative paths of the .cs files under productSourceRoot.
# An edge B -> A ("A consumes B") exists when A's identifier set contains a type name declared in B,
# or when A consumes a type that B declares a base type of. Name matching over-approximates: an edge
# may exist where the compiler sees none, which widens the selection and never narrows it.
function Remove-NonCode([string] $Text) {
    # Length-preserving: every character of a comment or literal becomes a space, except the line
    # breaks, so offsets, line numbers and indentation are unchanged.
    return $script:nonCode.Replace($Text, { param($match) $script:notLineBreak.Replace($match.Value, ' ') })
}
$notLineBreak = [regex] '[^\r\n]'

# References are read with comments removed but string literals kept. A comment that names a type -
# `/// <c>PageBaselineGuard</c>`, `// deliberately broader than McpHttpServerCommand's` - is not a
# dependency any code path follows, and in this tree such comments were what chained unrelated
# types into one component: McpToolExecutionLock's summary named PageBaselineGuard, so every change
# under page update reached every tool. A literal is kept because a type name in a string can be a
# real runtime reference (reflection, a type-name lookup), and dropping one would narrow a selection.
# Length-preserving like Remove-NonCode, so the same declaration offsets cut it into type bodies.
function Remove-CommentsOnly([string] $Text) {
    return $script:nonCode.Replace($Text, {
        param($match)
        if ($match.Value.StartsWith('/')) { return $script:notLineBreak.Replace($match.Value, ' ') }
        return $match.Value
    })
}

$graph = $null
function Get-ProductFileTexts() {
    $texts = @{}
    $productRoot = Join-Path $root $manifest.productSourceRoot
    foreach ($file in Get-ChildItem -LiteralPath $productRoot -Filter '*.cs' -File -Recurse) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
        if ($relative -cmatch '/(bin|obj)/') { continue }
        $texts[$relative] = Read-Text $file.FullName
    }
    return $texts
}

# Which files declare an MCP resource or prompt is asked once per (closure type, owner) pair by
# Select-FixturesForProductFile, and the widest closure in this tree holds 3144 types. Answering
# it by reading the file back from disk made -Inventory an order of magnitude slower; the text is
# already here, so the answer is a set built once - the shape RegistrationTypes and UnboundedTypes
# are built with.
function Get-EntryPointFileSet($Texts) {
    $entryPointFileSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($relative in $Texts.Keys) {
        if ($Texts[$relative].Contains('[McpServerResourceType') -or $Texts[$relative].Contains('[McpServerPromptType')) {
            [void]$entryPointFileSet.Add($relative)
        }
    }
    return ,$entryPointFileSet
}

# An alias of a namespace this repository declares is a root too; one aliasing System.* adds
# nothing, because no type behind it is ours.
function Get-NamespaceRoots($Texts, $NamespaceDeclaration, $NamespaceAlias) {
    $namespaceRoots = New-Object System.Collections.Generic.HashSet[string]
    foreach ($relative in $Texts.Keys) {
        # Every segment, not only the first: inside `namespace Clio.Command.McpServer` the name
        # `Common.McpWorker.IWorkerTempResidueSweeper` resolves against the enclosing Clio, so a chain
        # may start at any segment of a namespace this repository declares.
        foreach ($m in [regex]::Matches($Texts[$relative], $NamespaceDeclaration)) {
            foreach ($segment in $m.Groups[1].Value.Split('.')) { [void]$namespaceRoots.Add($segment) }
        }
    }
    foreach ($relative in $Texts.Keys) {
        foreach ($m in [regex]::Matches($Texts[$relative], $NamespaceAlias)) {
            if ($namespaceRoots.Contains($m.Groups[2].Value)) { [void]$namespaceRoots.Add($m.Groups[1].Value) }
        }
    }
    return ,$namespaceRoots
}

# A nested type indented less than the type that contains it would become the file's only "top
# level" and swallow the outer type's body. Two files in this tree are formatted that way; rather
# than guess, attribute the whole file to every type it declares.
function Add-WholeFileAttribution([string] $Relative, $Views, $DeclarationMatches, $TypeFiles) {
    $declaredAll = @($DeclarationMatches | ForEach-Object { $_.Groups[3].Value } | Select-Object -Unique)
    foreach ($name in $declaredAll) {
        foreach ($view in $Views) {
            if (-not $view.Map.ContainsKey($name)) { $view.Map[$name] = New-Object System.Text.StringBuilder }
            [void]$view.Map[$name].Append($view.Text)
        }
        if (-not $TypeFiles.ContainsKey($name)) { $TypeFiles[$name] = New-Object System.Collections.Generic.HashSet[string] }
        [void]$TypeFiles[$name].Add($Relative)
    }
    return ,$declaredAll
}

# The base list of a declaration. The declaration pattern reads it only when the colon follows the
# name; a primary constructor puts a parameter list in between - `class Foo(IBar bar) : IFoo`, or
# the same list spread over several lines - and 690 declarations under clio/ are written that way.
# Without this every one of them lost its implementation -> interface edge, and a service reached
# only through its interface looked unreachable. The parameter list is skipped by matching
# parentheses on the blanked text, so a parenthesis inside a literal or a comment cannot end it.
function Get-BaseListText([string] $Scan, $Match) {
    # The declaration pattern captures a base list only to the end of its line; the list itself runs
    # to the body's opening brace, so read it from the blanked text up to there either way.
    if ($Match.Groups[4].Success) {
        $start = $Match.Groups[4].Index
        $end = $Scan.IndexOfAny([char[]]'{;', $start)
        if ($end -lt 0) { $end = $Scan.Length }
        return $Scan.Substring($start, $end - $start)
    }
    $i = $Match.Index + $Match.Length
    while ($i -lt $Scan.Length -and [char]::IsWhiteSpace($Scan[$i])) { $i++ }
    if ($i -ge $Scan.Length -or $Scan[$i] -ne '(') { return $null }
    $depth = 0
    do {
        if ($Scan[$i] -eq '(') { $depth++ } elseif ($Scan[$i] -eq ')') { $depth-- }
        $i++
    } while ($i -lt $Scan.Length -and $depth -gt 0)
    while ($i -lt $Scan.Length -and [char]::IsWhiteSpace($Scan[$i])) { $i++ }
    if ($i -ge $Scan.Length -or $Scan[$i] -ne ':') { return $null }
    $end = $Scan.IndexOfAny([char[]]'{;', $i)
    if ($end -lt 0) { $end = $Scan.Length }
    return $Scan.Substring($i + 1, $end - $i - 1)
}

function Add-BaseListEntries([string] $Scan, $Tops, $InterfaceTypes, $BaseList) {
    foreach ($top in $Tops) {
        if ($top.Groups[2].Value -eq 'interface') { [void]$InterfaceTypes.Add($top.Groups[3].Value) }
        $baseListText = Get-BaseListText $Scan $top
        if ($null -eq $baseListText) { continue }
        $baseNames = @([regex]::Matches($baseListText, '(?<![\w.])([A-Za-z_]\w*)') | ForEach-Object { $_.Groups[1].Value })
        if (-not $BaseList.ContainsKey($top.Groups[3].Value)) { $BaseList[$top.Groups[3].Value] = New-Object System.Collections.Generic.HashSet[string] }
        foreach ($baseName in $baseNames) { [void]$BaseList[$top.Groups[3].Value].Add($baseName) }
    }
}

# Usings, the namespace and file-level attributes precede every type and can carry a reference
# that belongs to all of them. Every view of the file (raw, blanked, comment-free) is the same
# length, so one set of offsets cuts all of them and no type body is lexed again later.
function Add-TypeBodySpans([string] $Relative, $Views, $Tops, $TypeFiles) {
    $declared = New-Object System.Collections.Generic.List[string]
    $length = $Views[0].Text.Length
    for ($i = 0; $i -lt $Tops.Count; $i++) {
        $name = $Tops[$i].Groups[3].Value
        $from = $Tops[$i].Index
        $to = if ($i + 1 -lt $Tops.Count) { $Tops[$i + 1].Index } else { $length }
        foreach ($view in $Views) {
            if (-not $view.Map.ContainsKey($name)) { $view.Map[$name] = New-Object System.Text.StringBuilder }
            [void]$view.Map[$name].Append($view.Text, 0, $Tops[0].Index).Append($view.Text, $from, $to - $from)
        }
        if (-not $TypeFiles.ContainsKey($name)) { $TypeFiles[$name] = New-Object System.Collections.Generic.HashSet[string] }
        [void]$TypeFiles[$name].Add($Relative)
        if (-not $declared.Contains($name)) { $declared.Add($name) }
    }
    return ,$declared
}

function Add-TopLevelDeclarations([string] $Relative, $Views, [string] $Scan, $DeclarationMatches, [int] $TopIndent, $InterfaceTypes, $BaseList, $TypeFiles) {
    $tops = @($DeclarationMatches | Where-Object { $_.Groups[1].Value.Length -eq $TopIndent })
    Add-BaseListEntries $Scan $tops $InterfaceTypes $BaseList
    $declared = Add-TypeBodySpans $Relative $Views $tops $TypeFiles
    return ,$declared
}

# Abstract declarations, recorded so Get-ToolTypes can leave them out. The match is taken on blanked
# text, so `abstract` in a comment or a literal does not count.
function Add-AbstractTypes($DeclarationMatches, $Declared, $AbstractTypes) {
    foreach ($m in $DeclarationMatches) {
        if ($Declared.Contains($m.Groups[3].Value) -and $m.Value -cmatch '\babstract\b') { [void]$AbstractTypes.Add($m.Groups[3].Value) }
    }
}

# The concrete MCP tool types: every non-abstract type whose own code carries [McpServerToolType] or
# an [McpServerTool] method. Most tools declare neither attribute on the class - they inherit
# [McpServerToolType] from BaseTool<T> - so the method attribute is the one that finds them. BaseTool
# itself is abstract and left out: it is the base every tool inherits, and stopping the closure there
# would hide every tool behind it. Get-ConsumerClosure stops at these types.
function Get-ToolTypes($TypeBodyScan, $AbstractTypes) {
    $toolTypes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($name in $TypeBodyScan.Keys) {
        if ($AbstractTypes.Contains($name)) { continue }
        if ($TypeBodyScan[$name].ToString() -cmatch '\[\s*McpServerTool(?:Type)?\s*[\](,]') { [void]$toolTypes.Add($name) }
    }
    return ,$toolTypes
}

function Add-DeclarationsForFile([string] $Relative, [string] $Text, $TypeDeclaration, $InterfaceTypes, $BaseList, $Bodies, $TypeFiles, $AbstractTypes) {
    # Scan for declarations on a copy with raw-string contents blanked out, so a code sample inside
    # a literal cannot be read as the file's next top-level type. Offsets are preserved.
    $scan = Remove-NonCode $Text
    $declarationMatches = @($TypeDeclaration.Matches($scan))
    if ($declarationMatches.Count -eq 0) { return ,@() }
    $code = Remove-CommentsOnly $Text
    # The views are cut by the same offsets; a lexer change that shifts one would mis-slice every
    # type body silently, so it fails here instead.
    if ($scan.Length -ne $Text.Length -or $code.Length -ne $Text.Length) { throw "Lexing changed the length of $Relative; the type-body views cannot share offsets." }
    $views = @(
        @{ Text = $Text; Map = $Bodies.Raw }
        @{ Text = $scan; Map = $Bodies.Scan }
        @{ Text = $code; Map = $Bodies.Code }
    )
    $topIndent = ($declarationMatches | ForEach-Object { $_.Groups[1].Value.Length } | Measure-Object -Minimum).Minimum
    if ($declarationMatches[0].Groups[1].Value.Length -ne $topIndent) {
        $declared = Add-WholeFileAttribution $Relative $views $declarationMatches $TypeFiles
    }
    else {
        $declared = Add-TopLevelDeclarations $Relative $views $scan $declarationMatches $topIndent $InterfaceTypes $BaseList $TypeFiles
    }
    Add-AbstractTypes $declarationMatches $declared $AbstractTypes
    return ,$declared
}

# Every map keyed by a type name is ORDINAL. A PowerShell `@{}` folds case, and C# does not: with
# `@{}` the local `command` in 2651 type bodies was an edge to the type `Command`, and a comment
# reading `(this command is MCP-callable` made three commands look like extensions of it. Those
# edges joined nearly every command to nearly every tool, so most product changes ran the full suite.
function New-OrdinalMap() { return [System.Collections.Hashtable]::new([System.StringComparer]::Ordinal) }

# The graph node is a TYPE, not a file. A file that declares a narrow helper next to a widely used
# one would otherwise merge their consumer sets and make the narrow type look as connected as the
# wide one - measured as the single largest source of over-approximation in this tree.
# A type owns the text from its declaration to the next top-level declaration, so nested types are
# part of their outer type and no character of the file is left unattributed.
function Build-TypeNodes($Texts, $TypeDeclaration) {
    $interfaceTypes = New-Object System.Collections.Generic.HashSet[string]
    $abstractTypes = New-Object System.Collections.Generic.HashSet[string]
    $baseList = New-OrdinalMap    # type name -> the names in its base list
    # type name -> the text that belongs to it (a partial type accumulates), in three views of the
    # same length: Raw, Scan (comments and literals blanked) and Code (comments blanked).
    $bodies = @{ Raw = New-OrdinalMap; Scan = New-OrdinalMap; Code = New-OrdinalMap }
    $typeFiles = New-OrdinalMap   # type name -> files declaring it
    $typesByFile = New-OrdinalMap # file -> type names declared at its top level
    foreach ($relative in $Texts.Keys) {
        $declared = Add-DeclarationsForFile $relative $Texts[$relative] $TypeDeclaration $interfaceTypes $baseList $bodies $typeFiles $abstractTypes
        $typesByFile[$relative] = $declared
    }
    return @{
        InterfaceTypes = $interfaceTypes; BaseList = $baseList; ToolTypes = (Get-ToolTypes $bodies.Scan $abstractTypes)
        TypeBody = $bodies.Raw; TypeBodyScan = $bodies.Scan; TypeBodyCode = $bodies.Code
        TypeFiles = $typeFiles; TypesByFile = $typesByFile
    }
}

function Get-BodyTokens([string] $Body, $NamespaceRoots, $Identifier, $Qualified) {
    $tokens = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in $Identifier.Matches($Body)) { [void]$tokens.Add($m.Groups[1].Value) }
    # `Clio.Common.Foo` and `global::Clio.Common.Foo`: the dot before Foo hides it from $Identifier,
    # and 1247 references in this tree are written that way. Only chains rooted in a namespace this
    # repository declares are followed, so `task.Result` is still member access, not a reference.
    foreach ($m in $Qualified.Matches($Body)) {
        if (-not $NamespaceRoots.Contains($m.Groups[1].Value)) { continue }
        foreach ($segment in $m.Groups[2].Value.Split('.')) { if ($segment) { [void]$tokens.Add($segment) } }
    }
    return ,$tokens
}

function Build-ConsumerGraph($TypeBody, $TypeBodyCode, $NamespaceRoots, $Identifier, $Qualified, $VerbDeclaration) {
    $verbsByType = New-OrdinalMap
    $consumers = New-OrdinalMap   # type name -> type names whose text names it
    foreach ($name in $TypeBody.Keys) {
        $body = $TypeBody[$name].ToString()
        $verbs = @([regex]::Matches($body, $VerbDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        if ($verbs.Count -gt 0) { $verbsByType[$name] = $verbs }
        $tokens = Get-BodyTokens $TypeBodyCode[$name].ToString() $NamespaceRoots $Identifier $Qualified
        foreach ($token in $tokens) {
            if ($token -eq $name -or -not $TypeBody.ContainsKey($token)) { continue }
            if (-not $consumers.ContainsKey($token)) { $consumers[$token] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$consumers[$token].Add($name)
        }
    }
    return @{ VerbsByType = $verbsByType; Consumers = $consumers }
}

# $From's recorded consumers become $To's too (self excluded). Used wherever one type stands in for
# another that a caller never names directly - an extension's extended type, an interface's
# implementation. When $From has no recorded consumers yet, nothing is copied and $To gets no entry
# either, so a still-empty edge does not masquerade as "this type is known to the graph".
function Copy-ConsumerEdgesIfSourceExists($Consumers, [string] $From, [string] $To) {
    if (-not $Consumers.ContainsKey($From)) { return }
    if (-not $Consumers.ContainsKey($To)) { $Consumers[$To] = New-Object System.Collections.Generic.HashSet[string] }
    foreach ($c in $Consumers[$From]) { if ($c -ne $To) { [void]$Consumers[$To].Add($c) } }
}

# Same copy, but $To (the registered implementation) gets an entry even when $From (the service) has
# no recorded consumers yet - services.AddSingleton<IFoo, Foo>() marks Foo as known to the graph
# purely by being registered, before anything is known to consume IFoo.
function Copy-ConsumerEdgesEnsuringTargetExists($Consumers, [string] $From, [string] $To) {
    if (-not $Consumers.ContainsKey($To)) { $Consumers[$To] = New-Object System.Collections.Generic.HashSet[string] }
    if (-not $Consumers.ContainsKey($From)) { return }
    foreach ($c in $Consumers[$From]) { if ($c -ne $To) { [void]$Consumers[$To].Add($c) } }
}

# Extension class -> extended type. `value.Normalize()` names neither the extension class nor
# its file, so the only route from a call site to the extension is the type it extends.
function Add-ExtensionConsumerEdges($TypeBody, $TypeBodyScan, $Consumers, $ExtensionParameter) {
    $unboundedTypes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($name in @($TypeBody.Keys)) {
        # Blanked text: `(this command is MCP-callable` in a comment is not an extension method.
        foreach ($m in $ExtensionParameter.Matches($TypeBodyScan[$name].ToString())) {
            $extended = $m.Groups[1].Value
            if ($extended -eq $name) { continue }
            if (-not $TypeBody.ContainsKey($extended)) {
                # `this string`, `this IEnumerable<T>`, `this Exception`: the receiver is not ours, so
                # the callers cannot be enumerated. Half the extension methods in this tree are of
                # that shape, and guessing an empty consumer set for them would skip the build.
                [void]$unboundedTypes.Add($name)
                continue
            }
            Copy-ConsumerEdgesIfSourceExists $Consumers $extended $name
        }
    }
    return ,$unboundedTypes
}

# Implementation -> interface. A consumer injects IFoo and never spells Foo out, so without this
# edge every service behind an interface looks unreachable. Restricted to base types declared as
# `interface`: a base CLASS in this tree (Command, BaseTool) is a template-method host whose
# hundreds of subclasses are not interchangeable, and following it merges the whole tree.
function Add-InterfaceConsumerEdges($BaseList, $InterfaceTypes, $Consumers) {
    foreach ($name in @($BaseList.Keys)) {
        foreach ($baseName in $BaseList[$name]) {
            if (-not $InterfaceTypes.Contains($baseName)) { continue }
            Copy-ConsumerEdgesIfSourceExists $Consumers $baseName $name
        }
    }
}

# services.AddSingleton<IFoo, Foo>() - the one edge name matching cannot see, because a consumer
# of IFoo never spells Foo out. Taken from the registration files only, and only as an exact pair.
function Add-RegistrationPairEdges([string] $RegistrationText, $TypeBody, $Consumers, $RegistrationPair) {
    foreach ($m in $RegistrationPair.Matches($RegistrationText)) {
        $service = $m.Groups[1].Value
        $implementation = $m.Groups[2].Value
        if (-not $TypeBody.ContainsKey($implementation)) { continue }
        if (-not $TypeBody.ContainsKey($service)) { Add-ExternalServiceImplementation $implementation $service; continue }
        Copy-ConsumerEdgesEnsuringTargetExists $Consumers $service $implementation
    }
}

# services.AddSingleton<ILogger>(ConsoleLogger.Instance) and the lambda form: one generic argument
# and an instance or factory that names the implementation somewhere on the same line. Only the
# opening of the call is matched by $Match; the argument itself is read by matching parentheses,
# because a lambda body holds semicolons of its own and a literal can hold anything.
function Resolve-FactoryImplementationArgument([string] $RegistrationText, $Match) {
    $start = $Match.Index + $Match.Length
    if ($start -ge $RegistrationText.Length -or $RegistrationText[$start] -eq ')') { return @{ Argument = $null; Truncated = $false } }
    $depth = 1
    $cursor = $start
    $limit = [Math]::Min($RegistrationText.Length, $start + 4000)
    while ($cursor -lt $limit -and $depth -gt 0) {
        $character = $RegistrationText[$cursor]
        if ($character -eq '(') { $depth++ } elseif ($character -eq ')') { $depth-- }
        $cursor++
    }
    return @{ Argument = $RegistrationText.Substring($start, $cursor - $start); Truncated = ($depth -gt 0) }
}

# The factory form carries the implementation in the argument rather than in a second generic
# parameter, so every known type named on that line is linked to the service.
function Add-RegistrationFactoryEdges([string] $RegistrationText, [string] $RegistrationFile, $TypeBody, $Consumers, $RegistrationFactory) {
    foreach ($m in $RegistrationFactory.Matches($RegistrationText)) {
        $service = $m.Groups[1].Value
        $external = -not $TypeBody.ContainsKey($service)
        if (-not $external -and -not $Consumers.ContainsKey($service)) { continue }
        $scan = Resolve-FactoryImplementationArgument $RegistrationText $m
        if ($null -eq $scan.Argument) { continue }
        # The cap is reached only by a factory argument longer than 4000 characters. Stopping
        # there would silently drop the implementation name the argument carries, and the graph
        # would then report a file reachable only through that implementation as unreachable -
        # a skipped build, the direction that loses a test. There is no name to record because
        # the name is precisely what was not read, so the whole graph degrades to a full run.
        if ($scan.Truncated) { $script:factoryScanTruncated.Add("$service (registration in $RegistrationFile)") }
        # For a service this repository does not declare, only the types the factory constructs are
        # its implementation; `sp.GetRequiredService<IApplicationClientFactory>()` in the same lambda
        # is a dependency of the factory, and marking it would make every change to it a full run.
        $namePattern = if ($external) { '\bnew\s+(?:[\w.]*\.)?([A-Za-z_]\w*)' } else { '(?<![\w.])([A-Za-z_]\w*)' }
        foreach ($t in [regex]::Matches($scan.Argument, $namePattern)) {
            $implementation = $t.Groups[1].Value
            if ($implementation -eq $service -or -not $TypeBody.ContainsKey($implementation)) { continue }
            if ($external) { Add-ExternalServiceImplementation $implementation $service; continue }
            Copy-ConsumerEdgesEnsuringTargetExists $Consumers $service $implementation
        }
    }
}

function Add-RegistrationConsumerEdges($Registration, $Texts, $TypeBody, $Consumers, $RegistrationPair, $RegistrationFactory) {
    foreach ($registrationFile in $Registration) {
        if (-not $Texts.ContainsKey($registrationFile)) { continue }
        # A `;` inside a literal would end the statement early and hide the implementation.
        $registrationText = Remove-NonCode $Texts[$registrationFile]
        Add-RegistrationPairEdges $registrationText $TypeBody $Consumers $RegistrationPair
        Add-RegistrationFactoryEdges $registrationText $registrationFile $TypeBody $Consumers $RegistrationFactory
    }
}

# One implementation can be registered for several external services; each one's consumers count.
function Add-ExternalServiceImplementation([string] $Implementation, [string] $Service) {
    if (-not $script:externalServiceImplementations.ContainsKey($Implementation)) {
        $script:externalServiceImplementations[$Implementation] = New-Object System.Collections.Generic.HashSet[string]
    }
    [void]$script:externalServiceImplementations[$Implementation].Add($Service)
}

# An implementation registered for a service this repository does not declare gets, as its
# consumers, every type whose code names that service - the edge Add-InterfaceConsumerEdges draws for
# an interface declared here, which the token scan cannot draw because an external name is not a
# graph node. Returns the implementations no type consumes that way; their blast radius is unknown.
function Add-ExternalServiceConsumerEdges($ExternalServiceImplementations, $TypeBodyCode, $Consumers, $RegistrationTypes) {
    $unresolved = New-OrdinalMap
    foreach ($entry in @($ExternalServiceImplementations.GetEnumerator())) {
        $implementation = $entry.Key
        if (-not $Consumers.ContainsKey($implementation)) { $Consumers[$implementation] = New-Object System.Collections.Generic.HashSet[string] }
        foreach ($service in $entry.Value) {
            $mention = [regex] "(?<![\w.])$([regex]::Escape($service))\b"
            $found = $false
            foreach ($name in $TypeBodyCode.Keys) {
                # The composition root names every service it registers; it is not a consumer.
                if ($name -ceq $implementation -or $RegistrationTypes.Contains($name)) { continue }
                if (-not $mention.IsMatch($TypeBodyCode[$name].ToString())) { continue }
                [void]$Consumers[$implementation].Add($name)
                $found = $true
            }
            # Any one service nobody names leaves part of the implementation's reach unknown.
            if (-not $found) { $unresolved[$implementation] = $service }
        }
    }
    return $unresolved
}

# A type declared only in a registration file is never traversed through: every type is named
# there, so following it would make every change reach every tool.
function Build-RegistrationTypes($Registration, $TypesByFile, $TypeFiles) {
    $registrationTypes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($registrationFile in $Registration) {
        if (-not $TypesByFile.ContainsKey($registrationFile)) { continue }
        foreach ($name in $TypesByFile[$registrationFile]) {
            $owners = @($TypeFiles[$name] | Where-Object { $Registration -notcontains $_ })
            if ($owners.Count -eq 0) { [void]$registrationTypes.Add($name) }
        }
    }
    return ,$registrationTypes
}

function Get-Graph() {
    if ($null -ne $script:graph) { return $script:graph }
    # A type declaration at the start of a line. Attributes are part of the match, so [Verb("x")]
    # belongs to the body of the options type it decorates.
    # The base list may start on the following line - `class Foo\n    : IFoo` - so one line break is
    # allowed before the colon. Any more would risk swallowing an unrelated `:` further down.
    $typeDeclaration = [regex] '(?m)^([ \t]*)(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected|static|sealed|abstract|partial|readonly)[\w \t]*\b(class|record|interface|struct|enum)\s+([A-Za-z_]\w*)\b(?:<[^>{\r\n]*>)?[ \t]*(?:\r?\n[ \t]*)?(?::[ \t]*([^{\r\n]+))?'
    $verbDeclaration = '\[\s*Verb\(\s*"([^"]+)"'
    # An identifier that is not preceded by a dot: `SysSettingsManager` counts, `task.Result` and
    # `options.Schema` do not. Member access through a common property name is what made the graph
    # a single blob when every token counted.
    $identifier = [regex] '(?<![\w.])([A-Za-z_]\w*)'
    $qualified = [regex] '(?<![\w.])(?:global::)?([A-Za-z_]\w*)((?:\.[A-Za-z_]\w*)+)'
    $namespaceDeclaration = '(?m)^\s*namespace\s+([A-Za-z_][\w.]*)'
    # `using Contracts = Clio.Common;` makes Contracts.Foo a reference to Clio.Common.Foo.
    $namespaceAlias = '(?m)^\s*using\s+([A-Za-z_]\w*)\s*=\s*(?:global::)?([A-Za-z_]\w*)[\w.]*\s*;'
    # `public static int Normalize(this Service value)`: the extension type is never named by the
    # caller, so the call site has to be routed through the type it extends.
    $extensionParameter = [regex] '\(\s*this\s+(?:ref\s+|in\s+|scoped\s+)*([A-Za-z_]\w*)'
    # Everything that is not code: raw strings (the closing run matches the opening one), verbatim
    # and ordinary strings, char literals, line and block comments. Structure - declarations, base
    # lists, the parentheses of a registration call - is parsed on text where each of these has been
    # replaced by spaces, so a bracket, a quote or a declaration written inside one cannot be read as
    # syntax. Offsets and line breaks are preserved, so indentation and anchors still work.
    # References are read from the text with comments removed and literals kept (Remove-CommentsOnly).
    # Order matters: the longest and most specific form first. A raw string is matched with any
    # number of leading dollars, because `$"""` and `$$"""` are interpolated raw strings and 44
    # files here use them. An interpolated string is listed
    # before the ordinary one because a quote inside a hole - `$"{Call(\")\")}"`, which 105 files
    # here contain, BindingsModule.cs among them - would otherwise end the literal early and leave
    # a stray bracket in the structural text.
    $script:nonCode = [regex] @'
\$*("{3,})[\s\S]*?\1|(?:\$@|@\$)"(?:\{(?:[^{}"]|"(?:""|[^"])*")*\}|\{\{|\}\}|""|[^"])*"|\$@?"(?:\{(?:[^{}"]|"(?:\\.|[^"\\\r\n])*")*\}|\{\{|\}\}|""|\\.|[^"\\\r\n])*"|@"(?:[^"]|"")*"|"(?:\\.|[^"\\\r\n])*"|'(?:\\.|[^'\\\r\n])*'|//[^\r\n]*|/\*[\s\S]*?\*/
'@
    # services.AddSingleton<IFoo, Foo>() - the one edge name matching cannot see, because a consumer
    # of IFoo never spells Foo out. Taken from the registration files only, and only as an exact pair.
    $registrationPair = [regex] 'Add(?:Singleton|Scoped|Transient|KeyedSingleton|KeyedScoped|KeyedTransient|HttpClient)<\s*(?:[\w.]*\.)?(\w+)\s*,\s*(?:[\w.]*\.)?(\w+)\s*>'
    # services.AddSingleton<ILogger>(ConsoleLogger.Instance) and the lambda form: one generic argument
    # and an instance or factory that names the implementation somewhere on the same line.
    # Only the opening of the call: the argument is then read by matching parentheses, because a
    # lambda body holds semicolons of its own and a literal can hold anything.
    $registrationFactory = [regex] 'Add(?:Singleton|Scoped|Transient|KeyedSingleton|KeyedScoped|KeyedTransient|HttpClient)<\s*(?:[\w.]*\.)?(\w+)\s*>\s*\('

    $texts = Get-ProductFileTexts
    $entryPointFileSet = Get-EntryPointFileSet $texts

    # The graph node is a TYPE, not a file. A file that declares a narrow helper next to a widely used
    # one would otherwise merge their consumer sets and make the narrow type look as connected as the
    # wide one - measured as the single largest source of over-approximation in this tree.
    $namespaceRoots = Get-NamespaceRoots $texts $namespaceDeclaration $namespaceAlias
    $nodes = Build-TypeNodes $texts $typeDeclaration
    $consumerGraph = Build-ConsumerGraph $nodes.TypeBody $nodes.TypeBodyCode $namespaceRoots $identifier $qualified $verbDeclaration

    $script:factoryScanTruncated = New-Object System.Collections.Generic.List[string]
    # implementation -> service, for a service type this repository does not declare
    # (services.AddTransient<IDataProvider>(sp => new ClassifyingDataProvider(...))). Its consumers
    # inject the external interface and never name the implementation; Add-ExternalServiceConsumerEdges
    # links it to the types that name the service instead.
    $script:externalServiceImplementations = New-OrdinalMap
    $unboundedTypes = Add-ExtensionConsumerEdges $nodes.TypeBody $nodes.TypeBodyScan $consumerGraph.Consumers $extensionParameter
    Add-InterfaceConsumerEdges $nodes.BaseList $nodes.InterfaceTypes $consumerGraph.Consumers | Out-Null

    $registration = @($manifest.registrationFiles)
    Add-RegistrationConsumerEdges $registration $texts $nodes.TypeBody $consumerGraph.Consumers $registrationPair $registrationFactory | Out-Null
    $registrationTypes = Build-RegistrationTypes $registration $nodes.TypesByFile $nodes.TypeFiles
    $unresolvedServiceImplementations = Add-ExternalServiceConsumerEdges $script:externalServiceImplementations $nodes.TypeBodyCode $consumerGraph.Consumers $registrationTypes

    # An invariant the guard checks: after blanking, no quote, comment marker or char literal may
    # remain anywhere. A lexer that misses a literal form leaves one behind, and that is exactly the
    # class of bug that silently narrows the structural parse.
    # Only -Inventory reads it, and computing it re-runs Remove-NonCode over every file under
    # clio/ - roughly doubling the lexing a plain selection does (measured: graph ~10 s, inventory
    # ~30 s). A selection on a pull request pays nothing for a list it never prints.
    $residue = New-Object System.Collections.Generic.List[string]
    if ($Inventory) {
        foreach ($relative in ($texts.Keys | Sort-Object)) {
            $blanked = Remove-NonCode $texts[$relative]
            if ($blanked.Contains('"') -or $blanked.Contains('//') -or $blanked.Contains('/*')) { $residue.Add($relative) }
        }
    }

    $script:graph = @{
        LexerResidue = $residue
        FactoryScanTruncated = $script:factoryScanTruncated
        Texts = $texts; TypeFiles = $nodes.TypeFiles; TypesByFile = $nodes.TypesByFile
        VerbsByType = $consumerGraph.VerbsByType; Consumers = $consumerGraph.Consumers; RegistrationTypes = $registrationTypes
        UnboundedTypes = $unboundedTypes; EntryPointFiles = $entryPointFileSet; ToolTypes = $nodes.ToolTypes
        UnresolvedServiceImplementations = $unresolvedServiceImplementations
        ToolMemberCallers = (Get-ToolMemberCallers $nodes.ToolTypes $consumerGraph.Consumers $nodes.TypeBodyCode)
        Registration = $registration
    }
    return $script:graph
}

# consumer|tool pairs where a non-tool type calls a member of a tool type - `ComponentInfoTool.
# CreateDetailResponse(...)` from ComponentInfoCommand, `ODataReadTool.DescribeMarkupError(...)` from
# ODataFileContract. That is an execution path through the tool's code, unlike a registry's
# `typeof(PageSyncTool)` or a prompt's `PageSyncTool.ToolName`, so Get-ConsumerClosure follows it.
function Get-ToolMemberCallers($ToolTypes, $Consumers, $TypeBodyCode) {
    $callers = New-Object System.Collections.Generic.HashSet[string]
    foreach ($tool in $ToolTypes) {
        if (-not $Consumers.ContainsKey($tool)) { continue }
        $call = [regex] "(?<![\w.])$([regex]::Escape($tool))\s*\.\s*\w+\s*[(<]"
        foreach ($consumer in $Consumers[$tool]) {
            if ($ToolTypes.Contains($consumer)) { continue }
            if ($call.IsMatch($TypeBodyCode[$consumer].ToString())) { [void]$callers.Add("$consumer|$tool") }
        }
    }
    return ,$callers
}

$closureCache = @{}
function Get-ConsumerClosure([string] $FileRelative) {
    if ($script:closureCache.ContainsKey($FileRelative)) { return $script:closureCache[$FileRelative] }
    # Every type that transitively names a type declared in this file, plus those types themselves.
    # From a concrete MCP tool type the walk continues only to other tool types and to non-tool types
    # that call a member of it (Get-ToolMemberCallers). What reaches a tool is observed through that
    # tool's fixtures; the other types naming a tool are registries and prompts (ToolContractCatalog,
    # McpCoreToolProfile, *Prompt) that name dozens of tools each, and walking through them made one
    # tool's dependency reach nearly all of them. A tool that uses another tool (PageUpdateTool ->
    # PageSyncTool) is a real execution path, so that edge is still followed. This is the one rule
    # that can narrow a selection: code that reaches a tool only through a registry - dispatch by
    # name, as clio-run does - is covered by the fixtures that name that tool.
    $g = Get-Graph
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $queue = New-Object System.Collections.Generic.Queue[string]
    foreach ($name in $g.TypesByFile[$FileRelative]) { if ($seen.Add($name)) { $queue.Enqueue($name) } }
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $g.Consumers.ContainsKey($current)) { continue }
        $fromTool = $g.ToolTypes.Contains($current)
        foreach ($consumer in $g.Consumers[$current]) {
            if ($g.RegistrationTypes.Contains($consumer)) { continue }
            if ($fromTool -and -not $g.ToolTypes.Contains($consumer) -and -not $g.ToolMemberCallers.Contains("$consumer|$current")) { continue }
            if ($seen.Add($consumer)) { $queue.Enqueue($consumer) }
        }
    }
    $result = @($seen)
    $script:closureCache[$FileRelative] = $result
    return $result
}

$toolFilesByName = $null
function Get-ToolFilesByName() {
    # Every [McpServerTool(Name = ...)] literal in the tree, mapped to the file that declares it.
    # 132 CLI verbs are also MCP tool names, because the tool IS the command's MCP surface; without
    # this index a command change looks uncovered while its tool has fixtures.
    if ($null -ne $script:toolFilesByName) { return $script:toolFilesByName }
    $script:toolFilesByName = @{}
    $toolRootFull = Join-Path $root $manifest.toolSourceRoot
    foreach ($toolFile in Get-ChildItem -LiteralPath $toolRootFull -Filter '*.cs' -File -Recurse) {
        $relative = $toolFile.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
        foreach ($literal in (Get-ToolDeclarations $toolFile.FullName).Literals) {
            if (-not $script:toolFilesByName.ContainsKey($literal)) { $script:toolFilesByName[$literal] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$script:toolFilesByName[$literal].Add($relative)
        }
    }
    return $script:toolFilesByName
}

$verbFixtureCache = @{}
function Select-FixturesForVerb([string] $Verb) {
    if ($script:verbFixtureCache.ContainsKey($Verb)) { return $script:verbFixtureCache[$Verb] }
    # A command is reached two ways: an MCP tool published under the same name (the usual case), and
    # the CLI harness spelling the verb out in a fixture.
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $byName = Get-ToolFilesByName
    if ($byName.ContainsKey($Verb)) {
        foreach ($toolFile in $byName[$Verb]) {
            foreach ($n in @(Select-FixturesForTool $toolFile)) { [void]$selected.Add($n) }
        }
    }
    $needle = '"' + $Verb + '"'
    foreach ($name in $fixtureSources.Keys) {
        if ($fixtureSources[$name].Contains($needle)) { [void]$selected.Add($name) }
    }
    $result = @($selected)
    $script:verbFixtureCache[$Verb] = $result
    return $result
}

# The six early-exit checks that force a full run before the closure is even walked: a truncated
# factory scan, a file outside the tree, reflection, an unenumerable extension receiver, an
# implementation registered for an external service that no type here names, or a file declaring
# no type at all. Split out of Select-FixturesForProductFile so that function's own branching
# is just the closure walk and the fixture-selection verdict.
function Test-ProductFileFullRunReason([string] $FileRelative, $Graph, [ref] $Reason) {
    if (@($Graph.FactoryScanTruncated).Count -gt 0) {
        $Reason.Value = "full run (a factory registration argument exceeded the scan cap, so the graph is missing at least one implementation edge: $(@($Graph.FactoryScanTruncated) -join '; '))"
        return $true
    }
    if (-not $Graph.TypesByFile.ContainsKey($FileRelative)) { $Reason.Value = 'full run (file not in the tree)'; return $true }
    # AGENTS.md: [ResolvedDynamically] marks a service resolved by reflection or from another
    # assembly. That is exactly the edge an identifier scan cannot see, so the graph is not allowed
    # to conclude anything about such a file.
    if ($Graph.Texts[$FileRelative].Contains('[ResolvedDynamically]')) { $Reason.Value = 'full run (declares a [ResolvedDynamically] type, resolved by reflection)'; return $true }
    foreach ($name in $Graph.TypesByFile[$FileRelative]) {
        if ($Graph.UnboundedTypes.Contains($name)) { $Reason.Value = "full run ($name extends a type this repository does not declare, so its callers cannot be enumerated)"; return $true }
        if ($Graph.UnresolvedServiceImplementations.ContainsKey($name)) {
            $Reason.Value = "full run ($name is registered as the implementation of $($Graph.UnresolvedServiceImplementations[$name]), a service this repository does not declare and no type here names, so its consumers cannot be enumerated)"
            return $true
        }
    }
    if (@($Graph.TypesByFile[$FileRelative]).Count -eq 0) { $Reason.Value = 'full run (declares no type)'; return $true }
    return $false
}

# An MCP resource or prompt reached THROUGH the closure is an entry point just as a tool file is.
# Rooting it only when it is the changed file left a file consumed by a resource resolving to
# "nothing observes this", although the fixtures assert on it through that resource.
function Add-ClosureOwnerReach([string] $Type, $Graph, [string] $RootFileRelative, $ToolFiles, $EntryPointFiles) {
    foreach ($owner in $Graph.TypeFiles[$Type]) {
        if ($owner.StartsWith($manifest.toolSourceRoot) -and $owner.EndsWith('.cs')) { [void]$ToolFiles.Add($owner) }
        if ($owner -ne $RootFileRelative -and $owner.EndsWith('.cs') -and (Test-McpEntryPointFile $owner)) { [void]$EntryPointFiles.Add($owner) }
    }
}

function Add-ClosureVerbReach([string] $Type, $Graph, $Selected) {
    if (-not $Graph.VerbsByType.ContainsKey($Type)) { return 0 }
    $verbCount = 0
    foreach ($verb in $Graph.VerbsByType[$Type]) {
        $verbFixtures = @(Select-FixturesForVerb $verb)
        if ($verbFixtures.Count -gt 0) { $verbCount++ }
        foreach ($n in $verbFixtures) { [void]$Selected.Add($n) }
    }
    return $verbCount
}

# Walks the consumer closure once and buckets what it reaches: the tool files and MCP resource/prompt
# files consumed transitively, and how many covered CLI verbs the closure's types declare. The verb
# fixtures are resolved here too, since Select-FixturesForVerb is cached and cheapest to call inline.
function Get-ClosureReachability($Closure, $Graph, [string] $RootFileRelative) {
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $toolFiles = New-Object System.Collections.Generic.HashSet[string]
    $entryPointFiles = New-Object System.Collections.Generic.HashSet[string]
    $verbCount = 0
    foreach ($type in $Closure) {
        Add-ClosureOwnerReach $type $Graph $RootFileRelative $toolFiles $entryPointFiles
        $verbCount += Add-ClosureVerbReach $type $Graph $selected
    }
    return @{ Selected = $selected; ToolFiles = $toolFiles; EntryPointFiles = $entryPointFiles; VerbCount = $verbCount }
}

function Select-FixturesForProductFile([string] $FileRelative, [ref] $Reason) {
    $g = Get-Graph
    if (Test-ProductFileFullRunReason $FileRelative $g $Reason) { return @() }
    $closure = @(Get-ConsumerClosure $FileRelative)
    # Reaching such an implementation is as unbounded as being one: the change flows into every
    # consumer of the external service, and none of them is in the closure.
    foreach ($type in $closure) {
        if ($g.UnresolvedServiceImplementations.ContainsKey($type)) {
            $Reason.Value = "full run (reaches $type, the implementation of $($g.UnresolvedServiceImplementations[$type]), a service this repository does not declare and no type here names, so its consumers cannot be enumerated)"
            return @()
        }
    }
    $reach = Get-ClosureReachability $closure $g $FileRelative
    foreach ($toolFile in $reach.ToolFiles) {
        foreach ($n in @(Select-FixturesForTool $toolFile)) { [void]$reach.Selected.Add($n) }
    }
    foreach ($entryPointFile in $reach.EntryPointFiles) {
        foreach ($n in @(Select-FixturesForEntryPoint $entryPointFile)) { [void]$reach.Selected.Add($n) }
    }
    if ($reach.ToolFiles.Count -eq 0 -and $reach.EntryPointFiles.Count -eq 0 -and $reach.VerbCount -eq 0) {
        $Reason.Value = "no MCP tool, MCP resource/prompt or covered CLI verb consumes it (closure $($closure.Count) type(s))"
        return @()
    }
    if ($reach.Selected.Count -eq 0) {
        $Reason.Value = "full run ($($reach.ToolFiles.Count) consuming tool file(s) and $($reach.EntryPointFiles.Count) consuming MCP resource/prompt file(s) select no fixture)"
        return @()
    }
    $Reason.Value = "reached from $($reach.ToolFiles.Count) tool file(s), $($reach.EntryPointFiles.Count) MCP resource/prompt file(s) and $($reach.VerbCount) covered verb(s) over $($closure.Count) consumer type(s)"
    return @($reach.Selected)
}

# The diff-only gate for a registration file: no diff to read, an empty diff, or a changed line that
# is not itself a registration statement each force a full run. Returns the changed lines on success,
# or $null (with Reason set) on any of those refusals.
function Get-RegistrationOnlyChangedLines([string] $FileRelative, [ref] $Reason) {
    if ([string]::IsNullOrWhiteSpace($BaseRef)) { $Reason.Value = 'full run (registration file, no diff available to narrow it)'; return $null }
    $diff = @(& git -C $root diff -U0 "$BaseRef...$HeadRef" -- $FileRelative)
    if ($LASTEXITCODE -ne 0) { $Reason.Value = 'full run (registration file, git diff failed)'; return $null }
    $changed = @($diff | Where-Object { ($_.StartsWith('+') -or $_.StartsWith('-')) -and -not ($_.StartsWith('+++') -or $_.StartsWith('---')) } | ForEach-Object { $_.Substring(1) })
    if ($changed.Count -eq 0) { $Reason.Value = 'full run (registration file, empty diff)'; return $null }
    # A line that is only punctuation, a brace or a comment carries no resolution change.
    # Both alternatives are anchored: an unanchored registration alternative accepted any line that
    # merely CONTAINED a registration call, so `services.AddSingleton<IFoo, Foo>(); Reset();` passed
    # as registration-only and the second statement narrowed the run with nothing guarding it.
    $registrationStatement = '^\s*(?://.*|/\*.*|\*.*|\}|\{|\)\s*;?|)$|^\s*(?:services|builder|collection)\s*\.\s*(?:Add|Try(?:Add)?)\w*\s*[<(][^;]*;?\s*$|^\s*\.\s*As\w*\s*<[^;]*;?\s*$'
    $offending = @($changed | Where-Object { $_ -notmatch $registrationStatement })
    if ($offending.Count -gt 0) {
        $Reason.Value = "full run (registration file, $($offending.Count) changed line(s) are not registration statements)"
        return $null
    }
    # The comma keeps this an array on the way out: unwrapped, a one-line diff would return as a bare
    # string and lose both .Count and array semantics for the caller.
    return ,$changed
}

# The known types named on the changed lines, and the files (outside the registration set itself)
# that declare them - the set whose reachability decides the registration file's own selection.
function Get-RegistrationChangedTypeOwners($Changed, $Graph, [ref] $Reason) {
    $names = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in $Changed) {
        foreach ($m in [regex]::Matches($line, '(?<![\w.])([A-Za-z_]\w*)')) {
            $name = $m.Groups[1].Value
            if ($Graph.TypeFiles.ContainsKey($name)) { [void]$names.Add($name) }
        }
    }
    if ($names.Count -eq 0) { $Reason.Value = 'full run (registration file, no known type on the changed lines)'; return $null }
    $owners = New-Object System.Collections.Generic.HashSet[string]
    foreach ($name in $names) {
        foreach ($owner in $Graph.TypeFiles[$name]) { if ($Graph.Registration -notcontains $owner) { [void]$owners.Add($owner) } }
    }
    # The comma keeps this a HashSet on the way out: unwrapped, a single owner would return as a bare
    # string and lose .Count for the caller.
    return ,$owners
}

function Select-FixturesForRegistrationFile([string] $FileRelative, [ref] $Reason) {
    # The composition root is named by nothing and names everything, so the graph cannot place it.
    # Its diff can: a pull request that adds `services.AddSingleton<IFoo, Foo>()` changes what Foo's
    # consumers resolve and nothing else. Only a diff made exclusively of registration statements
    # qualifies - anything else (a new using, a reordered method, a changed lifetime helper) can
    # affect resolution globally and still runs the whole suite.
    $changed = Get-RegistrationOnlyChangedLines $FileRelative $Reason
    if ($null -eq $changed) { return @() }
    $g = Get-Graph
    $owners = Get-RegistrationChangedTypeOwners $changed $g $Reason
    if ($null -eq $owners) { return @() }
    $selected = New-Object System.Collections.Generic.HashSet[string]
    foreach ($owner in $owners) {
        $ownerReason = ''
        $ownerFixtures = @(Select-FixturesForProductFile $owner ([ref]$ownerReason))
        if ($ownerFixtures.Count -eq 0 -and $ownerReason.StartsWith('full run')) {
            $Reason.Value = "full run (registration of $owner : $ownerReason)"
            return @()
        }
        foreach ($n in $ownerFixtures) { [void]$selected.Add($n) }
    }
    if ($selected.Count -eq 0) { $Reason.Value = "no fixture reaches the $($owners.Count) type(s) whose registration changed"; return @() }
    $Reason.Value = "registration of $($owners.Count) type(s) over $($changed.Count) changed line(s)"
    return @($selected)
}

function Select-FixturesForDataFile([string] $FileRelative, [ref] $Reason) {
    # A non-code asset under clio/ (a rules table, a catalog) is reached by name, so the product
    # files that spell its file name out are its consumers; from there the graph rules apply.
    $g = Get-Graph
    $leaf = [System.IO.Path]::GetFileName($FileRelative)
    $holders = @( $g.Texts.Keys | Where-Object { $g.Texts[$_].Contains($leaf) } | Sort-Object)
    if ($holders.Count -eq 0) { $Reason.Value = 'full run (no product file names this asset)'; return @() }
    $selected = New-Object System.Collections.Generic.HashSet[string]
    foreach ($holder in $holders) {
        $holderReason = ''
        $holderFixtures = @(Select-FixturesForProductFile $holder ([ref]$holderReason))
        # Checked per holder, not against what is already selected: one holder resolving to fixtures
        # must not hide another whose blast radius is unknown.
        if ($holderFixtures.Count -eq 0 -and $holderReason.StartsWith('full run')) { $Reason.Value = "full run (asset holder $holder : $holderReason)"; return @() }
        foreach ($n in $holderFixtures) { [void]$selected.Add($n) }
    }
    if ($selected.Count -eq 0) { $Reason.Value = "no fixture reaches the $($holders.Count) file(s) naming this asset"; return @() }
    $Reason.Value = "asset named by $($holders.Count) product file(s)"
    return @($selected)
}

# --- inventory mode ------------------------------------------------------------------------------
if ($Inventory) {
    $toolRootFull = Join-Path $root $manifest.toolSourceRoot
    $reachability = @{}
    foreach ($name in $fixtureSources.Keys) { $reachability[$name] = New-Object System.Collections.Generic.List[string] }
    $uncovered = New-Object System.Collections.Generic.List[string]
    foreach ($toolFile in Get-ChildItem -LiteralPath $toolRootFull -Filter '*.cs' -File -Recurse) {
        $relative = $toolFile.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
        # A tool file under fullRunPaths is classified by rule 2 and never selects anything itself.
        if (Test-GlobMatch $relative $manifest.fullRunPaths) { continue }
        $declared = Get-ToolDeclarations $toolFile.FullName
        if ($declared.Classes.Count -eq 0 -and $declared.Literals.Count -eq 0) { continue }
        $names = @(Select-FixturesForTool $relative)
        if ($names.Count -eq 0) { $uncovered.Add($relative); continue }
        foreach ($name in $names) { $reachability[$name].Add($relative) }
    }
    $fixturesOut = [ordered]@{}
    foreach ($key in ($fixturesByFile.Keys | Sort-Object)) { $fixturesOut[$key] = @($fixturesByFile[$key] | Sort-Object) }
    $reachOut = [ordered]@{}
    foreach ($key in ($reachability.Keys | Sort-Object)) { $reachOut[$key] = @($reachability[$key] | Sort-Object) }
    $noEnvironmentOut = [ordered]@{}
    foreach ($key in ($fixtureNoEnvironmentOnly.Keys | Sort-Object)) { $noEnvironmentOut[$key] = $fixtureNoEnvironmentOnly[$key] }
    $survivesOut = [ordered]@{}
    foreach ($key in ($fixtureSurvivesTeamCity.Keys | Sort-Object)) { $survivesOut[$key] = $fixtureSurvivesTeamCity[$key] }
    # Every product file the graph says no fixture can observe. Pinned in the repository, because
    # skipping the build for such a file is only safe while a human agrees the file is really
    # outside the MCP surface - a silent addition here is a test that stopped running.
    $g = Get-Graph
    $unreachable = New-Object System.Collections.Generic.List[string]
    foreach ($relative in ($g.TypesByFile.Keys | Sort-Object)) {
        if (Test-GlobMatch $relative $manifest.ignoredPaths) { continue }
        if (Test-GlobMatch $relative $manifest.fullRunPaths) { continue }
        if (@($manifest.registrationFiles) -contains $relative) { continue }
        # Skip only a file that really declares a tool - the same test the classify path applies.
        # Skipping everything under toolSourceRoot left the response records, linters and stores
        # that live there outside the pin, although classify routes them through the graph and can
        # therefore reach `none` for them; the pin exists precisely to make that reviewable.
        # An MCP resource or prompt file is rooted by Select-FixturesForEntryPoint, so it never
        # reaches the unreachable verdict and does not belong in the pin.
        if (Test-McpEntryPointFile $relative) { continue }
        if ($relative.StartsWith($manifest.toolSourceRoot) -and $relative.EndsWith('.cs')) {
            $toolPath = Join-Path $root $relative
            if (Test-Path -LiteralPath $toolPath) {
                $declaredTool = Get-ToolDeclarations $toolPath
                if (($declaredTool.Classes.Count -gt 0) -or ($declaredTool.Literals.Count -gt 0)) { continue }
            } else { continue }
        }
        $reason = ''
        if (@(Select-FixturesForProductFile $relative ([ref]$reason)).Count -eq 0 -and -not $reason.StartsWith('full run')) {
            $unreachable.Add($relative)
        }
    }
    # The same coverage gap as $uncovered, one entry-point kind out: a file declaring an MCP resource
    # or prompt that no fixture names. It never reaches the unreachable pin, because rule 7's
    # entry-point form escalates it to a full run instead - so without this list the gap is paid for on
    # every change and recorded nowhere.
    $uncoveredEntryPoints = New-Object System.Collections.Generic.List[string]
    foreach ($relative in ($g.TypesByFile.Keys | Sort-Object)) {
        if (Test-GlobMatch $relative $manifest.ignoredPaths) { continue }
        if (Test-GlobMatch $relative $manifest.fullRunPaths) { continue }
        if (-not (Test-McpEntryPointFile $relative)) { continue }
        if (@(Select-FixturesForEntryPoint $relative).Count -eq 0) { $uncoveredEntryPoints.Add($relative) }
    }
    [pscustomobject]@{
        fixtures = $fixturesOut; reachability = $reachOut; noEnvironmentOnly = $noEnvironmentOut; survivesTeamCity = $survivesOut
        uncoveredTools = @($uncovered | Sort-Object)
        uncoveredEntryPoints = @($uncoveredEntryPoints)
        unreachableProductFiles = @($unreachable)
        lexerResidue = @((Get-Graph).LexerResidue)
    } | ConvertTo-Json -Depth 4
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
    if (Test-GlobMatch $file $manifest.ignoredPaths) { Add-Decision $file 'ignored (documentation or unrelated project)' @(); continue }
    if (-not (Test-GlobMatch $file $manifest.relevantPaths)) { Add-Decision $file 'ignored (outside relevantPaths)' @(); continue }
    $relevantCount++

    if (@($manifest.registrationFiles) -contains $file) {
        $reason = ''
        $names = @(Select-FixturesForRegistrationFile $file ([ref]$reason))
        if ($names.Count -eq 0) {
            if ($reason.StartsWith('full run')) { $mode = 'full' }
            Add-Decision $file $reason @(); continue
        }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file $reason $names
        continue
    }

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
        # Not every file under Tools/ is a tool: response records, linters and stores live there too,
        # and they have no tool name for a fixture to reference. Only a file that really declares a
        # tool forces a full run when nothing covers it; the rest go through the graph like any
        # other product file.
        $toolPath = Join-Path $root $file
        $declaresTool = $false
        if (Test-Path -LiteralPath $toolPath) {
            $declared = Get-ToolDeclarations $toolPath
            $declaresTool = ($declared.Classes.Count -gt 0) -or ($declared.Literals.Count -gt 0)
        }
        if ($declaresTool) {
            $names = @(Select-FixturesForTool $file)
            if ($names.Count -eq 0) { $mode = 'full'; Add-Decision $file 'full run (tool file selects no fixture)' @(); continue }
            foreach ($n in $names) { [void]$fixtures.Add($n) }
            Add-Decision $file 'tool file' $names
            continue
        }
    }

    if ($file.StartsWith($manifest.productSourceRoot) -and (Test-McpEntryPointFile $file)) {
        $names = @(Select-FixturesForEntryPoint $file)
        if ($names.Count -eq 0) { $mode = 'full'; Add-Decision $file 'full run (MCP resource/prompt file selects no fixture)' @(); continue }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file 'MCP resource/prompt file' $names
        continue
    }

    if ($file.StartsWith($manifest.productSourceRoot) -and $file.EndsWith('.cs')) {
        $reason = ''
        $names = @(Select-FixturesForProductFile $file ([ref]$reason))
        if ($names.Count -eq 0) {
            if ($reason.StartsWith('full run')) { $mode = 'full' }
            Add-Decision $file $reason @(); continue
        }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file $reason $names
        continue
    }

    if ($file.StartsWith($manifest.productSourceRoot)) {
        $reason = ''
        $names = @(Select-FixturesForDataFile $file ([ref]$reason))
        if ($names.Count -eq 0) {
            if ($reason.StartsWith('full run')) { $mode = 'full' }
            Add-Decision $file $reason @(); continue
        }
        foreach ($n in $names) { [void]$fixtures.Add($n) }
        Add-Decision $file $reason $names
        continue
    }

    $mode = 'full'; Add-Decision $file 'full run (no rule)' @()
}

if ($mode -eq 'subset' -and $fixtures.Count -eq 0) {
    if ($relevantCount -eq 0) { $decisions.Add('nothing the diff touches is relevant to this suite -> nothing to run') }
    else { $decisions.Add('no fixture in this suite can observe the changed code -> nothing to run') }
    $mode = 'none'
}
if ($mode -eq 'subset' -and -not $IncludeNoEnvironment) {
    $needsTeamCity = @($fixtures | Where-Object { -not $fixtureNoEnvironmentOnly[$_] })
    if ($needsTeamCity.Count -eq 0) {
        $decisions.Add('every selected fixture is positively NoEnvironment-only and that tier runs on GitHub -> nothing to run on TeamCity')
        $mode = 'none'
    }
}
if ($mode -eq 'subset') {
    $runnable = @($fixtures | Where-Object { $fixtureSurvivesTeamCity[$_] })
    if ($runnable.Count -eq 0) {
        $decisions.Add("no test of the selected fixtures survives the TeamCity filter (it excludes $($teamCityExcluded -join ', ')) -> a build would deploy Creatio and run nothing")
        $mode = 'none'
    }
}

if ($mode -eq 'subset' -and $fixtures.Count -gt [int]$manifest.maxSubsetFixtures) {
    $decisions.Add("subset of $($fixtures.Count) fixtures exceeds maxSubsetFixtures=$($manifest.maxSubsetFixtures) -> full run")
    $mode = 'full'
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
