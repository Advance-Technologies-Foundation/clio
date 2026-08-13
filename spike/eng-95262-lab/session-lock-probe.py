#!/usr/bin/env python3
"""
Creatio session-serialization probe (ENG-95262).

Answers ONE question: does Creatio serialize concurrent DataService requests that share the same
session, the same user, or neither? The answer decides whether clio's MCP server may keep one shared
authenticated session per environment, or must use a pool of independent session lanes.

It deliberately does NOT use clio or creatio.client: raw HTTP only, no auto-redirect, one connection
pool per identity, so nothing in the client library can mask the platform behaviour.

Identities
  A1, A2  two request contexts sharing ONE byte-identical cookie jar from a single login
  B       a second independent login for the SAME user (different session cookies)
  C       an independent login for a DIFFERENT, permission-equivalent user (optional)

Usage
  python3 session-lock-probe.py --url http://host:88/app --user Supervisor --password '...' \
      [--user2 Integration --password2 '...'] [--net-core] [--force-use-session both] \
      [--blocker-schema SysSettings --blocker-column Code --blocker-value SomeCode] \
      [--repeat 30]

Modes
  --mode baseline   no blocker; fire A1/A2/B/C concurrently N times and compare latency distributions.
                    Cheap, needs no DB access, and already shows serialization if it is severe.
  --mode blocked    requires an externally blocked row (hold a DB transaction on it in another
                    session), then measures who waits behind it. This is the decisive test.

Output: one JSON line per probe to stdout, plus a summary table to stderr.
"""

import argparse, concurrent.futures as cf, json, statistics, sys, time, uuid
import http.client, urllib.parse

def _conn(url, timeout):
    u = urllib.parse.urlparse(url)
    host, port = u.hostname, u.port or (443 if u.scheme == "https" else 80)
    if u.scheme == "https":
        import ssl
        ctx = ssl._create_unverified_context()
        return http.client.HTTPSConnection(host, port, timeout=timeout, context=ctx), u.path.rstrip("/")
    return http.client.HTTPConnection(host, port, timeout=timeout), u.path.rstrip("/")

class Identity:
    """One login = one cookie jar = one connection pool (a single keep-alive connection here)."""
    def __init__(self, name, url, user, password, force_use_session, timeout):
        self.name, self.url, self.user, self.password = name, url, user, password
        self.force_use_session, self.timeout = force_use_session, timeout
        self.cookies, self.csrf = {}, None
        self.conn, self.base = _conn(url, timeout)

    def login(self):
        body = json.dumps({"UserName": self.user, "UserPassword": self.password})
        headers = {"Content-Type": "application/json"}
        if self.force_use_session:
            headers["ForceUseSession"] = "true"
        t0 = time.monotonic()
        self.conn.request("POST", f"{self.base}/ServiceModel/AuthService.svc/Login", body, headers)
        r = self.conn.getresponse()
        payload = r.read().decode("utf-8", "replace")
        elapsed = time.monotonic() - t0
        for k, v in r.getheaders():
            if k.lower() == "set-cookie":
                # Each Set-Cookie arrives as its own header tuple; never split on ", " — an
                # `expires=Wed, 21 Oct ...` attribute would be torn in half.
                nv = v.split(";", 1)[0]
                if "=" in nv:
                    name, val = nv.split("=", 1)
                    self.cookies[name.strip()] = val.strip()
        # Creatio 8.2 issues CRT_CSRF alongside the legacy BPMCSRF; send whichever exists.
        self.csrf = self.cookies.get("BPMCSRF") or self.cookies.get("CRT_CSRF")
        try:
            ok = json.loads(payload).get("Code") == 0
        except Exception:                            # noqa: BLE001 - a login page is not JSON
            ok = False
        return {"identity": self.name, "phase": "login", "status": r.status, "elapsed": elapsed,
                "ok": bool(ok), "cookie_names": sorted(self.cookies),
                # fingerprint, never the value
                "session_fp": hash(self.cookies.get(".ASPXAUTH", "")) & 0xffffffff}

    def clone_jar_from(self, other):
        """A2: byte-identical cookies, but its OWN connection — so any waiting is server-side."""
        self.cookies = dict(other.cookies)
        self.csrf = other.csrf

    def select(self, label, query):
        headers = {"Content-Type": "application/json",
                   "Cookie": "; ".join(f"{k}={v}" for k, v in self.cookies.items())}
        if self.csrf:
            headers["BPMCSRF"] = self.csrf
        if self.force_use_session:
            headers["ForceUseSession"] = "true"
        corr = str(uuid.uuid4())
        headers["X-Probe-Correlation"] = corr
        t0 = time.monotonic()
        rec = {"identity": self.name, "label": label, "correlation": corr,
               "sent_at": time.time(), "force_use_session": self.force_use_session}
        try:
            self.conn.request("POST", f"{self.base}/DataService/json/SyncReply/SelectQuery",
                              json.dumps(query), headers)
            r = self.conn.getresponse()
            body = r.read()
            rec.update(status=r.status, location=r.getheader("Location"),
                       content_type=r.getheader("Content-Type"),
                       html=body[:1].decode("latin-1") == "<", bytes=len(body))
        except Exception as ex:                      # noqa: BLE001 - probe records every failure
            rec.update(status=None, error=f"{type(ex).__name__}: {ex}")
            self.conn, self.base = _conn(self.url, self.timeout)   # poisoned connection, replace it
        rec["elapsed"] = time.monotonic() - t0
        return rec

