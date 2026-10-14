using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.RuntimeVerification;

/// <summary>
/// The Freedom UI DOM contract page-smoke relies on, as the scripts it evaluates in the page. Every script is a
/// self-contained expression whose value is a plain JSON object, so it is read with <c>returnByValue</c>.
/// </summary>
/// <remarks>
/// The contract (see docs/knowledge/platform/a-freedom-ui-page-is-ready-when-its-outlet-is-quiet.md):
/// <list type="bullet">
/// <item>The Shell is up when <c>crt-root</c> exists and the splash <c>#loading-animation</c> is hidden.</item>
/// <item>A Freedom UI page renders inside <c>crt-navigation-schema-outlet</c>; a visible <c>crt-7x-schema</c> or
/// <c>.schema-wrap</c> means a Classic UI page instead.</item>
/// <item>A visible <c>crt-mask .crt-mask-container</c> is a loading mask; presence alone means nothing, only
/// visibility does.</item>
/// <item>A page control is a <c>crt-*</c> component carrying its view element name in <c>element-name</c>, and in
/// <c>data-item-marker</c>/<c>data-qa</c> when the marker feature is on.</item>
/// </list>
/// The page is ready when all of that holds and the set of visible named elements has not changed for a quiet period;
/// the quiet period is the caller's, the scripts only report a signature.
/// </remarks>
internal static class FreedomUiPageScripts {

	/// <summary>Longest caption, label or value reported, in characters.</summary>
	internal const int MaxText = 60;

	/// <summary>Most buttons reported.</summary>
	internal const int MaxButtons = 30;

	/// <summary>Most fields reported.</summary>
	internal const int MaxFields = 40;

	// Helpers shared by every script. visible() uses checkVisibility where the browser has it (Chromium 105+), which
	// also sees opacity:0 and visibility:hidden; a field component is one of FIELD_TAGS.
	private const string Prelude = """
		const FIELD_TAGS = ['crt-input','crt-number-input','crt-combobox','crt-checkbox','crt-datetimepicker',
			'crt-email-input','crt-phone-input','crt-web-input','crt-rich-text-editor','crt-html-editor','crt-switch',
			'crt-slide-toggle','crt-multi-select','crt-tag-select','crt-color-picker','crt-image-input','crt-password-input'];
		const FIELD_SELECTOR = FIELD_TAGS.join(',');
		const visible = e => !!e && (typeof e.checkVisibility === 'function'
			? e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true })
			: e.getClientRects().length > 0);
		const clean = (s, max) => {
			if (s === null || s === undefined) { return null; }
			s = String(s).replace(/\s+/g, ' ').trim();
			if (!s) { return null; }
			return s.length > max ? s.slice(0, max - 1) + '…' : s;
		};
		const textOf = e => {
			const walker = document.createTreeWalker(e, NodeFilter.SHOW_TEXT);
			const parts = [];
			let node;
			while ((node = walker.nextNode())) {
				if (!node.parentElement || node.parentElement.closest('mat-icon, crt-icon, .mat-icon, svg, style, script')) {
					continue;
				}
				parts.push(node.nodeValue);
			}
			return parts.join(' ');
		};
		const nameOf = e => e.getAttribute('element-name') || e.getAttribute('data-item-marker') || e.getAttribute('data-qa') || null;
		const outlet = document.querySelector('crt-navigation-schema-outlet');
		const scope = outlet || document.body;
		const buttonCaption = e => {
			const native = e.querySelector('button');
			return clean(textOf(e), 200) || clean(native && native.getAttribute('aria-label'), 200)
				|| clean(e.getAttribute('title'), 200) || clean(native && native.getAttribute('title'), 200);
		};
		const buttonDisabled = e => {
			const native = e.querySelector('button');
			return !!((native && (native.disabled || native.getAttribute('aria-disabled') === 'true'))
				|| e.hasAttribute('disabled') || e.classList.contains('disabled'));
		};
		const pageButtons = () => Array.from(scope.querySelectorAll('crt-button'))
			.filter(e => visible(e) && !(e.parentElement && e.parentElement.closest(FIELD_SELECTOR)));
		""";

