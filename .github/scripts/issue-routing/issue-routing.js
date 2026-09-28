'use strict';

// Routes a new or edited issue to a component label and owner, from the "Component" dropdown of
// the issue forms in .github/ISSUE_TEMPLATE and the map in .github/component-owners.json.
// Called by .github/workflows/issue-routing.yml through actions/github-script; the pure functions
// are exported separately so issue-routing.test.js can cover them with `node --test`.
// See .github/ISSUE_ROUTING.md for the behaviour contract.

const fs = require('node:fs');

// What GitHub writes into the body for an optional field the author left empty.
const NO_RESPONSE = '_No response_';

// The body is attacker-controlled (up to 65,536 chars). Headings and checkboxes are recognised by
// plain character scanning instead of regular expressions, and over-long lines are never treated
// as structure, so parsing stays linear in the body length.
const MAX_STRUCTURAL_LINE = 1000;
const LOGIN = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;

function normalize(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim().toLowerCase();
}

function isBlank(ch) {
  return ch === ' ' || ch === '\t';
}

function skipBlanks(text, from) {
  let i = from;
  while (i < text.length && isBlank(text[i])) i++;
  return i;
}

// Drops an ATX closing sequence: "Component ###" -> "Component". Hashes glued to the text
// ("C#") are content, not a closing sequence.
function stripClosingHashes(text) {
  let end = text.length;
  while (end > 0 && text[end - 1] === '#') end--;
  if (end === text.length) return text;
  if (end === 0) return '';
  return isBlank(text[end - 1]) ? text.slice(0, end).trimEnd() : text;
}

/** Returns the text of a markdown ATX heading line ("### Component"), or null. */
function headingText(line) {
  if (line.length > MAX_STRUCTURAL_LINE) return null;
  let i = 0;
  while (i < 3 && line[i] === ' ') i++;
  const hashes = i;
  while (line[i] === '#' && i - hashes < 7) i++;
  const level = i - hashes;
  if (level < 1 || level > 6 || !isBlank(line[i])) return null;
  return stripClosingHashes(line.slice(i).trim()) || null;
}

/** Parses a trimmed task-list line ("- [x] Packages"), or returns null. */
function checkboxItem(line) {
  if (line.length > MAX_STRUCTURAL_LINE || (line[0] !== '-' && line[0] !== '*')) return null;
  const open = skipBlanks(line, 1);
  if (open === 1 || line[open] !== '[' || line[open + 2] !== ']') return null;
  const mark = line[open + 1];
  if (mark !== ' ' && mark !== 'x' && mark !== 'X') return null;
  const textStart = skipBlanks(line, open + 3);
  if (textStart === open + 3) return null;
  return { checked: mark !== ' ', text: line.slice(textStart).trim() };
}

/**
 * Splits an issue-form body into `heading -> raw section text`. Headings are matched
 * case-insensitively; the FIRST occurrence wins, so a heading the author typed inside a later
 * textarea cannot override a form field that appears earlier in the form.
 */
function parseIssueFormSections(body) {
  const sections = new Map();
  let current = null;
  let buffer = [];
  const flush = () => {
    if (current !== null && !sections.has(current)) {
      sections.set(current, buffer.join('\n').trim());
    }
  };
  for (const line of String(body ?? '').split(/\r?\n/)) {
    const heading = headingText(line);
    if (heading !== null) {
      flush();
      current = normalize(heading);
      buffer = [];
    } else if (current !== null) {
      buffer.push(line);
    }
  }
  flush();
  return sections;
}

/**
 * Returns the selected value(s) of the form field labelled `fieldLabel`, or an empty array when the
 * field is absent or was left empty. Handles a single-select dropdown (the whole answer is one
 * value; option texts may contain commas, so it is NOT split) and a checkboxes field (only ticked
 * items).
 */
function extractFieldValues(body, fieldLabel) {
  const raw = parseIssueFormSections(body).get(normalize(fieldLabel));
  if (!raw || raw === NO_RESPONSE) {
    return [];
  }
  const lines = raw.split('\n').map(l => l.trim()).filter(Boolean);
  const checkboxes = lines.map(checkboxItem).filter(Boolean);
  if (checkboxes.length > 0) {
    return checkboxes.filter(c => c.checked && c.text).map(c => c.text);
  }
  const value = lines.filter(l => l !== NO_RESPONSE).join(' ').trim();
  return value ? [value] : [];
}

/** Finds the component whose `option` text or `id` equals `value` (whitespace/case-insensitive). */
function resolveComponent(value, config) {
  const wanted = normalize(value);
  if (!wanted) {
    return null;
  }
  return config.components.find(c => normalize(c.option) === wanted || normalize(c.id) === wanted) ?? null;
}

