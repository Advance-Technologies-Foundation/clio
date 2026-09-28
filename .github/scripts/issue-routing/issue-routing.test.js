'use strict';

// Run: node --test .github/scripts/issue-routing/issue-routing.test.js
// No dependencies: node:test and node:assert only, so CI needs no npm install.

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const routing = require('./issue-routing.js');

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const configPath = path.join(repoRoot, '.github', 'component-owners.json');
const templateDir = path.join(repoRoot, '.github', 'ISSUE_TEMPLATE');

const config = routing.validateConfig({
  fieldLabel: 'Component',
  triageLabel: { name: 'needs-triage' },
  components: [
    { id: 'mcp-server', option: 'MCP server (tools, prompts)', label: 'MCP', owners: ['alice', 'bob'] },
    { id: 'package', option: 'Packages', label: 'component:package', owners: ['@carol'] },
    { id: 'docs', option: 'Documentation', label: 'component:docs', owners: [] },
    { id: 'other', option: 'Other / not sure', triage: true },
  ],
});

function formBody(component, { crlf = false } = {}) {
  const body = [
    '### Component',
    '',
    component,
    '',
    '### clio version',
    '',
    '8.0.2.50',
    '',
    '### Steps to reproduce',
    '',
    '```shell',
    'clio push-pkg x.gz',
    '```',
  ].join('\n');
  return crlf ? body.replace(/\n/g, '\r\n') : body;
}

test('extracts a dropdown value from an issue-form body', () => {
  // Arrange
  const body = formBody('Packages');
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['Packages'], 'the text under "### Component" is the selected option');
});

test('extracts the value from a CRLF body with trailing spaces and a differently cased heading', () => {
  // Arrange
  const body = formBody('  MCP server (tools, prompts)  ', { crlf: true }).replace('### Component', '### component  ');
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['MCP server (tools, prompts)'], 'bodies edited in the browser arrive with CRLF; headings match case-insensitively');
});

test('keeps an option that contains commas as one value', () => {
  // Arrange
  const body = formBody('CLI core (settings, authentication, command parsing)');
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['CLI core (settings, authentication, command parsing)'], 'a single-select dropdown answer must not be split on commas');
});

test('treats _No response_ and an absent field as no value', () => {
  // Arrange
  const empty = formBody(routing.NO_RESPONSE);
  const blank = 'Something is broken, no form was used.';
  // Act
  const fromEmpty = routing.extractFieldValues(empty, 'Component');
  const fromBlank = routing.extractFieldValues(blank, 'Component');
  // Assert
  assert.deepEqual(fromEmpty, [], 'GitHub writes _No response_ for an optional field left empty');
  assert.deepEqual(fromBlank, [], 'a blank issue has no Component section');
});

test('uses the first Component heading so a later user-typed heading cannot override it', () => {
  // Arrange
  const body = `${formBody('Documentation')}\n\n### Component\n\nPackages\n`;
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['Documentation'], 'the form field precedes every free-text field, so the first occurrence is the real answer');
});

test('extracts only ticked items from a checkboxes field', () => {
  // Arrange
  const body = '### Component\n\n- [ ] Packages\n- [X] Documentation\n\n### Other\n\nx';
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['Documentation'], 'unticked checkbox items are not selections');
});

test('routes a known component to its label and owners', () => {
  // Arrange
  const body = formBody('MCP server (tools, prompts)');
  // Act
  const plan = routing.planRouting({ body, currentLabels: ['bug'], currentAssignees: [], config });
  // Assert
  assert.equal(plan.status, 'resolved', 'the option is in the map');
  assert.deepEqual(plan.labelsToAdd, ['MCP'], 'the component label is added');
  assert.deepEqual(plan.labelsToRemove, [], 'nothing is removed on a fresh issue');
  assert.deepEqual(plan.ownerCandidates, ['alice', 'bob'], 'owners are candidates in map order');
});

test('strips a leading @ from owners', () => {
  // Arrange
  const body = formBody('Packages');
  // Act
  const plan = routing.planRouting({ body, config });
  // Assert
  assert.deepEqual(plan.ownerCandidates, ['carol'], 'the assignee API takes a bare login');
});

