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
const NOTIFICATION_MODES = ['assign', 'mention'];
// Marks the one routing comment per issue, so a later run updates it instead of adding another.
const COMMENT_MARKER = '<!-- issue-routing -->';
// The identity GITHUB_TOKEN writes as. Labels and comments by this login are routing's own.
const DEFAULT_ROUTING_BOT = 'github-actions[bot]';

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
    errors.push(`${where}: "owners" must be an array (use [] when nobody owns the component)`);
    return;
  }
  for (const owner of owners) {
    if (!LOGIN.test(String(owner).replace(/^@/, ''))) {
      errors.push(`${where}: owner "${owner}" is not a GitHub user login (teams cannot be assignees)`);
    }
  }
}

function checkComponent(errors, seen, component, index, prefix) {
  const where = describeComponent(component, index);
  checkUnique(errors, seen.id, where, 'id', component?.id);
  checkUnique(errors, seen.option, where, 'option', component?.option);
  if (component?.triage) return;
  checkUnique(errors, seen.label, where, 'label', component?.label, ' unless "triage": true');
  if (component?.label && prefix && !normalize(component.label).startsWith(normalize(prefix))) {
    // Routing removes these labels on its own; a shared topic label would be stripped from issues.
    errors.push(`${where}: label "${component.label}" must start with "${prefix}"`);
  }
  checkOwners(errors, where, component?.owners);
  checkNotificationMode(errors, where, component?.ownerNotification, true);
}

function checkNotificationMode(errors, where, mode, optional) {
  if (optional && mode === undefined) return;
  if (!NOTIFICATION_MODES.includes(mode)) {
    errors.push(`${where}: "ownerNotification" must be one of ${NOTIFICATION_MODES.join(', ')}`);
  }
}

/** How a component's owners hear about a routed issue: `assign` or `mention`. */
function notificationMode(component, config) {
  return component.ownerNotification ?? config.ownerNotification;
}

