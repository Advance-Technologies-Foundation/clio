import json, subprocess, sys, datetime, collections

def api(path):
    out = subprocess.run(["curl","-sS","--cacert","/root/.ccr/ca-bundle.crt",
        "-H","Accept: application/vnd.github+json",
        f"https://api.github.com/repos/Advance-Technologies-Foundation/clio{path}"],
        capture_output=True, text=True).stdout
    try: return json.loads(out)
    except Exception: return None

runs = []
for page in (1,2,3):
    d = api(f"/actions/workflows/teamcity-mcp-e2e.yml/runs?per_page=100&page={page}")
    if not d or "workflow_runs" not in d: break
    runs += d["workflow_runs"]
seen, rows = set(), []
for r in runs:
    sha, branch = r["head_sha"], r["head_branch"]
    if sha in seen: continue
    seen.add(sha)
    st = api(f"/commits/{sha}/statuses?per_page=30")
    if not isinstance(st, list): continue
    tc = [s for s in st if s["context"] == "CLIO MCP e2e tests (ATF)"]
    by_build = collections.defaultdict(list)
    for s in tc:
        by_build[s["target_url"].rsplit("/",1)[-1]].append(s)
    for build, entries in by_build.items():
        started = [e for e in entries if e["description"] == "TeamCity build started"]
        final = [e for e in entries if e["state"] in ("success","failure","error")]
        if not started or not final: continue
        t0 = datetime.datetime.fromisoformat(started[0]["created_at"].replace("Z","+00:00"))
        t1 = datetime.datetime.fromisoformat(final[0]["created_at"].replace("Z","+00:00"))
        mins = (t1-t0).total_seconds()/60
        if mins <= 0: continue
        rows.append((mins, final[0]["state"], build, branch, t0.strftime("%m-%d %H:%M")))
    if len(rows) >= 45: break
rows.sort(key=lambda r: r[4])
mine = "claude/repo-update-z18z41"
print(f"{'start':<12} {'build':<10} {'state':<8} {'min':>6}  branch")
for m,st,b,br,t in rows:
    tag = " <== MINE" if br == mine else ""
    print(f"{t:<12} {b:<10} {st:<8} {m:6.1f}  {br[:38]}{tag}")
ok_mine = [m for m,st,b,br,t in rows if br==mine and st=="success"]
ok_other = [m for m,st,b,br,t in rows if br!=mine and st=="success"]
print()
print(f"successful runs, MINE   n={len(ok_mine)}  min={min(ok_mine):.1f} max={max(ok_mine):.1f} mean={sum(ok_mine)/len(ok_mine):.1f}" if ok_mine else "no mine")
print(f"successful runs, OTHERS n={len(ok_other)}  min={min(ok_other):.1f} max={max(ok_other):.1f} mean={sum(ok_other)/len(ok_other):.1f}" if ok_other else "no others")
