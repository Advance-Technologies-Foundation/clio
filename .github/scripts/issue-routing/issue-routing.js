'use strict';

// Routes a new or edited issue to a component label and owner, from the "Component" dropdown of
// the issue forms in .github/ISSUE_TEMPLATE and the map in .github/component-owners.json.
// Called by .github/workflows/issue-routing.yml through actions/github-script; the pure functions
// are exported separately so issue-routing.test.js can cover them with `node --test`.
// See .github/ISSUE_ROUTING.md for the behaviour contract.

const fs = require('fs');

// What GitHub writes into the body for an optional field the author left empty.
const NO_RESPONSE = '_No response_';

const HEADING = /^\s{0,3}#{1,6}\s+(.+?)\s*#*\s*$/;
const CHECKBOX = /^\s*[-*]\s+\[([ xX])\]\s+(.+?)\s*$/;
const LOGIN = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;

function normalize(text) {
  return String(text ?? '').replace(/\s+/g, ' ').trim().toLowerCase();
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
    const match = HEADING.exec(line);
    if (match) {
      flush();
      current = normalize(match[1]);
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
  const checkboxes = lines.map(l => CHECKBOX.exec(l)).filter(Boolean);
  if (checkboxes.length > 0) {
    return checkboxes.filter(m => m[1] !== ' ').map(m => m[2]);
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

/** Throws with every problem found; a broken map must fail loudly, not route silently. */
function validateConfig(config) {
  const errors = [];
  if (!config || !Array.isArray(config.components)) {
    throw new Error('component-owners.json: "components" must be an array');
  }
  if (!config.fieldLabel) errors.push('"fieldLabel" is required');
  if (!config.triageLabel?.name) errors.push('"triageLabel.name" is required');
  const seen = { id: new Set(), option: new Set(), label: new Set() };
  config.components.forEach((c, i) => {
    const where = `components[${i}]${c?.id ? ` (${c.id})` : ''}`;
    for (const key of ['id', 'option']) {
      if (!c?.[key]) {
        errors.push(`${where}: "${key}" is required`);
      } else if (seen[key].has(normalize(c[key]))) {
        errors.push(`${where}: duplicate ${key} "${c[key]}"`);
      } else {
        seen[key].add(normalize(c[key]));
      }
    }
    if (c?.triage) return;
    if (!c?.label) {
      errors.push(`${where}: "label" is required unless "triage": true`);
    } else if (seen.label.has(normalize(c.label))) {
      errors.push(`${where}: duplicate label "${c.label}"`);
    } else {
      seen.label.add(normalize(c.label));
    }
    if (!Array.isArray(c?.owners)) {
      errors.push(`${where}: "owners" must be an array (use [] for label-only routing)`);
    } else {
      for (const owner of c.owners) {
        const login = String(owner).replace(/^@/, '');
        if (!LOGIN.test(login)) {
          errors.push(`${where}: owner "${owner}" is not a GitHub user login (teams cannot be assignees)`);
        }
      }
    }
  });
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

/**
 * Computes the label and assignee changes for one event. Pure: no API calls.
 *
 * Only labels this workflow owns are touched, and only when the form choice itself changed:
 * - `previousBody` (from `changes.body.from` on an edit) tells which component label came from the
 *   old choice; a component label a human added by hand is never removed.
 * - The triage label is added only while the issue carries no component label at all.
 * - Owners are proposed only when the issue has NO assignee. An assignee is the claim signal of
 *   the clio-issue-workflow skill, so an existing one (human or earlier routing) is never changed.
 */
function planRouting({ body, previousBody, currentLabels = [], currentAssignees = [], config }) {
  const current = classify(body, config);
  const previous = previousBody === undefined ? null : classify(previousBody, config);
  const has = new Set(currentLabels.map(normalize));
  const componentLabels = new Set(config.components.filter(c => c.label).map(c => normalize(c.label)));
  const triage = config.triageLabel.name;

  const add = [];
  const remove = [];
  const result = new Set(has);

  const previousComponent = previous?.component ?? null;
  if (previousComponent && previousComponent.id !== current.component?.id && has.has(normalize(previousComponent.label))) {
    remove.push(previousComponent.label);
    result.delete(normalize(previousComponent.label));
  }

  if (current.status === 'resolved') {
    if (!result.has(normalize(current.component.label))) {
      add.push(current.component.label);
      result.add(normalize(current.component.label));
    }
    if (result.has(normalize(triage))) {
      remove.push(triage);
      result.delete(normalize(triage));
    }
  } else {
    const hasComponentLabel = [...result].some(l => componentLabels.has(l));
    if (!hasComponentLabel && !result.has(normalize(triage))) {
      add.push(triage);
    }
  }

  const owners = current.status === 'resolved' && currentAssignees.length === 0
    ? current.component.owners.map(o => String(o).replace(/^@/, ''))
    : [];

  return {
    status: current.status,
    values: current.values,
    component: current.component,
    labelsToAdd: add,
    labelsToRemove: remove,
    ownerCandidates: owners,
    alreadyAssigned: currentAssignees.length > 0,
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

/**
 * Entry point for actions/github-script. Assigns at most ONE owner: the first assignable login in
 * the component's `owners` list. Several assignees would read as an ambiguous claim to the
 * claim-clio-issue skill, which stops on multiple assignees.
 */
async function run({ github, context, core, configPath }) {
  const payload = context.payload;
  const issue = payload.issue;
  if (!issue || issue.pull_request) {
    core.info('Not an issue event; nothing to route.');
    return;
  }
  if (payload.action === 'edited' && payload.changes?.body === undefined) {
    core.info('Only the title changed; routing depends on the body. Skipping.');
    return;
  }

  const config = loadConfig(configPath);
  const repo = context.repo;
  const plan = planRouting({
    body: issue.body,
    previousBody: payload.action === 'edited' ? payload.changes.body.from : undefined,
    currentLabels: (issue.labels || []).map(l => (typeof l === 'string' ? l : l.name)),
    currentAssignees: (issue.assignees || []).map(a => a.login),
    config,
  });

  core.info(`Issue #${issue.number}: component field = ${JSON.stringify(plan.values)} -> ${plan.status}${plan.component ? ` (${plan.component.id})` : ''}`);
  if (plan.status === 'unknown') {
    core.warning(`Component value ${JSON.stringify(plan.values)} is not in .github/component-owners.json; the issue form and the map are out of sync.`);
  }

  const labelsToAdd = [...plan.labelsToAdd];
  let labelsToRemove = [...plan.labelsToRemove];
  let assigned = null;
  if (plan.ownerCandidates.length > 0) {
    for (const login of plan.ownerCandidates) {
      if (await isAssignable(github, core, repo, login)) {
        assigned = login;
        break;
      }
    }
    if (assigned) {
      try {
        const { data } = await github.rest.issues.addAssignees({ ...repo, issue_number: issue.number, assignees: [assigned] });
        // The API answers 201 and silently drops a login it cannot assign; verify.
        if (!(data.assignees || []).some(a => normalize(a.login) === normalize(assigned))) {
          core.warning(`GitHub accepted the request but did not assign "${assigned}".`);
          assigned = null;
        }
      } catch (error) {
        core.warning(`Could not assign "${assigned}": ${error.message}`);
        assigned = null;
      }
    }
    if (!assigned) {
      core.warning(`No owner of "${plan.component.id}" could be assigned; marking the issue for triage.`);
      const triage = normalize(config.triageLabel.name);
      if (labelsToRemove.some(l => normalize(l) === triage)) {
        labelsToRemove = labelsToRemove.filter(l => normalize(l) !== triage);
      } else if (!labelsToAdd.some(l => normalize(l) === triage)) {
        labelsToAdd.push(config.triageLabel.name);
      }
    }
  } else if (plan.status === 'resolved' && plan.alreadyAssigned) {
    core.info('Issue already has an assignee; leaving assignment unchanged.');
  }

  for (const name of labelsToRemove) {
    try {
      await github.rest.issues.removeLabel({ ...repo, issue_number: issue.number, name });
    } catch (error) {
      if (error.status !== 404) core.warning(`Could not remove label "${name}": ${error.message}`);
    }
  }
  const ready = [];
  for (const name of labelsToAdd) {
    if (await ensureLabel(github, core, repo, labelSpec(name, config))) ready.push(name);
  }
  if (ready.length > 0) {
    try {
      await github.rest.issues.addLabels({ ...repo, issue_number: issue.number, labels: ready });
    } catch (error) {
      core.warning(`Could not add labels ${JSON.stringify(ready)}: ${error.message}`);
    }
  }

  await core.summary
    .addHeading(`Issue #${issue.number} routing`, 3)
    .addTable([
      [{ data: 'Field', header: true }, { data: 'Value', header: true }],
      ['Component value', plan.values.join(', ') || '(none)'],
      ['Status', plan.status],
      ['Labels added', ready.join(', ') || '-'],
      ['Labels removed', labelsToRemove.join(', ') || '-'],
      ['Assignee', assigned || (plan.alreadyAssigned ? '(unchanged, already assigned)' : '-')],
    ])
    .write();
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