	/// <summary>
	/// Reports where the page is: <c>login</c> (the session was not accepted), <c>classic</c> (a Classic UI page),
	/// <c>loading</c> with the reason, or <c>ready</c> with a signature of the visible named elements.
	/// </summary>
	internal const string ReadyScript = "(() => {" + Prelude + """
		const href = location.href;
		if (/\/(Login|NuiLogin)\b/i.test(location.pathname) || document.querySelector('#loginEdit, .login-container')) {
			return { state: 'login', href };
		}
		if (document.readyState !== 'complete') { return { state: 'loading', why: 'the document is still loading', href }; }
		if (visible(document.querySelector('#loading-animation'))) { return { state: 'loading', why: 'the Shell splash is showing', href }; }
		if (!document.querySelector('crt-root')) { return { state: 'loading', why: 'the Freedom UI Shell (crt-root) has not rendered', href }; }
		if (Array.from(document.querySelectorAll('crt-7x-schema, .schema-wrap')).some(visible)) {
			return { state: 'classic', href };
		}
		if (!outlet) { return { state: 'loading', why: 'no page outlet (crt-navigation-schema-outlet) yet', href }; }
		if (Array.from(document.querySelectorAll('crt-mask .crt-mask-container')).some(visible)) {
			return { state: 'loading', why: 'a loading mask is showing', href };
		}
		const named = Array.from(outlet.querySelectorAll('[element-name]')).filter(visible);
		if (named.length === 0) { return { state: 'loading', why: 'the page has rendered no named element yet', href }; }
		let hash = 0;
		for (const e of named) {
			const n = e.getAttribute('element-name');
			for (let i = 0; i < n.length; i++) { hash = (hash * 31 + n.charCodeAt(i)) | 0; }
		}
		return { state: 'ready', href, signature: href + '|' + named.length + '|' + hash };
	})()
	""";

	/// <summary>The page title and its visible buttons and fields, capped by the constants above.</summary>
	internal static readonly string InventoryScript = "(() => {" + Prelude
		+ $"const MAX_TEXT = {MaxText}; const MAX_BUTTONS = {MaxButtons}; const MAX_FIELDS = {MaxFields};" + """
		const titleElement = scope.querySelector('[element-name="PageTitle"]');
		const title = clean(titleElement ? textOf(titleElement) : document.title, MAX_TEXT);
		const buttons = pageButtons().map(e => {
			const item = { name: nameOf(e), caption: clean(buttonCaption(e), MAX_TEXT) };
			if (buttonDisabled(e)) { item.disabled = true; }
			return item;
		});
		const fields = Array.from(scope.querySelectorAll(FIELD_SELECTOR))
			.filter(e => visible(e) && !(e.parentElement && e.parentElement.closest(FIELD_SELECTOR)))
			.map(e => {
				const tag = e.tagName.toLowerCase();
				const input = e.querySelector('input, textarea, [contenteditable="true"]');
				const label = e.querySelector('label');
				const item = {
					name: nameOf(e),
					type: tag.replace(/^crt-/, ''),
					caption: clean(label ? textOf(label) : null, MAX_TEXT)
						|| clean(input && input.getAttribute('aria-label'), MAX_TEXT)
						|| clean(input && input.getAttribute('placeholder'), MAX_TEXT)
				};
				if ((input && (input.required || input.getAttribute('aria-required') === 'true'))
					|| e.querySelector('.mat-mdc-form-field-required-marker, .required')) {
					item.required = true;
				}
				if (input && (input.readOnly || input.disabled || input.getAttribute('aria-readonly') === 'true')) {
					item.readonly = true;
				}
				if (tag === 'crt-password-input' || (input && input.type === 'password')) {
					item.value = null;
				} else if (input && input.type === 'checkbox') {
					item.value = input.checked ? 'true' : 'false';
				} else if (input && input.isContentEditable) {
					item.value = clean(input.innerText, MAX_TEXT);
				} else if (input) {
					item.value = clean(input.value, MAX_TEXT);
				} else {
					const checked = e.querySelector('[aria-checked]');
					item.value = checked ? checked.getAttribute('aria-checked') : null;
				}
				const error = e.querySelector('mat-error, .mat-mdc-form-field-error');
				if (error && visible(error)) { item.error = clean(textOf(error), MAX_TEXT); }
				else if (input && input.getAttribute('aria-invalid') === 'true') { item.error = 'invalid'; }
				return item;
			});
		return {
			title, scope: outlet ? 'page' : 'document',
			totalButtons: buttons.length, totalFields: fields.length,
			buttons: buttons.slice(0, MAX_BUTTONS), fields: fields.slice(0, MAX_FIELDS)
		};
	})()
	""";

