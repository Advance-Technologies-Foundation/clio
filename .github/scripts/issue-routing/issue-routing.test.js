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
  componentLabelPrefix: 'component:',
  ownerNotification: 'assign',
  triageLabel: { name: 'needs-triage' },
  components: [
    { id: 'ring', option: 'ClioRing', label: 'component:ring', owners: ['erin', 'frank'], ownerNotification: 'mention' },
    { id: 'mcp-server', option: 'MCP server (tools, prompts)', label: 'component:mcp', owners: ['alice', 'bob'] },
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
  assert.deepEqual(plan.labelsToAdd, ['component:mcp'], 'the component label is added');
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
  assert.deepEqual(plan.labelsToAdd, ['component:package'], 'the label is still routed');
  assert.equal(plan.alreadyAssigned, true, 'the run reports why it did not assign');
});

test('keeps needs-triage on a component that has no owners', () => {
  // Arrange
  const body = formBody('Documentation');
  // Act
  const opened = routing.planRouting({ body, currentLabels: ['bug'], config });
  const edited = routing.planRouting({ body, previousBody: formBody('Other / not sure'), currentLabels: ['needs-triage'], config });
  // Assert
  assert.deepEqual(opened.labelsToAdd, ['component:docs', 'needs-triage'], 'nobody owns docs, so a human still has to pick the issue up');
  assert.deepEqual(edited.labelsToRemove, [], 'needs-triage is not removed when nobody can be assigned');
  assert.deepEqual(edited.ownerCandidates, [], 'there is nobody to assign');
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

test('keeps a component label a human triager set on an unrouted issue', () => {
  // Arrange
  const blank = 'free text issue';
  const notSure = formBody('Other / not sure');
  // Act
  const onBlank = routing.planRouting({ body: `${blank} more`, previousBody: blank, currentLabels: ['component:docs'], config });
  const onNotSure = routing.planRouting({ body: `${notSure}\nmore`, previousBody: notSure, currentLabels: ['component:docs'], config });
  // Assert
  assert.equal(onBlank.unchanged, true, 'a blank issue has no form choice to enforce');
  assert.equal(onNotSure.unchanged, true, 'the author did not change the "not sure" choice, so the triager label stays');
});

test('swaps the component label when an edit changes the component', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = formBody('Documentation');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['bug', 'component:package'], currentAssignees: ['carol'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['component:package'], 'the label of the old choice is removed');
  assert.deepEqual(plan.labelsToAdd, ['component:docs', 'needs-triage'], 'the new choice is applied; docs has no owner');
  assert.deepEqual(plan.ownerCandidates, [], 'the existing assignee is kept; reassignment is a human decision');
});

test('never touches topic labels outside the component prefix', () => {
  // Arrange: MCP, Guidance and ring are human topic labels, not routing labels.
  const previousBody = formBody('Packages');
  const body = formBody('MCP server (tools, prompts)');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['MCP', 'Guidance', 'ring', 'component:package'], currentAssignees: ['carol'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['component:package'], 'only the routing-owned label is removed');
  assert.deepEqual(plan.labelsToAdd, ['component:mcp'], 'the new component label is added');
});

test('removes the triage label once an owned component is chosen', () => {
  // Arrange
  const previousBody = formBody('Other / not sure');
  const body = formBody('Packages');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['needs-triage'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['needs-triage'], 'the issue is routed to an owner now');
  assert.deepEqual(plan.labelsToAdd, ['component:package'], 'the chosen component label is added');
  assert.deepEqual(plan.ownerCandidates, ['carol'], 'the owner is proposed');
});

test('a text-only edit is a no-op, so an owner a human unassigned is not re-assigned', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = `${previousBody}\n\nMore details.`;
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['bug', 'component:package'], currentAssignees: [], config });
  // Assert
  assert.equal(plan.unchanged, true, 'the live labels already match the component');
  assert.deepEqual([plan.labelsToAdd, plan.labelsToRemove, plan.ownerCandidates], [[], [], []], 'nothing is re-applied');
});

test('reconciles from live labels when GitHub cancelled the run of the edit that changed the component', () => {
  // Arrange: A was applied, the A->B run was cancelled, this run is a later B->B text edit.
  const previousBody = formBody('Packages');
  const body = `${previousBody}\n\ntypo fix`;
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['component:mcp', 'bug'], currentAssignees: ['alice'], config });
  // Assert
  assert.equal(plan.unchanged, false, 'the live label disagrees with the form, so this is not a no-op');
  assert.deepEqual(plan.labelsToRemove, ['component:mcp'], 'the stale label from the cancelled run is removed');
  assert.deepEqual(plan.labelsToAdd, ['component:package'], 'the current choice is applied');
  assert.deepEqual(plan.ownerCandidates, [], 'the existing assignee is kept');
});