function describeComponent(component, index) {
  const suffix = component?.id ? ' (' + component.id + ')' : '';
  return `components[${index}]${suffix}`;
}

function checkUnique(errors, seen, where, key, value, hint = '') {
  if (!value) {
    errors.push(`${where}: "${key}" is required${hint}`);
  } else if (seen.has(normalize(value))) {
    errors.push(`${where}: duplicate ${key} "${value}"`);
  } else {
    seen.add(normalize(value));
  }
}

function checkOwners(errors, where, owners) {
  if (!Array.isArray(owners)) {
    errors.push(`${where}: "owners" must be an array (use [] for label-only routing)`);
    return;
  }
  for (const owner of owners) {
    if (!LOGIN.test(String(owner).replace(/^@/, ''))) {
      errors.push(`${where}: owner "${owner}" is not a GitHub user login (teams cannot be assignees)`);
    }
  }
}

function checkComponent(errors, seen, component, index) {
  const where = describeComponent(component, index);
  checkUnique(errors, seen.id, where, 'id', component?.id);
  checkUnique(errors, seen.option, where, 'option', component?.option);
  if (component?.triage) return;
  checkUnique(errors, seen.label, where, 'label', component?.label, ' unless "triage": true');
  checkOwners(errors, where, component?.owners);
}

/** Throws with every problem found; a broken map must fail loudly, not route silently. */
function validateConfig(config) {
  if (!config || !Array.isArray(config.components)) {
    throw new Error('component-owners.json: "components" must be an array');
  }
  const errors = [];
  if (!config.fieldLabel) errors.push('"fieldLabel" is required');
  if (!config.triageLabel?.name) errors.push('"triageLabel.name" is required');
  const seen = { id: new Set(), option: new Set(), label: new Set() };
  config.components.forEach((component, index) => checkComponent(errors, seen, component, index));
  if (errors.length > 0) {
    throw new Error(`component-owners.json is invalid:\n- ${errors.join('\n- ')}`);
  }
  return config;
}

function loadConfig(path) {
  return validateConfig(JSON.parse(fs.readFileSync(path, 'utf8')));
}

/**
 * Resolves what the body routes to.
 * status: 'resolved' | 'missing' (no value) | 'unknown' (value not in the map) | 'triage' (the
 * "not sure" option).
 */
function classify(body, config) {
  const values = extractFieldValues(body, config.fieldLabel);
  if (values.length === 0) {
    return { status: 'missing', values, component: null };
  }
  const components = values.map(v => resolveComponent(v, config));
  const routable = components.find(c => c && !c.triage);
  if (routable) {
    return { status: 'resolved', values, component: routable };
  }
  if (components.some(c => c?.triage)) {
    return { status: 'triage', values, component: null };
  }
  return { status: 'unknown', values, component: null };
}

function routingKey(result) {
  return result.component ? `component:${result.component.id}` : 'unrouted';
}

function componentLabelChanges({ chosen, isEdit, present, config }) {
  const add = [];
  const remove = [];
  const componentLabels = config.components.filter(c => c.label).map(c => normalize(c.label));
  if (isEdit || chosen) {
    for (const label of componentLabels) {
      if (label !== normalize(chosen) && present.has(label)) remove.push(present.get(label));
    }
  }
  if (chosen && !present.has(normalize(chosen))) add.push(chosen);
  const keepsComponentLabel = componentLabels.some(l => present.has(l) && !remove.includes(present.get(l)));
  return { add, remove, keepsComponentLabel };
}

function triageLabelChanges({ chosen, keepsComponentLabel, present, config }) {
  const triage = normalize(config.triageLabel.name);
  if (chosen) {
    return present.has(triage) ? { add: [], remove: [present.get(triage)] } : { add: [], remove: [] };
  }
  const needsTriage = !keepsComponentLabel && !present.has(triage);
  return { add: needsTriage ? [config.triageLabel.name] : [], remove: [] };
}

/**
 * Computes the label and assignee changes for one event. Pure: no API calls.
 *
 * - `previousBody` is `changes.body.from` of an `edited` event, `undefined` for `opened`. An edit
 *   that does not change the routing (same component, or still unrouted) is a no-op, so a text
 *   edit never re-adds a label or an owner a human removed.
 * - When the component changes, the form choice is authoritative for component labels: every
 *   other component label is removed, whatever put it there. Using the CURRENT labels instead of
 *   only the previous choice keeps this right when GitHub collapsed queued runs of several edits.
 * - On `opened`, the triage label is added only while the issue has no component label.
 * - Owners are proposed only when the issue has NO assignee. An assignee is the claim signal of
 *   the claim-clio-issue skill, so an existing one (human or earlier routing) is never changed.
 */