def build_select(schema, column=None, value=None, row_count=1):
    """Minimal DataService SelectQuery. With column/value it targets the deliberately blocked row."""
    q = {"rootSchemaName": schema, "operationType": 0, "rowCount": row_count,
         "columns": {"items": {"Id": {"expression": {"expressionType": 0, "columnPath": "Id"}}}}}
    if column is not None:
        q["filters"] = {"items": {"f": {
            "filterType": 1, "comparisonType": 3,
            "leftExpression": {"expressionType": 0, "columnPath": column},
            "rightExpression": {"expressionType": 2, "parameter": {
                "dataValueType": 1, "value": value}}}},
            "logicalOperation": 0, "isEnabled": True, "filterType": 6}
    return q

def main():
    p = argparse.ArgumentParser()
    p.add_argument("--url", required=True)
    p.add_argument("--user", required=True)
    # Password may come from the environment so it never appears in argv / process listings.
    p.add_argument("--password", default=None)
    p.add_argument("--user2"); p.add_argument("--password2")
    p.add_argument("--mode", choices=["baseline", "blocked"], default="baseline")
    p.add_argument("--force-use-session", choices=["on", "off", "both"], default="both")
    p.add_argument("--probe-schema", default="SysSettings")
    p.add_argument("--blocker-schema", default=None)
    p.add_argument("--blocker-column", default=None)
    p.add_argument("--blocker-value", default=None)
    p.add_argument("--repeat", type=int, default=30)
    p.add_argument("--timeout", type=float, default=60.0)
    a = p.parse_args()

    import os
    a.password = a.password or os.environ.get("PROBE_PASSWORD")
    a.password2 = a.password2 or os.environ.get("PROBE_PASSWORD2")
    if not a.password:
        sys.exit("password required (--password or PROBE_PASSWORD)")

    modes = [True, False] if a.force_use_session == "both" else [a.force_use_session == "on"]
    results = []
    for fus in modes:
        A1 = Identity("A1", a.url, a.user, a.password, fus, a.timeout)
        A2 = Identity("A2", a.url, a.user, a.password, fus, a.timeout)
        B = Identity("B", a.url, a.user, a.password, fus, a.timeout)
        ids = [A1, B]
        C = None
        if a.user2 and a.password2:
            C = Identity("C", a.url, a.user2, a.password2, fus, a.timeout)
            ids.append(C)
        for i in ids:
            rec = i.login(); rec["force_use_session"] = fus
            print(json.dumps(rec), flush=True)
            if not rec["ok"]:
                sys.exit(f"login failed for {i.name}")
        A2.clone_jar_from(A1)      # same cookies, own connection

        probe = build_select(a.probe_schema)
        for n in range(a.repeat):
            if a.mode == "blocked":
                if not (a.blocker_schema and a.blocker_column):
                    sys.exit("--mode blocked needs --blocker-schema/--blocker-column/--blocker-value")
                blocker = build_select(a.blocker_schema, a.blocker_column, a.blocker_value)
                workers = [(A1, "blocker", blocker)]
            else:
                workers = [(A1, "probe", probe)]
            workers += [(A2, "probe", probe), (B, "probe", probe)] + ([(C, "probe", probe)] if C else [])
            with cf.ThreadPoolExecutor(max_workers=len(workers)) as ex:
                futs = []
                for ident, label, q in workers:
                    futs.append(ex.submit(ident.select, label, q))
                    if label == "blocker":
                        time.sleep(0.5)          # make sure the blocker is in flight first
                for f in futs:
                    rec = f.result(); rec["iteration"] = n; rec["force_use_session"] = fus
                    results.append(rec); print(json.dumps(rec), flush=True)

    by = {}
    for r in results:
        if r.get("label") != "probe":
            continue
        by.setdefault((r["force_use_session"], r["identity"]), []).append(r["elapsed"])
    print("\nforce_use_session identity  n   p50      p95      max", file=sys.stderr)
    for (fus, ident), xs in sorted(by.items()):
        xs.sort()
        p50 = statistics.median(xs)
        p95 = xs[min(len(xs) - 1, int(len(xs) * 0.95))]
        print(f"{str(fus):<18} {ident:<9} {len(xs):<3} {p50:7.3f}s {p95:7.3f}s {max(xs):7.3f}s",
              file=sys.stderr)
    print("\nRead: if A2 is far slower than B (and C), the platform serializes per SESSION.\n"
          "If A2 and B are both slow but C is fast, it serializes per USER.\n"
          "If all are equal, this path does not serialize and the wedge is purely clio-side.",
          file=sys.stderr)

if __name__ == "__main__":
    main()