test('switching to "not sure" removes the component label and asks for triage', () => {
  // Arrange
  const previousBody = formBody('Packages');
  const body = formBody('Other / not sure');
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['component:package'], config });
  // Assert
  assert.deepEqual(plan.labelsToRemove, ['component:package'], 'the author withdrew the component');
  assert.deepEqual(plan.labelsToAdd, ['needs-triage'], 'nothing routes the issue now');
});

test('mention mode lists every owner and assigns nobody', () => {
  // Arrange
  const body = formBody('ClioRing');
  // Act
  const plan = routing.planRouting({ body, currentLabels: ['needs-triage'], currentAssignees: [], config });
  // Assert
  assert.deepEqual(plan.ownerCandidates, [], 'the assignee stays free for whoever claims the issue');
  assert.deepEqual(plan.ownersToMention, ['erin', 'frank'], 'all owners are notified');
  assert.deepEqual(plan.labelsToAdd, ['component:ring'], 'the label is routed');
  assert.deepEqual(plan.labelsToRemove, ['needs-triage'], 'the owners were told, so it is not waiting for triage');
});

test('mention mode does not mention again on a text edit', () => {
  // Arrange
  const previousBody = formBody('ClioRing');
  const body = `${previousBody}\nmore`;
  // Act
  const plan = routing.planRouting({ body, previousBody, currentLabels: ['component:ring'], config });
  // Assert
  assert.equal(plan.unchanged, true, 'routing did not change, so the owners are not pinged again');
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

test('recognises only real ATX headings', () => {
  // Arrange
  const cases = [
    ['#Component\n\nPackages', []],
    ['####### Component\n\nPackages', []],
    ['    ### Component\n\nPackages', []],
    ['   ### Component\n\nPackages', ['Packages']],
    ['###\tComponent\n\nPackages', ['Packages']],
  ];
  for (const [body, expected] of cases) {
    // Act
    const values = routing.extractFieldValues(body, 'Component');
    // Assert
    assert.deepEqual(values, expected, `${JSON.stringify(body.split('\n')[0])}: CommonMark needs 1-6 #s, at most 3 leading spaces and a blank after the #s`);
  }
});

test('keeps hashes glued to the heading text', () => {
  // Arrange
  const sections = routing.parseIssueFormSections('### Language C#\n\nx');
  // Act
  const keys = [...sections.keys()];
  // Assert
  assert.deepEqual(keys, ['language c#'], 'a closing sequence must be separated by a blank; "C#" is content');
});

test('ignores malformed checkbox lines', () => {
  // Arrange
  const body = '### Component\n\n-[x] Packages\n- [y] Workspaces\n- [x]Docs';
  // Act
  const values = routing.extractFieldValues(body, 'Component');
  // Assert
  assert.deepEqual(values, ['-[x] Packages - [y] Workspaces - [x]Docs'], 'none of the lines is a task item, so the section is read as one plain value');
});

test('rejects team owners, labels outside the prefix, duplicate options and missing labels', () => {
  // Arrange
  const broken = {
    fieldLabel: 'Component',
    componentLabelPrefix: 'component:',
    ownerNotification: 'assign',
    triageLabel: { name: 'needs-triage' },
    components: [
      { id: 'a', option: 'A', label: 'component:x', owners: ['@org/team'], ownerNotification: 'ping' },
      { id: 'd', option: 'D', label: 'MCP', owners: [] },
      { id: 'b', option: 'a', label: 'component:y', owners: [] },
      { id: 'c', option: 'C', owners: [] },
    ],
  };
  // Act
  const act = () => routing.validateConfig(broken);
  // Assert
  assert.throws(act, /teams cannot be assignees[\s\S]*"ownerNotification" must be one of assign, mention[\s\S]*label "MCP" must start with "component:"[\s\S]*duplicate option[\s\S]*"label" is required/, 'every problem is reported at once, including a shared topic label that routing would strip');
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

function fakeGitHub({ assignable = [], existingLabels = [], live = null, dropAssignee = false, createStatus = null, addLabelsStatus = null, removeStatus = null, comments = [], getStatus = null, events = [], commentStatus = null } = {}) {
  const calls = [];
  const failure = status => Object.assign(new Error(`HTTP ${status}`), { status });
  const issues = {
    // Returns the issue of the last eventContext() unless a live state or a failure is given.
    get: async () => { calls.push(['get']); if (getStatus) throw failure(getStatus); return { data: live ?? lastContextIssue }; },
    listEvents: async () => { calls.push(['listEvents']); return { data: events }; },
    getLabel: async ({ name }) => { calls.push(['getLabel', name]); if (!existingLabels.includes(name)) throw failure(404); },
    createLabel: async ({ name }) => { calls.push(['createLabel', name]); if (createStatus) throw failure(createStatus); },
    checkUserCanBeAssigned: async ({ assignee }) => { calls.push(['check', assignee]); if (!assignable.includes(assignee)) throw failure(404); },
    addAssignees: async ({ assignees }) => {
      calls.push(['addAssignees', ...assignees]);
      return { data: { assignees: dropAssignee ? [] : assignees.map(login => ({ login })) } };
    },
    addLabels: async ({ labels }) => { calls.push(['addLabels', ...labels]); if (addLabelsStatus) throw failure(addLabelsStatus); },
    removeLabel: async ({ name }) => { calls.push(['removeLabel', name]); if (removeStatus) throw failure(removeStatus); },
  };
  issues.listComments = async () => { calls.push(['listComments']); return { data: comments }; };
  issues.createComment = async ({ body }) => { calls.push(['createComment', body]); if (commentStatus) throw failure(commentStatus); };
  issues.updateComment = async ({ comment_id: id, body }) => { calls.push(['updateComment', id, body]); };
  const paginate = async (method, params) => (await method(params)).data;
  return { github: { rest: { issues }, paginate }, calls };
}

function fakeCore() {
  const warnings = [];
  const tables = [];
  const summary = { addHeading() { return this; }, addTable(rows) { tables.push(rows); return this; }, async write() {} };
  return { core: { info() {}, warning: m => warnings.push(m), summary }, warnings, tables };
}

let lastContextIssue = null;

function eventContext(body, { action = 'opened', changes, labels = ['bug'], assignees = [] } = {}) {
  lastContextIssue = { number: 7, body, labels: labels.map(name => ({ name })), assignees: assignees.map(login => ({ login })) };
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
  const { github, calls } = fakeGitHub({ assignable: ['bob'], existingLabels: ['component:mcp'] });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('MCP server (tools, prompts)')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'addAssignees'), [['addAssignees', 'bob']], 'exactly one owner is assigned: the first one who can be');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:mcp']], 'the component label is applied');
  assert.equal(warnings.length, 1, 'the non-assignable first owner is reported as a warning');
});

test('run plans from the live issue, not the stale event payload', async () => {
  // Arrange: the "opened" run already labelled and assigned before this edit run started.
  const body = formBody('Packages');
  const live = { number: 7, body, labels: [{ name: 'component:mcp' }], assignees: [{ login: 'alice' }] };
  const { github, calls } = fakeGitHub({ assignable: ['carol'], existingLabels: ['component:package'], live });
  const { core } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: formBody('MCP server (tools, prompts)') } }, labels: [] });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'removeLabel'), [['removeLabel', 'component:mcp']], 'the label added after the event fired is still removed');
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