function planRouting({ body, previousBody, currentLabels = [], currentAssignees = [], config }) {
  const current = classify(body, config);
  const base = {
    status: current.status,
    values: current.values,
    component: current.component,
    labelsToAdd: [],
    labelsToRemove: [],
    ownerCandidates: [],
    alreadyAssigned: currentAssignees.length > 0,
    unchanged: false,
  };
  const isEdit = previousBody !== undefined;
  if (isEdit && routingKey(classify(previousBody, config)) === routingKey(current)) {
    return { ...base, unchanged: true };
  }

  const present = new Map(currentLabels.map(l => [normalize(l), l]));
  const chosen = current.status === 'resolved' ? current.component.label : null;
  const components = componentLabelChanges({ chosen, isEdit, present, config });
  const triage = triageLabelChanges({ chosen, keepsComponentLabel: components.keepsComponentLabel, present, config });
  const owners = chosen && currentAssignees.length === 0
    ? current.component.owners.map(o => String(o).replace(/^@/, ''))
    : [];

  return {
    ...base,
    labelsToAdd: [...components.add, ...triage.add],
    labelsToRemove: [...components.remove, ...triage.remove],
    ownerCandidates: owners,
  };
}

function labelSpec(name, config) {
  if (normalize(name) === normalize(config.triageLabel.name)) {
    return config.triageLabel;
  }
  const component = config.components.find(c => c.label && normalize(c.label) === normalize(name));
  return { name, color: component?.color, description: component?.description };
}

// ---- GitHub side effects. Every call degrades to a warning: routing is a convenience and must
// ---- never turn the issue event red or block issue creation.

async function ensureLabel(github, core, repo, spec) {
  try {
    await github.rest.issues.getLabel({ ...repo, name: spec.name });
    return true;
  } catch (error) {
    if (error.status !== 404) {
      core.warning(`Could not read label "${spec.name}": ${error.message}`);
      return false;
    }
  }
  try {
    await github.rest.issues.createLabel({
      ...repo,
      name: spec.name,
      color: spec.color || 'ededed',
      description: (spec.description || '').slice(0, 100),
    });
    core.info(`Created missing label "${spec.name}".`);
    return true;
  } catch (error) {
    // 422 = someone (or a concurrent run) created it in between; that is fine.
    if (error.status === 422) return true;
    core.warning(`Could not create label "${spec.name}": ${error.message}`);
    return false;
  }
}

async function isAssignable(github, core, repo, login) {
  try {
    await github.rest.issues.checkUserCanBeAssigned({ ...repo, assignee: login });
    return true;
  } catch (error) {
    if (error.status === 404) {
      core.warning(`Owner "${login}" cannot be assigned in ${repo.owner}/${repo.repo} (not a collaborator with access?). Fix .github/component-owners.json.`);
    } else {
      core.warning(`Could not check whether "${login}" is assignable: ${error.message}`);
    }
    return false;
  }
}

// The payload is a snapshot from when the event fired; an earlier run for the same issue may have
// changed labels/assignees since. Plan from the live issue, or a quick open-then-edit ends with
// two component labels and two assignees.
async function readLiveIssue(github, core, repo, issue) {
  try {
    const { data } = await github.rest.issues.get({ ...repo, issue_number: issue.number });
    return data;
  } catch (error) {
    core.warning(`Could not re-read issue #${issue.number}, using the event payload: ${error.message}`);
    return issue;
  }
}

async function firstAssignable(github, core, repo, candidates) {
  for (const login of candidates) {
    if (await isAssignable(github, core, repo, login)) return login;
  }
  return null;
}

/** Assigns the first assignable candidate; returns the login, or null when nobody was assigned. */
async function assignOwner(github, core, repo, issueNumber, candidates) {
  const login = await firstAssignable(github, core, repo, candidates);
  if (!login) return null;
  try {
    const { data } = await github.rest.issues.addAssignees({ ...repo, issue_number: issueNumber, assignees: [login] });
    // The API answers 201 and silently drops a login it cannot assign; verify.
    if ((data.assignees || []).some(a => normalize(a.login) === normalize(login))) return login;
    core.warning(`GitHub accepted the request but did not assign "${login}".`);
  } catch (error) {
    core.warning(`Could not assign "${login}": ${error.message}`);
  }
  return null;
}

