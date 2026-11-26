// #1509 (D5): the Markdown input's toolbar. Wraps the text area's selection in `before`/`after` (bold, italic), or
// prefixes each selected line with `linePrefix` (heading, lists; "1. " numbers the lines 1., 2., ...). Writes the new
// text into the element, restores a sensible selection, and returns the new value and selection so .NET can report it.
window.consultologistMarkdown = {
    wrapSelection: (element, before, after, linePrefix) => {
        if (!element) {
            return null;
        }
        const text = element.value ?? "";
        const start = element.selectionStart ?? text.length;
        const end = element.selectionEnd ?? start;
        let value;
        let selectionStart;
        let selectionEnd;
        if (linePrefix) {
            const from = text.lastIndexOf("\n", start - 1) + 1;
            const lineEnd = text.indexOf("\n", end);
            const to = lineEnd === -1 ? text.length : lineEnd;
            const numbered = /^\d+\. $/.test(linePrefix);
            const lines = text.substring(from, to).split("\n")
                .map((line, index) => (numbered ? `${index + 1}. ` : linePrefix) + line);
            const block = lines.join("\n");
            value = text.substring(0, from) + block + text.substring(to);
            selectionStart = from;
            selectionEnd = from + block.length;
        } else {
            const chosen = text.substring(start, end);
            value = text.substring(0, start) + before + chosen + after + text.substring(end);
            selectionStart = start + before.length;
            selectionEnd = selectionStart + chosen.length;
        }
        element.value = value;
        element.focus();
        element.setSelectionRange(selectionStart, selectionEnd);
        return { value, selectionStart, selectionEnd };
    }
};