test('run reports only the label changes GitHub accepted', async () => {
  // Arrange
  const body = formBody('Packages');
  const live = { number: 7, body, labels: [{ name: 'component:mcp' }], assignees: [{ login: 'alice' }] };
  const { github } = fakeGitHub({ existingLabels: ['component:package'], live, addLabelsStatus: 500, removeStatus: 500 });
  const { core, tables } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: formBody('MCP server (tools, prompts)') } } });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  const rows = Object.fromEntries(tables[0].slice(1));
  assert.equal(rows['Labels added'], '-', 'a failed addLabels call must not be reported as added');
  assert.equal(rows['Labels removed'], '-', 'a failed non-404 removal must not be reported as removed');
});

test('run mentions owners in one routing comment and updates it on a later change', async () => {
  // Arrange
  const first = fakeGitHub({ existingLabels: ['component:ring'] });
  const existing = [{ id: 42, body: '<!-- issue-routing -->\nold', user: { login: 'github-actions[bot]' } }];
  const second = fakeGitHub({ existingLabels: ['component:ring'], comments: existing });
  const { core } = fakeCore();
  // Act
  await routing.run({ github: first.github, context: eventContext(formBody('ClioRing')), core, configPath: fixtureConfigPath() });
  await routing.run({ github: second.github, context: eventContext(formBody('ClioRing')), core, configPath: fixtureConfigPath() });
  // Assert
  const created = only(first.calls, 'createComment');
  assert.equal(created.length, 1, 'the first routing posts one comment');
  assert.match(created[0][1], /^<!-- issue-routing -->[\s\S]*@erin, @frank/, 'the comment carries the marker and mentions every owner');
  assert.equal(only(first.calls, 'addAssignees').length, 0, 'nobody is assigned in mention mode');
  assert.deepEqual(only(second.calls, 'updateComment').map(c => c[1]), [42], 'a later run updates the existing comment instead of adding one');
  assert.equal(only(second.calls, 'createComment').length, 0, 'no second comment is posted');
});