test('never proposes an owner when the issue already has an assignee', () => {
  // Arrange
  const body = formBody('Packages');
  // Act
  const plan = routing.planRouting({ body, currentAssignees: ['dave'], config });
  // Assert
  assert.deepEqual(plan.ownerCandidates, [], 'an assignee is a claim and must not be overridden');
  assert.equal(plan.alreadyAssigned, true, 'the run reports why it did not assign');
});

test('adds the triage label when no component was chosen or it is unknown or "not sure"', () => {
  for (const [value, status] of [[routing.NO_RESPONSE, 'missing'], ['Something removed from the form', 'unknown'], ['Other / not sure', 'triage']]) {
    // Arrange
    const body = formBody(value);
    // Act
    const plan = routing.planRouting({ body, currentLabels: ['bug'], config });
    // Assert
    assert.equal(plan.status, status, `"${value}" classifies as ${status}`);
    assert.deepEqual(plan.labelsToAdd, ['needs-triage'], `"${value}" cannot be routed, so a human has to triage it`);
    assert.deepEqual(plan.ownerCandidates, [], `"${value}" has no owner to assign`);
  }
});

test('does not add triage when a human already set a component label', () => {
  // Arrange
  const body = 'free text issue';
  // Act
  const plan = routing.planRouting({ body, currentLabels: ['component:docs'], config });
  // Assert
  assert.deepEqual(plan.labelsToAdd, [], 'a component label means someone already triaged it');
});

test('swaps the component label when an edit changes the component', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = formBody('Documentation');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['bug', 'component:package'], currentAssignees: ['carol'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['component:package'], 'the label that came from the old choice is removed');
  assert.deepEqual(plan.labelsToAdd, ['component:docs'], 'the label for the new choice is added');
  assert.deepEqual(plan.ownerCandidates, [], 'the existing assignee is kept; reassignment is a human decision');
});

test('keeps a hand-added component label when an edit does not change the component', () => {
  // Arrange
  const body = formBody('Documentation');
  // Act
  const plan = routing.planRouting({ body, previousBody: body, currentLabels: ['component:docs', 'MCP'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, [], 'MCP was added by a human, not by the form choice');
  assert.deepEqual(plan.labelsToAdd, [], 'the routed label is already present');
});

test('removes the triage label once a component is chosen', () => {
  // Arrange
  const previousBody = formBody('Other / not sure');
  const body = formBody('Documentation');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['needs-triage'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['needs-triage'], 'the issue is routed now');
  assert.deepEqual(plan.labelsToAdd, ['component:docs'], 'the chosen component label is added');
});

test('a text-only edit is a no-op, so labels and owners a human removed stay removed', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = `${previousBody}\n\nMore details.`;
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['bug'], currentAssignees: [], config });
  // Assert
  assert.equal(plan.unchanged, true, 'the component did not change');
  assert.deepEqual([plan.labelsToAdd, plan.labelsToRemove, plan.ownerCandidates], [[], [], []], 'nothing is re-applied');
});

test('removes every other component label when the component changes, even after collapsed runs', () => {
  // Arrange: A was applied, the A->B run was cancelled by GitHub, this is the B->C run.
  const previousBody = formBody('Packages');
  const body = formBody('Documentation');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['MCP', 'bug'], currentAssignees: ['alice'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['MCP'], 'the form choice is authoritative for component labels once it changes');
  assert.deepEqual(plan.labelsToAdd, ['component:docs'], 'the new choice is applied');
});

test('clearing the component removes its label and asks for triage', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = formBody('Other / not sure');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['component:package'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['component:package'], 'the old component no longer applies');
  assert.deepEqual(plan.labelsToAdd, ['needs-triage'], 'nothing routes the issue now');
});

test('parsing a hostile body stays fast', () => {
  // Arrange
  const hostile = [`# a${' '.repeat(60000)}!`, `- [x] a${' '.repeat(60000)}b`, `${'#'.repeat(6)} ${'#'.repeat(60000)}x`].join('\n');
  const started = process.hrtime.bigint();
  // Act
  routing.extractFieldValues(`${hostile}\n### Component\n\nPackages`, 'Component');
  // Assert
  const ms = Number(process.hrtime.bigint() - started) / 1e6;
  assert.ok(ms < 500, `took ${ms} ms; the patterns must not backtrack on long whitespace runs`);
});