// A routed issue nobody could be assigned to still needs a human: keep or add the triage label.
function keepForTriage(labels, config) {
  const triage = normalize(config.triageLabel.name);
  const isTriage = l => normalize(l) === triage;
  if (labels.remove.some(isTriage)) {
    return { add: labels.add, remove: labels.remove.filter(l => !isTriage(l)) };
  }
  if (labels.add.some(isTriage)) return labels;
  return { add: [...labels.add, config.triageLabel.name], remove: labels.remove };
}

async function applyLabels(github, core, repo, issueNumber, labels, config) {
  for (const name of labels.remove) {
    try {
      await github.rest.issues.removeLabel({ ...repo, issue_number: issueNumber, name });
    } catch (error) {
      if (error.status !== 404) core.warning(`Could not remove label "${name}": ${error.message}`);
    }
  }
  const ready = [];
  for (const name of labels.add) {
    if (await ensureLabel(github, core, repo, labelSpec(name, config))) ready.push(name);
  }
  if (ready.length === 0) return ready;
  try {
    await github.rest.issues.addLabels({ ...repo, issue_number: issueNumber, labels: ready });
  } catch (error) {
    core.warning(`Could not add labels ${JSON.stringify(ready)}: ${error.message}`);
  }
  return ready;
}

function describePlan(issueNumber, plan) {
  const component = plan.component ? ' (' + plan.component.id + ')' : '';
  return `Issue #${issueNumber}: component field = ${JSON.stringify(plan.values)} -> ${plan.status}${component}`;
}

function skipReason(payload) {
  const issue = payload.issue;
  if (!issue || issue.pull_request) return 'Not an issue event; nothing to route.';
  if (payload.action === 'edited' && payload.changes?.body === undefined) {
    return 'Only the title changed; routing depends on the body. Skipping.';
  }
  return null;
}

async function writeSummary(core, issueNumber, plan, outcome) {
  let assignee = outcome.assigned || '-';
  if (!outcome.assigned && plan.alreadyAssigned) assignee = '(unchanged, already assigned)';
  await core.summary
    .addHeading(`Issue #${issueNumber} routing`, 3)
    .addTable([
      [{ data: 'Field', header: true }, { data: 'Value', header: true }],
      ['Component value', plan.values.join(', ') || '(none)'],
      ['Status', plan.status],
      ['Labels added', outcome.added.join(', ') || '-'],
      ['Labels removed', outcome.removed.join(', ') || '-'],
      ['Assignee', assignee],
    ])
    .write();
}

/**
 * Entry point for actions/github-script. Assigns at most ONE owner: the first assignable login in
 * the component's `owners` list. Several assignees would read as an ambiguous claim to the
 * claim-clio-issue skill, which stops on multiple assignees.
 */
async function run({ github, context, core, configPath }) {
  const payload = context.payload;
  const skip = skipReason(payload);
  if (skip) {
    core.info(skip);
    return;
  }

  const config = loadConfig(configPath);
  const repo = context.repo;
  const issueNumber = payload.issue.number;
  const live = await readLiveIssue(github, core, repo, payload.issue);
  const plan = planRouting({
    body: live.body,
    previousBody: payload.action === 'edited' ? payload.changes.body.from ?? '' : undefined,
    currentLabels: (live.labels || []).map(l => (typeof l === 'string' ? l : l.name)),
    currentAssignees: (live.assignees || []).map(a => a.login),
    config,
  });

  core.info(describePlan(issueNumber, plan));
  if (plan.unchanged) {
    core.info('The edit did not change the component; leaving labels and assignees as they are.');
    return;
  }
  if (plan.status === 'unknown') {
    core.warning(`Component value ${JSON.stringify(plan.values)} is not in .github/component-owners.json; the issue form and the map are out of sync.`);
  }

  let labels = { add: plan.labelsToAdd, remove: plan.labelsToRemove };
  let assigned = null;
  if (plan.ownerCandidates.length > 0) {
    assigned = await assignOwner(github, core, repo, issueNumber, plan.ownerCandidates);
    if (!assigned) {
      core.warning(`No owner of "${plan.component.id}" could be assigned; marking the issue for triage.`);
      labels = keepForTriage(labels, config);
    }
  } else if (plan.status === 'resolved' && plan.alreadyAssigned) {
    core.info('Issue already has an assignee; leaving assignment unchanged.');
  }

  const added = await applyLabels(github, core, repo, issueNumber, labels, config);
  await writeSummary(core, issueNumber, plan, { assigned, added, removed: labels.remove });
}

module.exports = {
  NO_RESPONSE,
  parseIssueFormSections,
  extractFieldValues,
  resolveComponent,
  validateConfig,
  loadConfig,
  classify,
  planRouting,
  run,
};