	/// <summary>The visible dialogs and notifications (toasts), at most three of each.</summary>
	internal static readonly string OverlayScript = "(() => {" + Prelude + $"const MAX = {MaxText * 5};" + """
		const texts = selector => Array.from(document.querySelectorAll(selector)).filter(visible)
			.map(e => clean(textOf(e), MAX)).filter(t => !!t).slice(0, 3);
		return {
			dialogs: texts('mat-dialog-container, .mat-mdc-dialog-container, crt-message-dialog'),
			toasts: texts('mat-snack-bar-container, .mat-mdc-snack-bar-container, crt-notify')
		};
	})()
	""";

	/// <summary>
	/// The click script for one button, matched by caption or element name, case-insensitively, among the visible
	/// buttons of the page. It clicks only a single, enabled match.
	/// </summary>
	/// <param name="button">Caption or element name.</param>
	internal static string BuildClickScript(string button) =>
		"((wanted) => {" + Prelude + $"const MAX_TEXT = {MaxText};" + """
		const key = String(wanted).replace(/\s+/g, ' ').trim().toLowerCase();
		const all = pageButtons();
		const matches = all.filter(e => {
			const caption = (buttonCaption(e) || '').toLowerCase();
			const name = (nameOf(e) || '').toLowerCase();
			return caption === key || name === key;
		});
		const describe = e => ({ name: nameOf(e), caption: clean(buttonCaption(e), MAX_TEXT) });
		if (matches.length === 0) {
			return { outcome: 'not-found', candidates: all.slice(0, 15).map(describe) };
		}
		if (matches.length > 1) {
			return { outcome: 'ambiguous', candidates: matches.slice(0, 15).map(describe) };
		}
		const target = matches[0];
		const described = describe(target);
		if (buttonDisabled(target)) { return Object.assign({ outcome: 'disabled' }, described); }
		const native = target.querySelector('button') || target;
		native.scrollIntoView({ block: 'center' });
		native.click();
		return Object.assign({ outcome: 'clicked' }, described);
	})(
	""" + JsonSerializer.Serialize(button) + ")";

	/// <summary>Deserializes a script's value.</summary>
	internal static T Read<T>(JsonElement value) => value.Deserialize<T>(JsonOptions);

	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>What <see cref="FreedomUiPageScripts.ReadyScript"/> reports.</summary>
internal sealed record PageReadyState(string State, string Why, string Href, string Signature);

/// <summary>What <see cref="FreedomUiPageScripts.InventoryScript"/> reports.</summary>
internal sealed record PageInventory(
	string Title,
	string Scope,
	int TotalButtons,
	int TotalFields,
	List<PageButton> Buttons,
	List<PageField> Fields);

/// <summary>What <see cref="FreedomUiPageScripts.OverlayScript"/> reports.</summary>
internal sealed record PageOverlays(List<string> Dialogs, List<string> Toasts);

/// <summary>What the click script reports.</summary>
internal sealed record PageClickOutcome(string Outcome, string Name, string Caption, List<PageButton> Candidates);

/// <summary>A visible button of the page.</summary>
/// <param name="Name">View element name (<c>element-name</c>), or the marker when the name is absent.</param>
/// <param name="Caption">Visible caption, else its aria-label or title.</param>
public sealed record PageButton(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("caption")] string Caption) {

	/// <summary>True when the button cannot be clicked.</summary>
	[JsonPropertyName("disabled"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Disabled { get; init; }
}

/// <summary>A visible field of the page.</summary>
/// <param name="Name">View element name.</param>
/// <param name="Type">Component type without the <c>crt-</c> prefix, for example <c>input</c> or <c>combobox</c>.</param>
/// <param name="Caption">The field's label, else its aria-label or placeholder.</param>
public sealed record PageField(
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("type")] string Type,
	[property: JsonPropertyName("caption")] string Caption) {

	/// <summary>True when the field is marked required.</summary>
	[JsonPropertyName("required"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Required { get; init; }

	/// <summary>True when the field cannot be edited.</summary>
	[JsonPropertyName("readonly"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool Readonly { get; init; }

	/// <summary>The shown value, cut to 60 characters; never reported for a password field.</summary>
	[JsonPropertyName("value"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Value { get; init; }

	/// <summary>The validation message the field shows, or <c>invalid</c>.</summary>
	[JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}
