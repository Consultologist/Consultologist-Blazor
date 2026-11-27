// Mermaid rendering interop (#114). The ~5.5 MB vendored library is loaded on
// demand — the first Graph-pane open pays for it, nobody else does.
window.consultologistMermaid = {
	_loading: null,

	ensure: function () {
		if (window.mermaid) {
			return Promise.resolve();
		}

		if (!this._loading) {
			this._loading = new Promise((resolve, reject) => {
				const script = document.createElement("script");
				script.src = "lib/mermaid.min.js";
				script.onload = () => resolve();
				script.onerror = () => reject(new Error("mermaid.min.js failed to load"));
				document.head.appendChild(script);
			});
		}

		return this._loading;
	},

	render: async function (text) {
		await this.ensure();
		// #1510 (D1): Mermaid 12's full build bundles ELK; every diagram is laid out with it, frontmatter or not.
		window.mermaid.initialize({ startOnLoad: false, theme: "neutral", securityLevel: "strict", layout: "elk" });
		const id = "workflow-dag-" + Math.random().toString(36).slice(2, 10);
		const { svg } = await window.mermaid.render(id, text);
		return svg;
	},

	// #1510 (D12): Mermaid stays strict, which turns its own `click` off, so a run diagram's linked boxes are bound
	// here, once the SVG is on the page. Mermaid 12 writes a box as <g class="node" id="…flowchart-<box id>-<n>">;
	// each box `anchors` names becomes a link (a pointer, role="link", in the Tab order) whose click or Enter calls
	// `view.Follow(<anchor>)`. Binding again, or unbind, drops the handlers of the drawing before.
	bind: function (canvas, anchors, view) {
		this.unbind(canvas);
		if (!canvas || !anchors) {
			return;
		}

		const links = new AbortController();
		canvas._consultologistLinks = links;
		for (const box of canvas.querySelectorAll("g.node[id]")) {
			const match = /(?:^|-)flowchart-([A-Za-z0-9_]+)-\d+$/.exec(box.id);
			const anchor = match ? anchors[match[1]] : undefined;
			if (!anchor) {
				continue;
			}

			box.style.cursor = "pointer";
			box.setAttribute("role", "link");
			box.setAttribute("tabindex", "0");
			// A click after the view is gone (its .NET reference disposed) has nothing to call: it does nothing.
			const follow = () => view.invokeMethodAsync("Follow", anchor).catch(() => {});
			box.addEventListener("click", follow, { signal: links.signal });
			box.addEventListener("keydown", (event) => {
				if (event.key === "Enter") {
					event.preventDefault();
					follow();
				}
			}, { signal: links.signal });
		}
	},

	unbind: function (canvas) {
		if (canvas && canvas._consultologistLinks) {
			canvas._consultologistLinks.abort();
			delete canvas._consultologistLinks;
		}
	}
};
