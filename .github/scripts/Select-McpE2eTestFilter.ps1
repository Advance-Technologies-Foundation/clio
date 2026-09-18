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
#   uncoveredTools: [tool files no fixture names] }.
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
$fixtureNoEnvironmentOnly = @{}  # fixture class name -> $true when its file is positively NoEnvironment and has no Sandbox test
foreach ($file in Get-ChildItem -LiteralPath $fixtureRoot -Filter '*.cs' -File) {
    $text = Read-Text $file.FullName
    if ($text -cnotmatch '\[\s*(Test|TestFixture|TestCase|TestCaseSource|Theory)\b') { continue }
    $classes = @([regex]::Matches($text, $fixtureDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($classes.Count -eq 0) { continue }
    $fixturesByFile[$file.BaseName] = $classes
    # Positive classification only: a fixture that carries neither tier (bare Category("E2E")) runs on
    # TeamCity under the base filter and nowhere on GitHub, so it must never be treated as covered.
    $hasSandbox = $text.Contains('McpE2E.Sandbox') -or $text.Contains('McpE2ECategories.Sandbox')
    $hasNoEnvironment = $text.Contains('McpE2E.NoEnvironment') -or $text.Contains('McpE2ECategories.NoEnvironment')
    foreach ($class in $classes) { $fixtureSources[$class] = $text; $fixtureNoEnvironmentOnly[$class] = ($hasNoEnvironment -and -not $hasSandbox) }
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
    return $script:nonCode.Replace($Text, {
        param($match)
        $builder = New-Object System.Text.StringBuilder $match.Value.Length
        foreach ($character in $match.Value.ToCharArray()) {
            if ($character -eq "`n" -or $character -eq "`r") { [void]$builder.Append($character) }
            else { [void]$builder.Append(' ') }
        }
        return $builder.ToString()
    })
}

$graph = $null
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
    # `Clio.Common.Foo` and `global::Clio.Common.Foo`: the dot before Foo hides it from $identifier,
    # and 1247 references in this tree are written that way. Only chains rooted in a namespace this
    # repository declares are followed, so `task.Result` is still member access, not a reference.
    $qualified = [regex] '(?<![\w.])(?:global::)?([A-Za-z_]\w*)((?:\.[A-Za-z_]\w*)+)'
    $namespaceDeclaration = '(?m)^\s*namespace\s+([A-Za-z_]\w*)'
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
    # References are NOT read from the blanked text: a type named only in a comment adds an edge,
    # which widens the selection and is the safe direction.
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
    $registrationPair = [regex] 'Add(?:Singleton|Scoped|Transient|KeyedSingleton)<\s*(?:[\w.]*\.)?(\w+)\s*,\s*(?:[\w.]*\.)?(\w+)\s*>'
    # services.AddSingleton<ILogger>(ConsoleLogger.Instance) and the lambda form: one generic argument
    # and an instance or factory that names the implementation somewhere on the same line.
    # Only the opening of the call: the argument is then read by matching parentheses, because a
    # lambda body holds semicolons of its own and a literal can hold anything.
    $registrationFactory = [regex] 'Add(?:Singleton|Scoped|Transient|KeyedSingleton)<\s*(?:[\w.]*\.)?(\w+)\s*>\s*\('

    $texts = @{}
    $productRoot = Join-Path $root $manifest.productSourceRoot
    foreach ($file in Get-ChildItem -LiteralPath $productRoot -Filter '*.cs' -File -Recurse) {
        $relative = $file.FullName.Substring($root.Length).TrimStart('/', '\').Replace('\', '/')
        if ($relative -cmatch '/(bin|obj)/') { continue }
        $texts[$relative] = Read-Text $file.FullName
    }

    # The graph node is a TYPE, not a file. A file that declares a narrow helper next to a widely used
    # one would otherwise merge their consumer sets and make the narrow type look as connected as the
    # wide one - measured as the single largest source of over-approximation in this tree.
    # A type owns the text from its declaration to the next top-level declaration, so nested types are
    # part of their outer type and no character of the file is left unattributed.
    $interfaceTypes = New-Object System.Collections.Generic.HashSet[string]
    $baseList = @{}    # type name -> the names in its base list
    $namespaceRoots = New-Object System.Collections.Generic.HashSet[string]
    foreach ($relative in $texts.Keys) {
        foreach ($m in [regex]::Matches($texts[$relative], $namespaceDeclaration)) { [void]$namespaceRoots.Add($m.Groups[1].Value) }
    }
    # An alias of a namespace this repository declares is a root too; one aliasing System.* adds
    # nothing, because no type behind it is ours.
    foreach ($relative in $texts.Keys) {
        foreach ($m in [regex]::Matches($texts[$relative], $namespaceAlias)) {
            if ($namespaceRoots.Contains($m.Groups[2].Value)) { [void]$namespaceRoots.Add($m.Groups[1].Value) }
        }
    }

    $typeBody = @{}    # type name -> the text that belongs to it (a partial type accumulates)
    $typeFiles = @{}   # type name -> files declaring it
    $typesByFile = @{} # file -> type names declared at its top level
    foreach ($relative in $texts.Keys) {
        $text = $texts[$relative]
        # Scan for declarations on a copy with raw-string contents blanked out, so a code sample
        # inside a literal cannot be read as the file's next top-level type. Offsets are preserved.
        $scan = Remove-NonCode $text
        $declarationMatches = @($typeDeclaration.Matches($scan))
        if ($declarationMatches.Count -eq 0) { $typesByFile[$relative] = @(); continue }
        $topIndent = ($declarationMatches | ForEach-Object { $_.Groups[1].Value.Length } | Measure-Object -Minimum).Minimum
        # A nested type indented less than the type that contains it would become the file's only
        # "top level" and swallow the outer type's body. Two files in this tree are formatted that
        # way; rather than guess, attribute the whole file to every type it declares.
        if ($declarationMatches[0].Groups[1].Value.Length -ne $topIndent) {
            $declaredAll = @($declarationMatches | ForEach-Object { $_.Groups[3].Value } | Select-Object -Unique)
            foreach ($name in $declaredAll) {
                if (-not $typeBody.ContainsKey($name)) { $typeBody[$name] = New-Object System.Text.StringBuilder }
                [void]$typeBody[$name].Append($text)
                if (-not $typeFiles.ContainsKey($name)) { $typeFiles[$name] = New-Object System.Collections.Generic.HashSet[string] }
                [void]$typeFiles[$name].Add($relative)
            }
            $typesByFile[$relative] = $declaredAll
            continue
        }
        $tops = @($declarationMatches | Where-Object { $_.Groups[1].Value.Length -eq $topIndent })
        foreach ($top in $tops) {
            if ($top.Groups[2].Value -eq 'interface') { [void]$interfaceTypes.Add($top.Groups[3].Value) }
            if ($top.Groups[4].Success) {
                $baseNames = @([regex]::Matches($top.Groups[4].Value, '(?<![\w.])([A-Za-z_]\w*)') | ForEach-Object { $_.Groups[1].Value })
                if (-not $baseList.ContainsKey($top.Groups[3].Value)) { $baseList[$top.Groups[3].Value] = New-Object System.Collections.Generic.HashSet[string] }
                foreach ($baseName in $baseNames) { [void]$baseList[$top.Groups[3].Value].Add($baseName) }
            }
        }
        # Usings, the namespace and file-level attributes precede every type and can carry a reference
        # that belongs to all of them.
        $preamble = $text.Substring(0, $tops[0].Index)
        $declared = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $tops.Count; $i++) {
            $name = $tops[$i].Groups[3].Value
            $from = $tops[$i].Index
            $to = if ($i + 1 -lt $tops.Count) { $tops[$i + 1].Index } else { $text.Length }
            if (-not $typeBody.ContainsKey($name)) { $typeBody[$name] = New-Object System.Text.StringBuilder }
            [void]$typeBody[$name].Append($preamble).Append($text.Substring($from, $to - $from))
            if (-not $typeFiles.ContainsKey($name)) { $typeFiles[$name] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$typeFiles[$name].Add($relative)
            if (-not $declared.Contains($name)) { $declared.Add($name) }
        }
        $typesByFile[$relative] = @($declared)
    }

    $verbsByType = @{}
    $consumers = @{}   # type name -> type names whose text names it
    foreach ($name in $typeBody.Keys) {
        $body = $typeBody[$name].ToString()
        $verbs = @([regex]::Matches($body, $verbDeclaration) | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
        if ($verbs.Count -gt 0) { $verbsByType[$name] = $verbs }
        $tokens = New-Object System.Collections.Generic.HashSet[string]
        foreach ($m in $identifier.Matches($body)) { [void]$tokens.Add($m.Groups[1].Value) }
        foreach ($m in $qualified.Matches($body)) {
            if (-not $namespaceRoots.Contains($m.Groups[1].Value)) { continue }
            foreach ($segment in $m.Groups[2].Value.Split('.')) { if ($segment) { [void]$tokens.Add($segment) } }
        }
        foreach ($token in $tokens) {
            if ($token -eq $name -or -not $typeBody.ContainsKey($token)) { continue }
            if (-not $consumers.ContainsKey($token)) { $consumers[$token] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$consumers[$token].Add($name)
        }
    }

    # Extension class -> extended type. `value.Normalize()` names neither the extension class nor
    # its file, so the only route from a call site to the extension is the type it extends.
    $unboundedTypes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($name in @($typeBody.Keys)) {
        foreach ($m in $extensionParameter.Matches($typeBody[$name].ToString())) {
            $extended = $m.Groups[1].Value
            if ($extended -eq $name) { continue }
            if (-not $typeBody.ContainsKey($extended)) {
                # `this string`, `this IEnumerable<T>`, `this Exception`: the receiver is not ours, so
                # the callers cannot be enumerated. Half the extension methods in this tree are of
                # that shape, and guessing an empty consumer set for them would skip the build.
                [void]$unboundedTypes.Add($name)
                continue
            }
            if (-not $consumers.ContainsKey($extended)) { continue }
            if (-not $consumers.ContainsKey($name)) { $consumers[$name] = New-Object System.Collections.Generic.HashSet[string] }
            foreach ($c in $consumers[$extended]) { if ($c -ne $name) { [void]$consumers[$name].Add($c) } }
        }
    }

    # Implementation -> interface. A consumer injects IFoo and never spells Foo out, so without this
    # edge every service behind an interface looks unreachable. Restricted to base types declared as
    # `interface`: a base CLASS in this tree (Command, BaseTool) is a template-method host whose
    # hundreds of subclasses are not interchangeable, and following it merges the whole tree.
    foreach ($name in @($baseList.Keys)) {
        foreach ($baseName in $baseList[$name]) {
            if (-not $interfaceTypes.Contains($baseName)) { continue }
            if (-not $consumers.ContainsKey($baseName)) { continue }
            if (-not $consumers.ContainsKey($name)) { $consumers[$name] = New-Object System.Collections.Generic.HashSet[string] }
            foreach ($c in $consumers[$baseName]) { if ($c -ne $name) { [void]$consumers[$name].Add($c) } }
        }
    }

    $registration = @($manifest.registrationFiles)
    foreach ($registrationFile in $registration) {
        if (-not $texts.ContainsKey($registrationFile)) { continue }
        # A `;` inside a literal would end the statement early and hide the implementation.
        $registrationText = Remove-NonCode $texts[$registrationFile]
        foreach ($m in $registrationPair.Matches($registrationText)) {
            $service = $m.Groups[1].Value
            $implementation = $m.Groups[2].Value
            if (-not $typeBody.ContainsKey($service) -or -not $typeBody.ContainsKey($implementation)) { continue }
            if (-not $consumers.ContainsKey($implementation)) { $consumers[$implementation] = New-Object System.Collections.Generic.HashSet[string] }
            if (-not $consumers.ContainsKey($service)) { continue }
            foreach ($c in $consumers[$service]) { if ($c -ne $implementation) { [void]$consumers[$implementation].Add($c) } }
        }
        # The factory form carries the implementation in the argument rather than in a second generic
        # parameter, so every known type named on that line is linked to the service.
        foreach ($m in $registrationFactory.Matches($registrationText)) {
            $service = $m.Groups[1].Value
            if (-not $typeBody.ContainsKey($service) -or -not $consumers.ContainsKey($service)) { continue }
            $start = $m.Index + $m.Length
            if ($start -ge $registrationText.Length -or $registrationText[$start] -eq ')') { continue }
            $depth = 1
            $cursor = $start
            $limit = [Math]::Min($registrationText.Length, $start + 4000)
            while ($cursor -lt $limit -and $depth -gt 0) {
                $character = $registrationText[$cursor]
                if ($character -eq '(') { $depth++ } elseif ($character -eq ')') { $depth-- }
                $cursor++
            }
            $argument = $registrationText.Substring($start, $cursor - $start)
            foreach ($t in [regex]::Matches($argument, '(?<![\w.])([A-Za-z_]\w*)')) {
                $implementation = $t.Groups[1].Value
                if ($implementation -eq $service -or -not $typeBody.ContainsKey($implementation)) { continue }
                if (-not $consumers.ContainsKey($implementation)) { $consumers[$implementation] = New-Object System.Collections.Generic.HashSet[string] }
                foreach ($c in $consumers[$service]) { if ($c -ne $implementation) { [void]$consumers[$implementation].Add($c) } }
            }
        }
    }

    # A type declared only in a registration file is never traversed through: every type is named
    # there, so following it would make every change reach every tool.
    $registrationTypes = New-Object System.Collections.Generic.HashSet[string]
    foreach ($registrationFile in $registration) {
        if (-not $typesByFile.ContainsKey($registrationFile)) { continue }
        foreach ($name in $typesByFile[$registrationFile]) {
            $owners = @($typeFiles[$name] | Where-Object { $registration -notcontains $_ })
            if ($owners.Count -eq 0) { [void]$registrationTypes.Add($name) }
        }
    }

    # An invariant the guard checks: after blanking, no quote, comment marker or char literal may
    # remain anywhere. A lexer that misses a literal form leaves one behind, and that is exactly the
    # class of bug that silently narrows the structural parse.
    $residue = New-Object System.Collections.Generic.List[string]
    foreach ($relative in ($texts.Keys | Sort-Object)) {
        $blanked = Remove-NonCode $texts[$relative]
        if ($blanked.Contains('"') -or $blanked.Contains('//') -or $blanked.Contains('/*')) { $residue.Add($relative) }
    }

    $script:graph = @{
        LexerResidue = $residue
        Texts = $texts; TypeFiles = $typeFiles; TypesByFile = $typesByFile
        VerbsByType = $verbsByType; Consumers = $consumers; RegistrationTypes = $registrationTypes
        UnboundedTypes = $unboundedTypes
        Registration = $registration
    }
    return $script:graph
}

$closureCache = @{}
function Get-ConsumerClosure([string] $FileRelative) {
    if ($script:closureCache.ContainsKey($FileRelative)) { return $script:closureCache[$FileRelative] }
    # Every type that transitively names a type declared in this file, plus those types themselves.
    $g = Get-Graph
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $queue = New-Object System.Collections.Generic.Queue[string]
    foreach ($name in $g.TypesByFile[$FileRelative]) { if ($seen.Add($name)) { $queue.Enqueue($name) } }
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $g.Consumers.ContainsKey($current)) { continue }
        foreach ($consumer in $g.Consumers[$current]) {
            if ($g.RegistrationTypes.Contains($consumer)) { continue }
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

function Select-FixturesForProductFile([string] $FileRelative, [ref] $Reason) {
    $g = Get-Graph
    if (-not $g.TypesByFile.ContainsKey($FileRelative)) { $Reason.Value = 'full run (file not in the tree)'; return @() }
    # AGENTS.md: [ResolvedDynamically] marks a service resolved by reflection or from another
    # assembly. That is exactly the edge an identifier scan cannot see, so the graph is not allowed
    # to conclude anything about such a file.
    if ($g.Texts[$FileRelative].Contains('[ResolvedDynamically]')) { $Reason.Value = 'full run (declares a [ResolvedDynamically] type, resolved by reflection)'; return @() }
    foreach ($name in $g.TypesByFile[$FileRelative]) {
        if ($g.UnboundedTypes.Contains($name)) { $Reason.Value = "full run ($name extends a type this repository does not declare, so its callers cannot be enumerated)"; return @() }
    }
    if (@($g.TypesByFile[$FileRelative]).Count -eq 0) { $Reason.Value = 'full run (declares no type)'; return @() }
    $closure = @(Get-ConsumerClosure $FileRelative)
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $toolFiles = New-Object System.Collections.Generic.HashSet[string]
    $verbCount = 0
    foreach ($type in $closure) {
        foreach ($owner in $g.TypeFiles[$type]) {
            if ($owner.StartsWith($manifest.toolSourceRoot) -and $owner.EndsWith('.cs')) { [void]$toolFiles.Add($owner) }
        }
        if ($g.VerbsByType.ContainsKey($type)) {
            foreach ($verb in $g.VerbsByType[$type]) {
                $verbFixtures = @(Select-FixturesForVerb $verb)
                if ($verbFixtures.Count -gt 0) { $verbCount++ }
                foreach ($n in $verbFixtures) { [void]$selected.Add($n) }
            }
        }
    }
    foreach ($toolFile in $toolFiles) {
        foreach ($n in @(Select-FixturesForTool $toolFile)) { [void]$selected.Add($n) }
    }
    if ($toolFiles.Count -eq 0 -and $verbCount -eq 0) {
        $Reason.Value = "no MCP tool and no covered CLI verb consumes it (closure $($closure.Count) type(s))"
        return @()
    }
    if ($selected.Count -eq 0) {
        $Reason.Value = "full run ($($toolFiles.Count) consuming tool file(s) select no fixture)"
        return @()
    }
    $Reason.Value = "reached from $($toolFiles.Count) tool file(s) and $verbCount covered verb(s) over $($closure.Count) consumer type(s)"
    return @($selected)
}

function Select-FixturesForRegistrationFile([string] $FileRelative, [ref] $Reason) {
    # The composition root is named by nothing and names everything, so the graph cannot place it.
    # Its diff can: a pull request that adds `services.AddSingleton<IFoo, Foo>()` changes what Foo's
    # consumers resolve and nothing else. Only a diff made exclusively of registration statements
    # qualifies - anything else (a new using, a reordered method, a changed lifetime helper) can
    # affect resolution globally and still runs the whole suite.
    if ([string]::IsNullOrWhiteSpace($BaseRef)) { $Reason.Value = 'full run (registration file, no diff available to narrow it)'; return @() }
    $diff = @(& git -C $root diff -U0 "$BaseRef...$HeadRef" -- $FileRelative)
    if ($LASTEXITCODE -ne 0) { $Reason.Value = 'full run (registration file, git diff failed)'; return @() }
    $changed = @($diff | Where-Object { ($_.StartsWith('+') -or $_.StartsWith('-')) -and -not ($_.StartsWith('+++') -or $_.StartsWith('---')) } | ForEach-Object { $_.Substring(1) })
    if ($changed.Count -eq 0) { $Reason.Value = 'full run (registration file, empty diff)'; return @() }
    # A line that is only punctuation, a brace or a comment carries no resolution change.
    $registrationStatement = '^\s*(?://.*|/\*.*|\*.*|\}|\{|\)\s*;?|)$|(?:services|builder|collection)\s*\.\s*(?:Add|Try(?:Add)?)\w*\s*[<(]|\.\s*As\w*\s*<'
    $offending = @($changed | Where-Object { $_ -notmatch $registrationStatement })
    if ($offending.Count -gt 0) {
        $Reason.Value = "full run (registration file, $($offending.Count) changed line(s) are not registration statements)"
        return @()
    }
    $g = Get-Graph
    $names = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in $changed) {
        foreach ($m in [regex]::Matches($line, '(?<![\w.])([A-Za-z_]\w*)')) {
            $name = $m.Groups[1].Value
            if ($g.TypeFiles.ContainsKey($name)) { [void]$names.Add($name) }
        }
    }
    if ($names.Count -eq 0) { $Reason.Value = 'full run (registration file, no known type on the changed lines)'; return @() }
    $selected = New-Object System.Collections.Generic.HashSet[string]
    $owners = New-Object System.Collections.Generic.HashSet[string]
    foreach ($name in $names) { foreach ($owner in $g.TypeFiles[$name]) { if ($g.Registration -notcontains $owner) { [void]$owners.Add($owner) } } }
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
    # Every product file the graph says no fixture can observe. Pinned in the repository, because
    # skipping the build for such a file is only safe while a human agrees the file is really
    # outside the MCP surface - a silent addition here is a test that stopped running.
    $g = Get-Graph
    $unreachable = New-Object System.Collections.Generic.List[string]
    foreach ($relative in ($g.TypesByFile.Keys | Sort-Object)) {
        if (Test-GlobMatch $relative $manifest.ignoredPaths) { continue }
        if (Test-GlobMatch $relative $manifest.fullRunPaths) { continue }
        if (@($manifest.registrationFiles) -contains $relative) { continue }
        if ($relative.StartsWith($manifest.toolSourceRoot)) { continue }
        $reason = ''
        if (@(Select-FixturesForProductFile $relative ([ref]$reason)).Count -eq 0 -and -not $reason.StartsWith('full run')) {
            $unreachable.Add($relative)
        }
    }
    [pscustomobject]@{
        fixtures = $fixturesOut; reachability = $reachOut
        uncoveredTools = @($uncovered | Sort-Object)
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