test('parses an ATX heading with a closing sequence', () => {
  // Arrange
  const body = '### Component ###\n\nPackages';
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['Packages'], 'trailing #s are markdown syntax, not part of the heading');
});

test('rejects team owners, duplicate options and missing labels', () => {
  // Arrange
  const broken = {
    fieldLabel: 'Component',
    triageLabel: { name: 'needs-triage' },
    components: [
      { id: 'a', option: 'A', label: 'x', owners: ['@org/team'] },
      { id: 'b', option: 'a', label: 'y', owners: [] },
      { id: 'c', option: 'C', owners: [] },
    ],
  };
  // Act
  const act = () => routing.validateConfig(broken);
  // Assert
  assert.throws(act, /teams cannot be assignees[\s\S]*duplicate option[\s\S]*"label" is required/, 'every problem is reported at once');
});

test('the committed component-owners.json is valid', () => {
  // Arrange / Act
  const act = () => routing.loadConfig(configPath);
  // Assert
  assert.doesNotThrow(act, 'the workflow fails on an invalid map, so it must never be committed broken');
});

// Reads the options of the dropdown with `id: component` from our own issue-form YAML. Deliberately
// minimal (no YAML dependency): it relies on the layout of the committed templates, and fails loudly
// rather than passing when that layout changes.
function componentField(yaml) {
  const lines = yaml.split(/\r?\n/);
  const start = lines.findIndex(l => /^\s*id:\s*component\s*$/.test(l));
  assert.notEqual(start, -1, 'template has a dropdown with id: component');
  const labelLine = lines.find((l, i) => i > start && /^\s*label:/.test(l));
  const label = labelLine.replace(/^\s*label:\s*/, '').trim().replace(/^"(.*)"$/, '$1').replace(/^'(.*)'$/, '$1');
  const optionsAt = lines.findIndex((l, i) => i > start && /^\s*options:\s*$/.test(l));
  assert.notEqual(optionsAt, -1, 'the component dropdown has an options list');
  const indent = lines[optionsAt + 1].match(/^\s*/)[0];
  const options = [];
  for (let i = optionsAt + 1; i < lines.length && lines[i].startsWith(`${indent}- `); i++) {
    options.push(lines[i].slice(indent.length + 2).trim().replace(/^"(.*)"$/, '$1').replace(/^'(.*)'$/, '$1'));
  }
  return { label, options };
}

test('every issue form has the Component field and exactly the components in component-owners.json', () => {
  // Arrange
  const committed = routing.loadConfig(configPath);
  const expected = committed.components.map(c => c.option);
  const templates = fs.readdirSync(templateDir).filter(f => /\.ya?ml$/.test(f) && f !== 'config.yml');
  assert.ok(templates.length > 0, 'there are issue forms to check');
  for (const file of templates) {
    // Act
    const { label, options } = componentField(fs.readFileSync(path.join(templateDir, file), 'utf8'));
    // Assert
    assert.equal(label, committed.fieldLabel, `${file}: the dropdown label is the "### <label>" heading the router looks for`);
    assert.deepEqual(options, expected, `${file}: dropdown options must equal the map's "option" values, or selections route to needs-triage`);
  }
});

function fixtureConfigPath() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'issue-routing-'));
  const file = path.join(dir, 'component-owners.json');
  fs.writeFileSync(file, JSON.stringify(config));
  return file;
}

function fakeGitHub({ assignable = [], existingLabels = [], live = null, dropAssignee = false, createStatus = null } = {}) {
  const calls = [];
  const failure = status => Object.assign(new Error(`HTTP ${status}`), { status });
  const issues = {
    get: async () => { calls.push(['get']); if (!live) throw failure(500); return { data: live }; },
    getLabel: async ({ name }) => { calls.push(['getLabel', name]); if (!existingLabels.includes(name)) throw failure(404); },
    createLabel: async ({ name }) => { calls.push(['createLabel', name]); if (createStatus) throw failure(createStatus); },
    checkUserCanBeAssigned: async ({ assignee }) => { calls.push(['check', assignee]); if (!assignable.includes(assignee)) throw failure(404); },
    addAssignees: async ({ assignees }) => {
      calls.push(['addAssignees', ...assignees]);
      return { data: { assignees: dropAssignee ? [] : assignees.map(login => ({ login })) } };
    },
    addLabels: async ({ labels }) => { calls.push(['addLabels', ...labels]); },
    removeLabel: async ({ name }) => { calls.push(['removeLabel', name]); },
  };
  return { github: { rest: { issues } }, calls };
}

