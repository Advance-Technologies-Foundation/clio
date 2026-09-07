"""How many shipped flows carry a diagram LABEL, and what do those labels say?

A flow's caption is not in metadata.json - it is a localizable string, stored in the package's
Resources/<Schema>.Process/resource.<culture>.xml under BaseElements.<FlowName>.Caption.
"""
import collections
import io
import json
import os
import re

exec(open(r"C:/Users/D8671~1.KRE/AppData/Local/Temp/claude/C--Projects-clio/9f00abfa-1ac1-42fc-b0fa-fbbf43a0e380/scratchpad/scan_h1.py")
     .read().split("h1, h2_gateway")[0].split('"""', 2)[2])

ROOT = r"C:/Projects/PackageStore"
ITEM = re.compile(r'<Item\s+Name="BaseElements\.([^".]+)\.Caption"\s+Value="([^"]*)"')

by_kind = collections.Counter()
with_caption = collections.Counter()
samples = collections.defaultdict(list)
schemas = 0

for dirpath, _, filenames in os.walk(ROOT):
    if "metadata.json" not in filenames:
        continue
    if (os.sep + "Schemas" + os.sep) not in (dirpath + os.sep):
        continue
    try:
        with io.open(os.path.join(dirpath, "metadata.json"), encoding="utf-8-sig") as fh:
            data = json.load(fh)
    except Exception:
        continue
    elements = []
    collect(data, elements)
    flows = {}
    for el in elements:
        k = kind_of(el)
        if k and el.get("A2"):
            flows[el["A2"]] = k
    if not flows:
        continue
    schemas += 1
    schema_name = os.path.basename(dirpath)
    branch_root = os.path.dirname(os.path.dirname(dirpath))          # .../<pkg>/branches/<ver>
    res = os.path.join(branch_root, "Resources", schema_name + ".Process", "resource.en-US.xml")
    captions = {}
    if os.path.exists(res):
        try:
            text = io.open(res, encoding="utf-8-sig").read()
            captions = {m.group(1): m.group(2) for m in ITEM.finditer(text)}
        except Exception:
            captions = {}
    for name, kind in flows.items():
        by_kind[kind] += 1
        cap = (captions.get(name) or "").strip()
        if cap:
            with_caption[kind] += 1
            if len(samples[kind]) < 14:
                samples[kind].append(cap)

print("schemas with flows:", schemas)
print()
print("%-12s %8s %8s %7s" % ("kind", "flows", "labelled", "share"))
total = labelled = 0
for kind in ("conditional", "default", "sequence"):
    n, c = by_kind[kind], with_caption[kind]
    total += n
    labelled += c
    print("%-12s %8d %8d %6.1f%%" % (kind, n, c, (100.0 * c / n) if n else 0))
print("%-12s %8d %8d %6.1f%%" % ("ALL", total, labelled, (100.0 * labelled / total) if total else 0))
print()
for kind in ("conditional", "default", "sequence"):
    if samples[kind]:
        print("--- %s labels, as shipped ---" % kind)
        for cap in samples[kind]:
            print("   ", cap[:90])
        print()
