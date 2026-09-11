// #686: focus management for the run-diagram modal — the app's one hand-rolled
// dialog. Blazor owns the markup and the Escape-to-close; this owns the parts a
// virtual DOM can't reach: where focus is on open, keeping Tab inside the panel
// while it's up, and putting focus back on the trigger when it closes. Only one
// dialog is ever open at a time, so a single module-level slot is enough.
window.consultologistDialog = (() => {
	// The tab-order of what a user can land on inside the panel.
	const SELECTOR =
		'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

	let previous = null;
	let panel = null;
	let onKeyDown = null;

	// Visible, focusable descendants in document order. offsetParent is null for
	// display:none, so a hidden control never becomes a trap boundary.
	const focusable = (root) =>
		Array.from(root.querySelectorAll(SELECTOR)).filter(
			(el) => el.offsetParent !== null || el === document.activeElement
		);

	const activate = (dialogPanel) => {
		if (!dialogPanel) {
			return;
		}
		// Remember who opened us so close can hand focus back.
		previous = document.activeElement;
		panel = dialogPanel;

		const items = focusable(panel);
		(items[0] || panel).focus();

		// Wrap Tab / Shift+Tab so focus cycles within the dialog instead of
		// leaking out to the page behind it.
		onKeyDown = (event) => {
			if (event.key !== "Tab") {
				return;
			}
			const cycle = focusable(panel);
			if (cycle.length === 0) {
				event.preventDefault();
				panel.focus();
				return;
			}
			const first = cycle[0];
			const last = cycle[cycle.length - 1];
			if (event.shiftKey && document.activeElement === first) {
				event.preventDefault();
				last.focus();
			} else if (!event.shiftKey && document.activeElement === last) {
				event.preventDefault();
				first.focus();
			}
		};
		panel.addEventListener("keydown", onKeyDown);
	};

	const deactivate = () => {
		if (panel && onKeyDown) {
			panel.removeEventListener("keydown", onKeyDown);
		}
		panel = null;
		onKeyDown = null;

		// Restore focus to the trigger. If it's gone from the DOM (the page moved
		// on while the dialog was up), focus() is a harmless no-op.
		const target = previous;
		previous = null;
		if (target && typeof target.focus === "function") {
			target.focus();
		}
	};

	return { activate, deactivate };
})();
