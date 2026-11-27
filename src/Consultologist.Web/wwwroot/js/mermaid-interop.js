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
	}
};