/** Throws with every problem found; a broken map must fail loudly, not route silently. */
function validateConfig(config) {
  if (!config || !Array.isArray(config.components)) {
    throw new Error('component-owners.json: "components" must be an array');
  }
  const errors = [];
  if (!config.fieldLabel) errors.push('"fieldLabel" is required');
  if (!config.triageLabel?.name) errors.push('"triageLabel.name" is required');
  if (!config.componentLabelPrefix) errors.push('"componentLabelPrefix" is required');
  checkNotificationMode(errors, 'config', config.ownerNotification, false);
  const seen = { id: new Set(), option: new Set(), label: new Set() };
  config.components.forEach((component, index) => checkComponent(errors, seen, component, index, config.componentLabelPrefix));
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

function unchangedPlan(base) {
  return { ...base, unchanged: true };
}

// A resolved component: the issue must carry exactly the chosen component label. Owners and the
// triage label move only when routing actually changes (the chosen label is not on the issue
// yet), so a text edit never re-assigns an owner a human removed.
function planResolved({ base, current, present, liveComponentLabels, currentAssignees, config }) {
  const chosen = normalize(current.component.label);
  const alreadyRouted = present.has(chosen);
  const remove = liveComponentLabels.filter(l => l !== chosen).map(l => present.get(l));
  const add = alreadyRouted ? [] : [current.component.label];
  const owners = current.component.owners.map(o => String(o).replace(/^@/, ''));
  const triage = normalize(config.triageLabel.name);
  const mode = notificationMode(current.component, config);
  let ownerCandidates = [];
  let ownersToMention = [];
  if (!alreadyRouted && owners.length === 0 && !present.has(triage) && currentAssignees.length === 0) {
    // Nobody owns this component: route the label, but a human still has to pick the issue up.
    add.push(config.triageLabel.name);
  } else if (!alreadyRouted && owners.length > 0) {
    if (present.has(triage)) remove.push(present.get(triage));
    if (mode === 'mention') ownersToMention = owners;
    else if (currentAssignees.length === 0) ownerCandidates = owners;
  }
  if (add.length === 0 && remove.length === 0 && ownerCandidates.length === 0 && ownersToMention.length === 0) {
    return unchangedPlan(base);
  }
  return { ...base, labelsToAdd: add, labelsToRemove: remove, ownerCandidates, ownersToMention };
}

// No routable component. When the form explicitly says "not sure" (or an unknown value), component
// labels are cleared if the author changed the dropdown in THIS edit, or if routing applied them
// itself; a component label a human triager applied is kept. A blank issue is never cleared.
function planUnrouted({ base, current, previousBody, present, liveComponentLabels, currentAssignees, routingAppliedLabels, config }) {
  const explicit = current.status !== 'missing';
  const changedByAuthor = explicit
    && previousBody !== undefined
    && routingKey(classify(previousBody, config)) !== routingKey(current);
  // A label routing itself applied is stale once the form says "not sure", even when the run of
  // the edit that said so was cancelled; a label a human applied is a triager's decision.
  const stale = liveComponentLabels.filter(l => changedByAuthor || (explicit && routingAppliedLabels.has(l)));
  const remove = stale.map(l => present.get(l));
  const keepsComponentLabel = liveComponentLabels.length > remove.length;
  // An assigned issue has someone looking at it already; it is not waiting for triage.
  const needsTriage = !keepsComponentLabel && currentAssignees.length === 0
    && !present.has(normalize(config.triageLabel.name));
  const add = needsTriage ? [config.triageLabel.name] : [];
  if (add.length === 0 && remove.length === 0) return unchangedPlan(base);
  return { ...base, labelsToAdd: add, labelsToRemove: remove };
}

// An issue created through the API has no Component field; its component can still be given as
// exactly one `component:*` label (e.g. `gh issue create --label component:package`). The label is
// the choice, so labels stay as they are and only the owners are notified. That happens when the
// issue is opened or the label is added, never on a later text edit, so owners are told once.
function planFromLabel({ base, present, liveComponentLabels, currentAssignees, trigger, labeledName, config }) {
  if (liveComponentLabels.length !== 1) {
    return { ...base, source: liveComponentLabels.length > 1 ? 'ambiguous-labels' : 'none' };
  }
  const component = config.components.find(c => c.label && normalize(c.label) === liveComponentLabels[0]);
  const routed = { ...base, status: 'resolved', component, source: 'label' };
  if (trigger === 'edited') return unchangedPlan(routed);
  // Only the event that added THIS label routes; adding some other component label (a typo, or a
  // second one being considered) must not re-notify the owners or strip needs-triage.
  if (trigger === 'labeled' && normalize(labeledName) !== liveComponentLabels[0]) return unchangedPlan(routed);
  const changes = ownerChanges(component, present, currentAssignees, config);
  const nothing = Object.values(changes).every(list => list.length === 0);
  return nothing ? unchangedPlan(routed) : { ...routed, ...changes };
}

// Who is told about a routed issue, and what that does to the triage label.
function ownerChanges(component, present, currentAssignees, config) {
  const owners = component.owners.map(o => String(o).replace(/^@/, ''));
  const triage = normalize(config.triageLabel.name);
  const unassigned = currentAssignees.length === 0;
  if (owners.length === 0) {
    const needsTriage = !present.has(triage) && unassigned;
    return { labelsToAdd: needsTriage ? [config.triageLabel.name] : [], labelsToRemove: [], ownerCandidates: [], ownersToMention: [] };
  }
  const mention = notificationMode(component, config) === 'mention';
  const ownerCandidates = !mention && unassigned ? owners : [];
  const ownersToMention = mention ? owners : [];
  // needs-triage goes only when someone is actually being told; an assigned issue keeps whatever
  // triage decision a human made.
  const notifying = ownerCandidates.length > 0 || ownersToMention.length > 0;
  return {
    labelsToAdd: [],
    labelsToRemove: notifying && present.has(triage) ? [present.get(triage)] : [],
    ownerCandidates,
    ownersToMention,
  };
}

/**
 * Computes the label and assignee changes for one event from the LIVE labels and assignees. Pure:
 * no API calls.
 *
 * - `component:*` labels (config.componentLabelPrefix) belong to routing. For a resolved component
 *   the issue ends with exactly the chosen one, whatever the previous body said, so runs that
 *   GitHub collapsed or cancelled cannot leave a stale label. Topic labels outside the prefix are
 *   never touched.
 * - `previousBody` (`changes.body.from`, `undefined` on `opened`) and `routingAppliedLabels` (the
 *   component labels whose last `labeled` event was routing's own) are consulted only to tell a
 *   stale routing label from a human triager's label on an unrouted issue.
 * - Owners are proposed only when routing changes and the issue has NO assignee. An assignee is
 *   the claim signal of the claim-clio-issue skill, so an existing one is never changed.
 * - A component without owners gets its label and `needs-triage`.
 * - With `ownerNotification: "mention"` the owners are listed in `ownersToMention` instead: they
 *   are notified through a comment and the assignee stays free for whoever claims the issue.
 * - Without a Component field (an issue created through the API), a single `component:*` label is
 *   taken as the component; see planFromLabel. `trigger` is the event action: `opened`, `edited`
 *   or `labeled` (defaults from `previousBody`).
 * - `needs-triage` is added only to an issue nobody is assigned to.
 */
function planRouting({ body, previousBody, currentLabels = [], currentAssignees = [], routingAppliedLabels = [], trigger, labeledName, config }) {
  const current = classify(body, config);
  const base = {
    status: current.status,
    values: current.values,
    component: current.component,
    labelsToAdd: [],
    labelsToRemove: [],
    ownerCandidates: [],
    ownersToMention: [],
    alreadyAssigned: currentAssignees.length > 0,
    unchanged: false,
    source: current.status === 'missing' ? 'none' : 'form',
  };
  const present = new Map(currentLabels.map(l => [normalize(l), l]));
  const componentLabels = new Set(config.components.filter(c => c.label).map(c => normalize(c.label)));
  const liveComponentLabels = [...present.keys()].filter(l => componentLabels.has(l));
  const applied = new Set(routingAppliedLabels.map(normalize));
  const event = trigger ?? (previousBody === undefined ? 'opened' : 'edited');
  const context = { base, current, previousBody, present, liveComponentLabels, currentAssignees, routingAppliedLabels: applied, trigger: event, labeledName, config };
  if (current.status === 'resolved') return planResolved(context);
  if (current.status === 'missing') {
    const fromLabel = planFromLabel(context);
    if (fromLabel.source === 'label') return fromLabel;
    return { ...planUnrouted(context), source: fromLabel.source };
  }
  return planUnrouted(context);
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
// Without the live state a run cannot plan safely: the payload may predate an earlier run's
// assignment, and addAssignees is additive, so a stale plan would leave two assignees. A later edit
// or a manual re-run routes the issue instead.
async function readLiveIssue(github, core, repo, issue) {
  try {
    const { data } = await github.rest.issues.get({ ...repo, issue_number: issue.number });
    return data;
  } catch (error) {
    core.warning(`Could not re-read issue #${issue.number}; skipping routing without changes: ${error.message}`);
    return null;
  }
}

function routingBot(config) {
  return normalize(config.routingBotLogin || DEFAULT_ROUTING_BOT);
}

/** Component labels on the issue whose most recent `labeled` event was made by routing. */
async function readRoutingAppliedLabels(github, core, repo, issueNumber, labels, config) {
  const componentLabels = new Set(config.components.filter(c => c.label).map(c => normalize(c.label)));
  if (!labels.some(l => componentLabels.has(normalize(l)))) return [];
  try {
    const events = await github.paginate(github.rest.issues.listEvents, { ...repo, issue_number: issueNumber, per_page: 100 });
    const lastActor = new Map();
    for (const event of events) {
      if (event.event === 'labeled' && event.label?.name) lastActor.set(normalize(event.label.name), normalize(event.actor?.login));
    }
    return labels.filter(l => componentLabels.has(normalize(l)) && lastActor.get(normalize(l)) === routingBot(config));
  } catch (error) {
    // Unknown history: treat every label as human-applied, which only ever keeps a label.
    core.warning(`Could not read label history of #${issueNumber}: ${error.message}`);
    return [];
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
function keepForTriage(labels, config, alreadyAssigned = false) {
  const triage = normalize(config.triageLabel.name);
  const isTriage = l => normalize(l) === triage;
  if (labels.remove.some(isTriage)) {
    return { add: labels.add, remove: labels.remove.filter(l => !isTriage(l)) };
  }
  // An assigned issue already has someone on it: keep a triage label that is there, add none.
  if (labels.add.some(isTriage) || alreadyAssigned) return labels;
  return { add: [...labels.add, config.triageLabel.name], remove: labels.remove };
}

/** Applies the label changes; returns only the changes GitHub actually accepted. */
async function applyLabels(github, core, repo, issueNumber, labels, config) {
  const removed = [];
  for (const name of labels.remove) {
    try {
      await github.rest.issues.removeLabel({ ...repo, issue_number: issueNumber, name });
      removed.push(name);
    } catch (error) {
      if (error.status === 404) removed.push(name);
      else core.warning(`Could not remove label "${name}": ${error.message}`);
    }
  }
  const ready = [];
  for (const name of labels.add) {
    if (await ensureLabel(github, core, repo, labelSpec(name, config))) ready.push(name);
  }
  if (ready.length === 0) return { added: [], removed };
  try {
    await github.rest.issues.addLabels({ ...repo, issue_number: issueNumber, labels: ready });
    return { added: ready, removed };
  } catch (error) {
    core.warning(`Could not add labels ${JSON.stringify(ready)}: ${error.message}`);
    return { added: [], removed };
  }
}

function routingComment(component, owners) {
  const mentions = owners.map(o => '@' + o).join(', ');
  return [
    COMMENT_MARKER,
    `Routed to **${component.option}** (\`${component.label}\`). Owner(s): ${mentions}.`,
    '',
    '<sub>Notification only: nobody was assigned. Assign yourself to claim the issue. Source: `.github/component-owners.json`.</sub>',
  ].join('\n');
}

/** Creates or updates the single routing comment; returns true when it was written. */
async function upsertRoutingComment(github, core, repo, issueNumber, component, owners, config) {
  const body = routingComment(component, owners);
  try {
    const comments = await github.paginate(github.rest.issues.listComments, { ...repo, issue_number: issueNumber, per_page: 100 });
    // Only routing's own comment is updated; a human comment that copies the marker stays intact.
    const existing = comments.find(c => c.body?.startsWith(COMMENT_MARKER) && normalize(c.user?.login) === routingBot(config));
    if (existing) {
      await github.rest.issues.updateComment({ ...repo, comment_id: existing.id, body });
    } else {
      await github.rest.issues.createComment({ ...repo, issue_number: issueNumber, body });
    }
    return true;
  } catch (error) {
    core.warning(`Could not post the routing comment: ${error.message}`);
    return false;
  }
}

/**
 * Assigns or mentions the owners per the plan. An issue whose owners could not be told, either way,
 * keeps (or gets) the triage label so it stays visible.
 */
async function notifyOwners(github, core, repo, issueNumber, plan, config) {
  const labels = { add: plan.labelsToAdd, remove: plan.labelsToRemove };
  if (plan.ownerCandidates.length > 0) {
    const assigned = await assignOwner(github, core, repo, issueNumber, plan.ownerCandidates);
    if (assigned) return { labels, assigned, mentioned: [] };
    core.warning(`No owner of "${plan.component.id}" could be assigned; marking the issue for triage.`);
    return { labels: keepForTriage(labels, config), assigned: null, mentioned: [] };
  }
  if (plan.ownersToMention.length > 0) {
    if (await upsertRoutingComment(github, core, repo, issueNumber, plan.component, plan.ownersToMention, config)) {
      return { labels, assigned: null, mentioned: plan.ownersToMention };
    }
    core.warning(`The owners of "${plan.component.id}" could not be mentioned; marking the issue for triage.`);
    return { labels: keepForTriage(labels, config, plan.alreadyAssigned), assigned: null, mentioned: [] };
  }
  if (plan.status === 'resolved' && plan.alreadyAssigned) {
    core.info('Issue already has an assignee; leaving assignment unchanged.');
  }
  return { labels, assigned: null, mentioned: [] };
}

function describePlan(issueNumber, plan) {
  const component = plan.component ? ' (' + plan.component.id + ')' : '';
  const source = plan.source === 'label' ? ' from its component label' : '';
  return `Issue #${issueNumber}: component field = ${JSON.stringify(plan.values)} -> ${plan.status}${component}${source}`;
}

const UNCHANGED_REASONS = {
  form: 'Labels already match the selected component; leaving labels and assignees as they are.',
  label: 'Routed from its component label when the issue was opened or labelled; a text edit changes nothing.',
  'ambiguous-labels': 'No Component field and more than one component label; leaving the issue for a human.',
  none: 'No Component field and a component label or an assignee is already set; nothing to do.',
};

function unchangedReason(plan) {
  return UNCHANGED_REASONS[plan.source] ?? UNCHANGED_REASONS.none;
}

function skipReason(payload) {
  const issue = payload.issue;
  if (!issue || issue.pull_request) return 'Not an issue event; nothing to route.';
  if (payload.action === 'edited' && payload.changes?.body === undefined) {
    return 'Only the title changed; routing depends on the body. Skipping.';
  }
  return null;
}

function isComponentLabelEvent(payload, config) {
  return normalize(payload.label?.name).startsWith(normalize(config.componentLabelPrefix));
}

async function writeSummary(core, issueNumber, plan, outcome) {
  let assignee = outcome.assigned || '-';
  if (!outcome.assigned && plan.alreadyAssigned) assignee = '(unchanged, already assigned)';
  await core.summary
    .addHeading(`Issue #${issueNumber} routing`, 3)
    .addTable([
      [{ data: 'Field', header: true }, { data: 'Value', header: true }],
      ['Component value', plan.values.join(', ') || '(none)'],
      ['Status', plan.status + (plan.source === 'label' ? ' (from label)' : '')],
      ['Labels added', outcome.added.join(', ') || '-'],
      ['Labels removed', outcome.removed.join(', ') || '-'],
      ['Assignee', assignee],
      ['Owners mentioned', outcome.mentioned.join(', ') || '-'],
    ])
    .write();
}

/**
 * Entry point for actions/github-script. In `assign` mode it assigns at most ONE owner: the first
 * assignable login in the component's `owners` list. Several assignees would read as an ambiguous
 * claim to the claim-clio-issue skill, which stops on multiple assignees. In `mention` mode it
 * assigns nobody and mentions every owner in one routing comment.
 */
async function run({ github, context, core, configPath }) {
  const payload = context.payload;
  const skip = skipReason(payload);
  if (skip) {
    core.info(skip);
    return;
  }

  const config = loadConfig(configPath);
  if (payload.action === 'labeled' && !isComponentLabelEvent(payload, config)) {
    core.info(`Label "${payload.label?.name}" is not a component label; nothing to route.`);
    return;
  }
  const repo = context.repo;
  const issueNumber = payload.issue.number;
  const live = await readLiveIssue(github, core, repo, payload.issue);
  if (!live) return;
  const currentLabels = (live.labels || []).map(l => (typeof l === 'string' ? l : l.name));
  const status = classify(live.body, config).status;
  if (payload.action === 'labeled' && status !== 'missing') {
    // The form is the source of truth on form issues; a label added by hand is a human decision.
    core.info('The issue has a Component field; a label added later does not re-route it.');
    return;
  }
  const needsHistory = status !== 'resolved';
  const plan = planRouting({
    body: live.body,
    previousBody: payload.action === 'edited' ? payload.changes.body.from ?? '' : undefined,
    currentLabels,
    currentAssignees: (live.assignees || []).map(a => a.login),
    routingAppliedLabels: needsHistory ? await readRoutingAppliedLabels(github, core, repo, issueNumber, currentLabels, config) : [],
    trigger: payload.action,
    labeledName: payload.label?.name,
    config,
  });

  core.info(describePlan(issueNumber, plan));
  if (plan.unchanged) {
    core.info(unchangedReason(plan));
    return;
  }
  if (plan.status === 'unknown') {
    core.warning(`Component value ${JSON.stringify(plan.values)} is not in .github/component-owners.json; the issue form and the map are out of sync.`);
  }

  const notified = await notifyOwners(github, core, repo, issueNumber, plan, config);
  const applied = await applyLabels(github, core, repo, issueNumber, notified.labels, config);
  await writeSummary(core, issueNumber, plan, { assigned: notified.assigned, mentioned: notified.mentioned, ...applied });
}

// Returns true when the label was missing and has been created.
async function createIfMissing(github, core, repo, spec) {
  try {
    await github.rest.issues.getLabel({ ...repo, name: spec.name });
    return false;
  } catch (error) {
    if (error.status !== 404) {
      core.warning(`Could not read label "${spec.name}": ${error.message}`);
      return false;
    }
    return ensureLabel(github, core, repo, spec);
  }
}

/**
 * Creates every label the map declares (component labels and the triage label) that the repository
 * does not have yet, so an agent can pass `--label component:<id>` for a new component before
 * routing has ever used it. Existing labels are left alone: a team may have restyled them.
 */
async function syncLabels({ github, context, core, configPath }) {
  const config = loadConfig(configPath);
  const specs = [config.triageLabel, ...config.components.filter(c => c.label).map(c => labelSpec(c.label, config))];
  // A few dozen independent reads, and creations only for the (usually zero) missing labels, so
  // running them concurrently stays far below GitHub's rate limits.
  const outcomes = await Promise.all(specs.map(spec => createIfMissing(github, core, context.repo, spec)));
  const created = specs.filter((spec, index) => outcomes[index]).map(spec => spec.name);
  core.info(created.length > 0 ? `Created labels: ${created.join(', ')}` : 'All labels from the map already exist.');
  return created;
}

// ---- Choosing a component from code paths (used by agents through component-for.js).

// `paths` entries: a directory prefix ending with "/", an exact file, or a pattern with "*"
// (matches within one path segment). Returns how specific the match is (literal characters), or -1.
function pathMatchScore(pattern, file) {
  const pat = String(pattern).replaceAll('\\', '/');
  const target = String(file).replaceAll('\\', '/').replace(/^\.\//, '');
  if (pat.endsWith('/')) return target.startsWith(pat) ? pat.length : -1;
  if (!pat.includes('*')) return target === pat ? pat.length + 1 : -1;
  const parts = pat.split('*').map(part => part.replaceAll(/[.+?^${}()|[\]\\]/g, String.raw`\$&`));
  return new RegExp('^' + parts.join('[^/]*') + '$').test(target) ? pat.replaceAll('*', '').length : -1;
}

/**
 * Ranks the components whose `paths` cover the given files. For each file only the most specific
 * component counts (an exact file beats a directory), so a tool with its own entry is not claimed
 * by the broad directory of the MCP server. Returns [{ component, files }] sorted by file count.
 */
// The components whose best pattern for this file is the most specific one (ties are all returned).
function bestComponentsFor(file, config) {
  let best = -1;
  let winners = [];
  for (const component of config.components) {
    const score = Math.max(-1, ...(component.paths || []).map(pattern => pathMatchScore(pattern, file)));
    if (score < 0 || score < best) continue;
    if (score > best) { best = score; winners = []; }
    winners.push(component);
  }
  return winners;
}

function matchComponents(files, config) {
  const hits = new Map();
  for (const file of files) {
    for (const component of bestComponentsFor(file, config)) {
      if (!hits.has(component.id)) hits.set(component.id, { component, files: [] });
      hits.get(component.id).files.push(file);
    }
  }
  return [...hits.values()].sort((a, b) => b.files.length - a.files.length);
}

module.exports = {
  syncLabels,
  pathMatchScore,
  matchComponents,
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
