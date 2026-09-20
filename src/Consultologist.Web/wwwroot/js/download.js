// #808: trigger a browser download of bytes produced in .NET (base64-encoded),
// e.g. a zip of the package being edited. No library — an object URL on a
// transient anchor, revoked once the click is dispatched.
window.consultologistDownload = (fileName, mimeType, base64) => {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType || "application/octet-stream" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
};
