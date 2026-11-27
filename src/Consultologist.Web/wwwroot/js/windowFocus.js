// #1509 (Task 11): the Create page asks a 31 package's start checks again when the window regains focus, so an account
// changed in another tab (the time zone set, a signature block removed) is seen before Start. One listener per
// registration; the page stops it when it is disposed.
window.consultologistWindowFocus = (() => {
	const listeners = new Map();
	let next = 0;

	const listen = (dotnetRef) => {
		const id = ++next;
		const handler = () => dotnetRef.invokeMethodAsync("OnWindowFocusAsync").catch(() => {});
		window.addEventListener("focus", handler);
		listeners.set(id, handler);
		return id;
	};

	const stop = (id) => {
		const handler = listeners.get(id);
		if (handler) {
			window.removeEventListener("focus", handler);
			listeners.delete(id);
		}
	};

	return { listen, stop };
})();
