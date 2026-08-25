// #412: how a new build reaches a tab that is already open.
//
// The published service worker is cache-first, so a deploy installs into a
// *waiting* worker and the running tab keeps the old build until every tab is
// closed. This watcher registers the worker, notices the waiting one, and
// tells Blazor (UpdateBanner) so the user is asked to reload. The reload is
// only ever the user's click: a consult may be running and its page must not
// be swapped out from under it.
window.consultologistUpdate = (() => {
	const CHECK_INTERVAL_MS = 60 * 60 * 1000;
	let registration = null;
	let dotnetRef = null;
	let announced = false;

	const announce = () => {
		if (announced || !dotnetRef) {
			return;
		}
		announced = true;
		dotnetRef.invokeMethodAsync("OnUpdateReady");
	};

	// An installed worker while another controls the page is an update; the
	// very first install of the worker has no controller and is not news.
	const watchInstalling = (worker) => {
		if (!worker) {
			return;
		}
		worker.addEventListener("statechange", () => {
			if (worker.state === "installed" && navigator.serviceWorker.controller) {
				announce();
			}
		});
	};

	const start = async (ref) => {
		dotnetRef = ref;
		if (!("serviceWorker" in navigator)) {
			return false;
		}
		try {
			registration = await navigator.serviceWorker.register("service-worker.js");
		} catch (error) {
			console.warn("Service worker registration failed", error);
			return false;
		}

		// A tab opened after a deploy finds the worker already waiting.
		if (registration.waiting && navigator.serviceWorker.controller) {
			announce();
		}
		watchInstalling(registration.installing);
		registration.addEventListener("updatefound", () => watchInstalling(registration.installing));

		// A tab left open learns about a build without a navigation.
		const check = () => registration.update().catch(() => {});
		setInterval(check, CHECK_INTERVAL_MS);
		document.addEventListener("visibilitychange", () => {
			if (document.visibilityState === "visible") {
				check();
			}
		});
		return true;
	};

	const reload = () => {
		const waiting = registration && registration.waiting;
		if (!waiting) {
			// Nothing waiting any more (another tab already took the new
			// worker) — a plain reload gets the current build.
			window.location.reload();
			return;
		}
		navigator.serviceWorker.addEventListener(
			"controllerchange",
			() => window.location.reload(),
			{ once: true }
		);
		waiting.postMessage({ type: "SKIP_WAITING" });
	};

	return { start, reload };
})();