function fakeCore() {
  const warnings = [];
  const summary = { addHeading() { return this; }, addTable() { return this; }, async write() {} };
  return { core: { info() {}, warning: m => warnings.push(m), summary }, warnings };
}

function eventContext(body, { action = 'opened', changes, labels = ['bug'], assignees = [] } = {}) {
  return {
    repo: { owner: 'o', repo: 'r' },
    payload: {
      action,
      changes,
      issue: { number: 7, body, labels: labels.map(name => ({ name })), assignees: assignees.map(login => ({ login })) },
    },
  };
}

const only = (calls, name) => calls.filter(c => c[0] === name);

test('run assigns the first assignable owner and adds the component label', async () => {
  // Arrange
  const { github, calls } = fakeGitHub({ assignable: ['bob'], existingLabels: ['MCP'] });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('MCP server (tools, prompts)')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'addAssignees'), [['addAssignees', 'bob']], 'exactly one owner is assigned: the first one who can be');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'MCP']], 'the component label is applied');
  assert.equal(warnings.length, 2, 'the failed re-read and the non-assignable first owner are both reported as warnings');
});

test('run plans from the live issue, not the stale event payload', async () => {
  // Arrange: the "opened" run already labelled and assigned before this edit run started.
  const body = formBody('Packages');
  const live = { number: 7, body, labels: [{ name: 'MCP' }], assignees: [{ login: 'alice' }] };
  const { github, calls } = fakeGitHub({ assignable: ['carol'], existingLabels: ['component:package'], live });
  const { core } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: formBody('MCP server (tools, prompts)') } }, labels: [] });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'removeLabel'), [['removeLabel', 'MCP']], 'the label added after the event fired is still removed');
  assert.deepEqual(only(calls, 'addAssignees'), [], 'the live assignee blocks a second assignee');
});

test('run falls back to needs-triage and does not throw when no owner is assignable', async () => {
  // Arrange
  const { github, calls } = fakeGitHub({ assignable: [] });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('Packages')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.equal(only(calls, 'addAssignees').length, 0, 'nobody is assigned');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:package', 'needs-triage']], 'the issue still gets its component label and is flagged for triage');
  assert.ok(calls.some(c => c[0] === 'createLabel' && c[1] === 'needs-triage'), 'a missing label is created instead of failing');
  assert.ok(warnings.some(w => w.includes('cannot be assigned')), 'the access problem is logged, not thrown');
});

test('run keeps needs-triage instead of removing and re-adding it when the new owner is not assignable', async () => {
  // Arrange
  const body = formBody('Packages');
  const live = { number: 7, body, labels: [{ name: 'needs-triage' }], assignees: [] };
  const { github, calls } = fakeGitHub({ assignable: [], existingLabels: ['component:package'], live });
  const { core } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: formBody('Other / not sure') } } });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'removeLabel'), [], 'the issue still needs a human, so needs-triage stays');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:package']], 'only the component label is added');
});

test('run treats an assignee the API silently dropped as not assigned', async () => {
  // Arrange
  const { github, calls } = fakeGitHub({ assignable: ['carol'], existingLabels: ['component:package', 'needs-triage'], dropAssignee: true });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('Packages')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.ok(warnings.some(w => w.includes('did not assign')), 'a 201 without the login is reported');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:package', 'needs-triage']], 'the unassigned issue is flagged for triage');
});

test('run tolerates a label created concurrently (422)', async () => {
  // Arrange
  const { github, calls } = fakeGitHub({ assignable: ['carol'], createStatus: 422 });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('Packages')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:package']], 'a 422 means the label exists now, so it is still applied');
  assert.ok(!warnings.some(w => w.includes('Could not create label')), 'the race is not reported as a failure');
});

test('run ignores an edit that changed only the title', async () => {
  // Arrange
  const { github, calls } = fakeGitHub();
  const { core } = fakeCore();
  const context = eventContext(formBody('Workspaces'), { action: 'edited', changes: { title: { from: 'old' } } });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(calls, [], 'routing depends only on the body');
});
