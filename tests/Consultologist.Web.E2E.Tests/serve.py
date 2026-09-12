#!/usr/bin/env python3
# #710: a static server with SPA fallback for the E2E harness. Blazor deep
# links (/history, /consults/{id}) are client routes with no file on disk, so a
# plain http.server 404s them; fall back to index.html and let the router take
# over. Real assets (framework dlls, js, css) still serve from disk.
import os
import sys
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

port = int(sys.argv[1])
root = sys.argv[2]


class SpaHandler(SimpleHTTPRequestHandler):
    def do_GET(self):
        if not os.path.isfile(self.translate_path(self.path)):
            self.path = "/index.html"
        return super().do_GET()


handler = partial(SpaHandler, directory=root)
ThreadingHTTPServer(("127.0.0.1", port), handler).serve_forever()
