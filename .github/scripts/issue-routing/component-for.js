#!/usr/bin/env node
'use strict';

// Tells an agent (or a person) which `component:*` label a new clio issue should carry.
//
//   node .github/scripts/issue-routing/component-for.js <file-or-mcp-tool>...
//   node .github/scripts/issue-routing/component-for.js --list
//
// Arguments are repository paths (clio/Command/PageUpdateCommand.cs) or MCP tool names
// (update-page). Matching uses the `paths` of each component in .github/component-owners.json.
// Add --json for machine-readable output. Exit code 0 = one component, 2 = several or none.

const fs = require('node:fs');
const path = require('node:path');
const { loadConfig, matchComponents } = require('./issue-routing.js');

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const TOOLS_DIR = 'clio/Command/McpServer/Tools';

// The MCP tool names a C# file registers: the `Name` of each `[McpServerTool(...)]` attribute, as a
// string literal or as a `const string` declared in the same file.
function declaredToolNames(text) {
  const constants = new Map([...text.matchAll(/const\s+string\s+(\w+)\s*=\s*"([^"]+)"/g)].map(m => [m[1], m[2]]));
  const names = [];
  for (const match of text.matchAll(/\[McpServerTool\(\s*Name\s*=\s*([^,)\]]+)/g)) {
    const value = match[1].trim();
    const name = value.startsWith('"') ? value.replaceAll('"', '') : constants.get(value.split('.').pop());
    if (name) names.push(name);
  }
  return names;
}

// Finds the tool file that registers an MCP tool name. A name that is only quoted elsewhere (a
// description, a cross-reference, a feature toggle) never counts; a name registered by more than one
// file is reported as unknown rather than guessed.
function toolFile(name) {
  const dir = path.join(repoRoot, TOOLS_DIR);
  if (!fs.existsSync(dir)) return null;
  const declaring = [];
  for (const entry of fs.readdirSync(dir, { recursive: true })) {
    const file = String(entry);
    if (!file.endsWith('.cs')) continue;
    const text = fs.readFileSync(path.join(dir, file), 'utf8');
    if (text.includes('McpServerTool(') && declaredToolNames(text).includes(name)) {
      declaring.push(`${TOOLS_DIR}/${file.split(path.sep).join('/')}`);
    }
  }
  return declaring.length === 1 ? declaring[0] : null;
}

function toFile(arg) {
  if (arg.includes('/') || arg.includes('.')) return { arg, file: arg };
  return { arg, file: toolFile(arg) };
}

function describe({ component, files }) {
  const owners = (component.owners || []).join(', ') || '(none)';
  return `${component.label}  — ${component.option}\n    owners: ${owners}; matched: ${files.join(', ')}`;
}

function main(argv) {
  const json = argv.includes('--json');
  const args = argv.filter(a => a !== '--json');
  const config = loadConfig(path.join(repoRoot, '.github/component-owners.json'));
  if (args.length === 0 || args.includes('--list')) {
    const list = config.components.filter(c => c.label).map(c => ({ id: c.id, label: c.label, option: c.option, owners: c.owners }));
    console.log(json ? JSON.stringify(list, null, 2) : list.map(c => `${c.label.padEnd(34)} ${c.option}`).join('\n'));
    return 0;
  }
  const resolved = args.map(toFile);
  const unknown = resolved.filter(r => !r.file).map(r => r.arg);
  const ranked = matchComponents(resolved.filter(r => r.file).map(r => r.file), config);
  if (json) {
    console.log(JSON.stringify({ components: ranked.map(r => ({ id: r.component.id, label: r.component.label, owners: r.component.owners, files: r.files })), unknown }, null, 2));
  } else {
    if (ranked.length === 0) console.log('No component covers these paths; use "Other / not sure" (no component label) and let a human triage.');
    else console.log(ranked.map(describe).join('\n'));
    if (unknown.length > 0) console.log(`Not an MCP tool name or a path: ${unknown.join(', ')}`);
    if (ranked.length > 1) console.log('Several components match: pick the one the issue is about and put exactly one component label on it.');
  }
  return ranked.length === 1 ? 0 : 2;
}

if (require.main === module) process.exitCode = main(process.argv.slice(2));

module.exports = { main, toolFile, declaredToolNames };
