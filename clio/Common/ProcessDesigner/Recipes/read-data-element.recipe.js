// Process Designer "Read data" driving recipe (diagram-js, classic SchemaDesigner).
// Invoked by ProcessDesignerDriver as: (<thisFunction>)(phase, params).
// Phases return JSON-serializable objects (or a Promise resolving to one). The driver performs the
// TRUSTED clicks (CDP Input.dispatchMouseEvent) using the coordinates these phases return; the
// untrusted DOM work (context-pad append drag, field fills, state reads) happens here.
// Feasibility baseline: env krestov-test, process UsrProcess_493d4c9 — see ai-bp-ui-playbook.md §6.
function (phase, params) {
	params = params || {};
	var cls = function (el) { return (typeof el.className === 'object' ? el.className.baseVal : el.className) || ''; };
	var center = function (el) { var r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; };
	var fire = function (el, type, props) {
		var e = new Event(type, { bubbles: true });
		for (var k in (props || {})) { e[k] = props[k]; }
		el.dispatchEvent(e);
	};
	var setInputValue = function (el, value) {
		el.focus();
		var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
		setter.call(el, value);
		fire(el, 'input', {});
	};

	switch (phase) {
		// 1. Dismiss any stray create-popup occluding the canvas, then locate the Start event center.
		case 'prepare': {
			document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }));
			var shapes = [].slice.call(document.querySelectorAll('.djs-shape'));
			var start = shapes.filter(function (s) { return /\bstartEvent\b/.test(cls(s)); })[0];
			if (!start) { return { ready: false, error: 'start event not rendered yet' }; }
			var c = center(start);
			return { ready: true, x: c.x, y: c.y };
		}
		// 2. Append a System action from the selected element's context pad (defaults to readDataUserTask),
		//    using the QA-proven untrusted dragstart+mousemove+mouseup, auto-inserting onto the flow.
		case 'append': {
			var addBtn = document.querySelector('.djs-context-pad .entry[data-action="add.serviceTask"]');
			if (!addBtn) { return Promise.resolve({ ok: false, error: 'context pad (add.serviceTask) not available — start not selected' }); }
			var svg = document.querySelector('.djs-container svg');
			var sr = svg.getBoundingClientRect();
			var tx = sr.left + 360, ty = sr.top + 200;
			fire(addBtn, 'dragstart', { clientX: 5, clientY: 5 });
			// The drag-group only materializes after an initial mousemove on the source element.
			var startShape = [].slice.call(document.querySelectorAll('.djs-shape'))
				.filter(function (s) { return /\bstartEvent\b/.test(cls(s)); })[0];
			if (startShape) {
				var startRect = startShape.getBoundingClientRect();
				fire(startShape, 'mousemove', { clientX: startRect.left + startRect.width / 2, clientY: startRect.top + startRect.height / 2 });
			}
			return new Promise(function (resolve) {
				setTimeout(function () {
					var dragNode = [].slice.call(document.querySelectorAll('.djs-drag-group')).pop();
					if (!dragNode) { resolve({ ok: false, error: 'append drag did not start' }); return; }
					fire(dragNode, 'mousemove', { clientX: tx, clientY: ty });
					fire(dragNode, 'mouseover', {});
					fire(dragNode, 'mousemove', { clientX: tx, clientY: ty });
					fire(dragNode, 'mouseup', {});
					setTimeout(function () {
						var read = [].slice.call(document.querySelectorAll('.djs-shape'))
							.filter(function (s) { return /readDataUserTask/.test(cls(s)); })[0];
						if (!read) { resolve({ ok: false, error: 'Read data element was not appended' }); return; }
						var rc = center(read);
						resolve({ ok: true, x: rc.x, y: rc.y });
					}, 400);
				}, 150);
			});
		}
		// 3. Fill the "object to read" lookup and surface the matching dropdown option's coordinates.
		//    The Read data setup card renders ASYNC after the appended element is auto-selected, so poll
		//    for the EntitySchemaSelect combo before typing.
		case 'fillObject': {
			return new Promise(function (resolve) {
				var fieldAttempts = 0;
				var awaitField = function () {
					var field = document.querySelector('input[id*="EntitySchemaSelect"]');
					if (!field) {
						if (++fieldAttempts >= 40) {
							var reads = [].slice.call(document.querySelectorAll('.djs-shape')).filter(function (s) { return /readDataUserTask/.test(cls(s)); });
							var props = [].slice.call(document.querySelectorAll('input[id*="PropertiesPage"]')).map(function (i) { return i.id; });
							resolve({ ok: false, error: 'object lookup field (EntitySchemaSelect) did not render. readDataShapes=' + reads.length + ', propsPageInputs=' + props.length + (props[0] ? (' e.g. ' + props[0]) : '') });
							return;
						}
						setTimeout(awaitField, 300);
						return;
					}
					setInputValue(field, params.object || '');
					fire(field, 'keyup', { key: 't' });
					var optionAttempts = 0;
					var awaitOption = function () {
						var option = [].slice.call(document.querySelectorAll('.listview .listview-item, .listview li, [class*="list-item"]'))
							.filter(function (li) { return li.offsetWidth > 0 && (li.textContent || '').trim() === (params.object || ''); })[0];
						if (option) { var c = center(option); resolve({ ok: true, optionX: c.x, optionY: c.y }); return; }
						if (++optionAttempts >= 12) { resolve({ ok: false, error: 'no lookup option matching "' + (params.object || '') + '"' }); return; }
						setTimeout(awaitOption, 250);
					};
					setTimeout(awaitOption, 300);
				};
				awaitField();
			});
		}
		// 4. Set the process caption (the deterministic readback handle).
		case 'setCaption': {
			var cap = document.getElementById('schema-designer-caption-el');
			if (!cap) { return { ok: false, error: 'caption field not found' }; }
			setInputValue(cap, params.caption || '');
			fire(cap, 'change', {});
			return { ok: true };
		}
		// 5. The designer flags an invalid connection with .djs-validate-outline.
		case 'checkValid': {
			return { invalid: !!document.querySelector('.djs-connection .djs-validate-outline') };
		}
		// 6. Locate the toolbar SAVE control.
		case 'saveCoords': {
			// The toolbar SAVE button (DOM text "Save"; CSS uppercases it). Prefer the stable id.
			var saveEl = document.getElementById('schema-designer-save-btn-textEl')
				|| [].slice.call(document.querySelectorAll('*')).filter(function (e) {
					return e.children.length === 0 && /^save$/i.test((e.textContent || '').trim()) && e.offsetWidth > 0;
				})[0];
			if (!saveEl) { return { ok: false, error: 'SAVE control not found' }; }
			var c = center(saveEl);
			return { ok: true, x: c.x, y: c.y };
		}
		// 7. Poll for the platform "Successfully saved" signal and read back identity where available.
		case 'saveResult': {
			return new Promise(function (resolve) {
				var attempts = 0;
				var poll = function () {
					attempts++;
					var panel = [].slice.call(document.querySelectorAll('.message-panel, [class*="message"]'))
						.filter(function (e) { return /successfully saved|сохранено|saved/i.test(e.textContent || ''); })[0];
					if (panel) {
						var uid = null;
						try {
							var mgr = window.Ext && Ext.ComponentManager && Ext.ComponentManager.get('schema-designer');
							uid = (mgr && mgr.model && mgr.model.values) ? (mgr.model.values.SchemaUId || null) : null;
						} catch (e) { /* identity readback is best-effort */ }
						resolve({ ok: true, uid: uid });
						return;
					}
					if (attempts >= 40) { resolve({ ok: false, error: 'no "Successfully saved" signal observed' }); return; }
					setTimeout(poll, 300);
				};
				poll();
			});
		}
		default:
			return { ok: false, error: 'unknown recipe phase: ' + phase };
	}
}