const ROUTING_BOT = { login: 'github-actions[bot]' };

test('run makes no changes when the live issue cannot be read', async () => {
  // Arrange: the payload predates an earlier run that already assigned alice.
  const { github, calls } = fakeGitHub({ assignable: ['carol'], getStatus: 503 });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('Packages')), core, configPath: fixtureConfigPath() });
  // Assert
  const writes = calls.filter(c => ['addAssignees', 'addLabels', 'removeLabel', 'createLabel', 'createComment', 'updateComment'].includes(c[0]));
  assert.deepEqual(writes, [], 'a stale payload plan could add a second assignee, so nothing is written');
  assert.ok(warnings.some(w => w.includes('skipping routing')), 'the skipped run is visible in the log');
});

test('run clears a routing-applied label after a collapsed switch to "not sure"', async () => {
  // Arrange: MCP was routed, the MCP -> Other run was cancelled, this is the Other -> Other text edit.
  const previous = formBody('Other / not sure');
  const body = `${previous}\nmore`;
  const live = { number: 7, body, labels: [{ name: 'component:mcp' }], assignees: [] };
  const events = [{ event: 'labeled', label: { name: 'component:mcp' }, actor: ROUTING_BOT }];
  const { github, calls } = fakeGitHub({ live, events, existingLabels: ['needs-triage'] });
  const { core } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: previous } } });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'removeLabel'), [['removeLabel', 'component:mcp']], 'the stale routing label is removed');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'needs-triage']], 'the reporter\'s "not sure" is honoured');
});

test('run keeps a component label a human applied to a "not sure" issue', async () => {
  // Arrange
  const previous = formBody('Other / not sure');
  const body = `${previous}\nmore`;
  const live = { number: 7, body, labels: [{ name: 'component:docs' }], assignees: [] };
  const events = [
    { event: 'labeled', label: { name: 'component:docs' }, actor: ROUTING_BOT },
    { event: 'unlabeled', label: { name: 'component:docs' }, actor: { login: 'dave' } },
    { event: 'labeled', label: { name: 'component:docs' }, actor: { login: 'dave' } },
  ];
  const { github, calls } = fakeGitHub({ live, events });
  const { core } = fakeCore();
  const context = eventContext(body, { action: 'edited', changes: { body: { from: previous } } });
  // Act
  await routing.run({ github, context, core, configPath: fixtureConfigPath() });
  // Assert
  assert.equal(only(calls, 'removeLabel').length, 0, 'the latest labeled event is a human triager, so the label stays');
  assert.equal(only(calls, 'addLabels').length, 0, 'a triaged issue does not need needs-triage');
});

test('run never overwrites a human comment that carries the routing marker', async () => {
  // Arrange
  const comments = [{ id: 9, body: '<!-- issue-routing --> copied by a person', user: { login: 'dave' } }];
  const { github, calls } = fakeGitHub({ existingLabels: ['component:ring'], comments });
  const { core } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(formBody('ClioRing')), core, configPath: fixtureConfigPath() });
  // Assert
  assert.equal(only(calls, 'updateComment').length, 0, 'the human comment is left untouched');
  assert.equal(only(calls, 'createComment').length, 1, 'routing posts its own comment instead');
});

test('run keeps needs-triage when the mention comment cannot be published', async () => {
  // Arrange
  const body = formBody('ClioRing');
  const live = { number: 7, body, labels: [{ name: 'needs-triage' }], assignees: [] };
  const { github, calls } = fakeGitHub({ live, existingLabels: ['component:ring'], commentStatus: 503 });
  const { core, warnings } = fakeCore();
  // Act
  await routing.run({ github, context: eventContext(body), core, configPath: fixtureConfigPath() });
  // Assert
  assert.deepEqual(only(calls, 'removeLabel'), [], 'nobody was told, so the issue stays visible in triage');
  assert.deepEqual(only(calls, 'addLabels'), [['addLabels', 'component:ring']], 'the component is still routed');
  assert.ok(warnings.some(w => w.includes('could not be mentioned')), 'the failure is logged');
});
