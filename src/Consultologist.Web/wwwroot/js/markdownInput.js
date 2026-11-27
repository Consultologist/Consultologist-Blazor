// #1509 (D5): the Markdown input's toolbar. Wraps the text area's selection in `before`/`after` (bold, italic), or
// prefixes each selected line with `linePrefix` (heading, lists; "1. " numbers the lines 1., 2., ...). Writes the new
// text into the control, restores a sensible selection, and returns the new value and selection so .NET can report it.
// `element` is Fluent's <fluent-text-area> host, which has `value` and `control` but no selection API: the real
// <textarea> is its `control` (FAST), found in its shadow root or its light DOM otherwise. Any failure returns null.
window.consultologistMarkdown = {
    wrapSelection: (element, before, after, linePrefix) => {
        try {
            const control = element?.control
                ?? element?.shadowRoot?.querySelector("textarea")
                ?? element?.querySelector?.("textarea")
                ?? element;
            if (!control || typeof control.setSelectionRange !== "function") {
                return null;
            }
            const text = control.value ?? "";
            const start = control.selectionStart ?? text.length;
            const end = control.selectionEnd ?? start;
            let value;
            let selectionStart;
            let selectionEnd;
            if (linePrefix) {
                const from = start === 0 ? 0 : text.lastIndexOf("\n", start - 1) + 1;
                // A selection that ends just after a newline does not reach the following line.
                const last = end > start && text[end - 1] === "\n" ? end - 1 : end;
                const lineEnd = text.indexOf("\n", last);
                const to = lineEnd === -1 ? text.length : lineEnd;
                const numbered = /^\d+\. $/.test(linePrefix);
                const block = text.substring(from, to).split("\n")
                    .map((line, index) => (numbered ? `${index + 1}. ` : linePrefix) + line)
                    .join("\n");
                value = text.substring(0, from) + block + text.substring(to);
                selectionStart = from;
                selectionEnd = from + block.length;
            } else {
                const chosen = text.substring(start, end);
                value = text.substring(0, start) + before + chosen + after + text.substring(end);
                selectionStart = start + before.length;
                selectionEnd = selectionStart + chosen.length;
            }
            control.value = value;
            // Keep the web component's own value in step with its textarea.
            control.dispatchEvent(new Event("input", { bubbles: true, composed: true }));
            control.focus();
            control.setSelectionRange(selectionStart, selectionEnd);
            return { value, selectionStart, selectionEnd };
        } catch {
            return null;
        }
    }
};
