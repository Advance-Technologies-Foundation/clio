#!/usr/bin/env python3
"""
Deterministic Creatio stub for the ENG-95262 prototype.

Gives the spike a backend whose behaviour is exactly controllable, so the three load-bearing
assumptions of the plan can be tested without depending on a real stand:

  POST /ServiceModel/AuthService.svc/Login  -> counts the call, sets .ASPXAUTH + BPMCSRF, {"Code":0}
  POST /0/DataService/json/SyncReply/SelectQuery
        ?delay=<seconds>                    -> responds after <seconds> (default 0)
        ?mode=stall-headers                 -> accepts and NEVER responds (a wedged backend)
        ?mode=stall-body                    -> sends headers, then stalls mid-body
  GET  /counters                            -> {"login": n, "select": n}  (and resets nothing)
  POST /reset                               -> zeroes the counters

Single-threaded state, ThreadingHTTPServer so concurrent requests are genuinely concurrent.
"""
import json, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

COUNTERS = {"login": 0, "select": 0}
# Global switches, set through POST /control. clio builds its own URLs, so per-request query flags are
# unreachable from the MCP path — the stall has to be a server-side mode.
STATE = {"stall": False, "delay": 0.0, "login_delay": 0.2}
LOCK = threading.Lock()

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):        # keep stdout clean; the spike is the thing being measured
        pass

    def _bump(self, key):
        with LOCK:
            COUNTERS[key] += 1
            return COUNTERS[key]

    def _json(self, payload, extra_headers=()):
        body = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        for k, v in extra_headers:
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        u = urlparse(self.path)
        if u.path == "/counters":
            with LOCK:
                self._json(dict(COUNTERS))
            return
        if u.path.endswith("/ping") or u.path == "/0/ping":
            self._json({"ok": True})
            return
        self._json({"success": True, "rows": []})

    def do_POST(self):
        u = urlparse(self.path)
        q = parse_qs(u.query)
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)

        if u.path == "/reset":
            with LOCK:
                COUNTERS["login"] = COUNTERS["select"] = 0
            self._json({"ok": True})
            return

        if u.path == "/control":
            # body was already consumed above; take the switches from the query string
            with LOCK:
                if "stall" in q:
                    STATE["stall"] = q["stall"][0].lower() in ("1", "true", "yes")
                if "delay" in q:
                    STATE["delay"] = float(q["delay"][0])
                if "login_delay" in q:
                    STATE["login_delay"] = float(q["login_delay"][0])
                self._json(dict(STATE))
            return

        if u.path.endswith("/AuthService.svc/Login"):
            n = self._bump("login")
            # Deliberately slow: a real cold login was measured at 3.6-19.4 s. Enough delay to
            # expose a stampede without making the run long.
            time.sleep(float(q.get("logindelay", [str(STATE["login_delay"])])[0]))
            self._json({"Code": 0, "Message": "", "n": n}, extra_headers=[
                ("Set-Cookie", ".ASPXAUTH=stub-auth-cookie; path=/; HttpOnly"),
                ("Set-Cookie", "BPMCSRF=stub-csrf; path=/"),
            ])
            return

        if "SelectQuery" in u.path:
            self._bump("select")
            mode = q.get("mode", [""])[0]
            if not mode and STATE["stall"]:
                mode = "stall-headers"       # global switch used by the clio MCP wedge harness
            if mode == "stall-headers":
                # Accept the request and never answer: the wedged-backend case.
                while True:
                    time.sleep(3600)
            if mode == "stall-body":
                # Headers + a partial body, then stall — tests whether the client's timeout
                # covers the response READ, not only the header exchange.
                self.send_response(200)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", "100000")
                self.end_headers()
                self.wfile.write(b'{"success":true,"rows":[')
                self.wfile.flush()
                while True:
                    time.sleep(3600)
            time.sleep(float(q.get("delay", ["0"])[0]))
            self._json({"success": True, "rows": [{"Id": "00000000-0000-0000-0000-000000000001"}]})
            return

        # Generic fallback (build/compile endpoints and anything else clio posts). Honours the global
        # delay so a long operation can be simulated without a real Creatio build.
        if STATE["delay"]:
            time.sleep(STATE["delay"])
        self._json({"success": True, "rows": []})

if __name__ == "__main__":
    import sys
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8099
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
