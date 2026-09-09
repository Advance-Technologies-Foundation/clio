"""How many shipped flows carry a diagram LABEL, and what do those labels say?

A flow's caption is not in metadata.json - it is a localizable string, stored in the package's
Resources/<Schema>.Process/resource.<culture>.xml under BaseElements.<FlowName>.Caption.

Run it against a PackageStore checkout:

    python flow-caption-corpus-scan.py [path-to-PackageStore]

The path may also come from the CRT_PACKAGE_STORE environment variable; it defaults to
C:/Projects/PackageStore. Nothing else is needed - this file is self-contained, which it was not
when it was first committed: it `exec`'d two helpers out of a session-scoped scratchpad directory
under one user's AppData\\Local\\Temp, so it raised FileNotFoundError on every other machine and on
the same machine once temp was cleaned. The figures it produces are quoted as fact in shipped MCP
tool descriptions, in package documentation, in a docs/knowledge record and in test descriptions, so
being unable to re-derive them was the real defect rather than the broken import.
"""
import collections
import io
import json
import os
import re
import sys

# The flow-element container inside a process schema's metadata. A sub-process owns its children in
# its OWN BK4, which is why the walk below is recursive rather than a single lookup.
ELEMENTS_KEY = "BK4"

# A flow's kind is read from the CLR CLASS first and the FlowType enum (CI4) second - the order the
# run time reads them, and the order that makes the corpus's one anomaly (a default flow stamped with
# the sequence palette item) land where the platform would put it.
# ProcessSchemaEditSequenceFlowType: Sequence=0 (absent), Default=1, Conditional=2.
FLOW_CLASSES = {
    "Terrasoft.Core.Process.ProcessSchemaSequenceFlow": "sequence",
    "Terrasoft.Core.Process.ProcessSchemaConditionalFlow": "conditional",
}
FLOW_TYPE_DEFAULT = 1


def collect(node, out):
    """Appends every flow-element dict in the schema to `out`, sub-process children included."""
    if isinstance(node, dict):
        _collect_from_dict(node, out)
    elif isinstance(node, list):
        for item in node:
            collect(item, out)


def _collect_from_dict(node, out):
    """The dict half: ELEMENTS_KEY holds flow elements, and every other value is walked through."""
    for key, value in node.items():
        if key != ELEMENTS_KEY or not isinstance(value, list):
            collect(value, out)
            continue
        for item in value:
            if isinstance(item, dict):
                out.append(item)
            # Unconditional, and equivalent: collect() is a no-op on anything that is neither a
            # dict nor a list, so a non-dict entry costs a call and changes nothing.
            collect(item, out)


def kind_of(element):
    """'sequence' | 'conditional' | 'default' for a flow element, None for anything that is not one."""
    kind = FLOW_CLASSES.get(element.get("BL1"))
    if kind is None:
        return None
    if kind == "conditional":
        return "conditional"
    return "default" if element.get("CI4") == FLOW_TYPE_DEFAULT else "sequence"


ROOT = (sys.argv[1] if len(sys.argv) > 1
        else os.environ.get("CRT_PACKAGE_STORE", r"C:/Projects/PackageStore"))
if not os.path.isdir(ROOT):
    raise SystemExit(
        "Not a directory: %s\nPass a PackageStore checkout as the first argument, or set "
        "CRT_PACKAGE_STORE." % ROOT)

ITEM = re.compile(r'<Item\s+Name="BaseElements\.([^".]+)\.Caption"\s+Value="([^"]*)"')

by_kind = collections.Counter()
with_caption = collections.Counter()
samples = collections.defaultdict(list)
schemas = 0
skipped_metadata = []
skipped_resources = []

for dirpath, _, filenames in os.walk(ROOT):
    if "metadata.json" not in filenames:
        continue
    if (os.sep + "Schemas" + os.sep) not in (dirpath + os.sep):
        continue
    try:
        with io.open(os.path.join(dirpath, "metadata.json"), encoding="utf-8-sig") as fh:
            data = json.load(fh)
    except (IOError, OSError, ValueError) as exc:
        # COUNTED, not swallowed. This script solely owns the 84.9% / 25.5% / 0.7% figures now quoted as
        # fact in shipped tool descriptions and in published guidance, and a bare `continue` made a
        # half-read corpus indistinguishable from a complete one - the percentages would simply shift and
        # still look plausible. Narrowed from `except Exception` too, so a defect in this script (a typo,
        # a bad regex) raises instead of silently reducing the denominator.
        skipped_metadata.append((dirpath, str(exc)))
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
            with io.open(res, encoding="utf-8-sig") as fh:      # context-managed: the handle leaked before
                text = fh.read()
            captions = {m.group(1): m.group(2) for m in ITEM.finditer(text)}
        except (IOError, OSError, UnicodeDecodeError) as exc:
            # An unreadable resource file makes every flow in that schema look UNLABELLED, which biases
            # the headline percentages DOWNWARD - the direction that would make the recommendation look
            # weaker than it is. Counted for the same reason as above.
            skipped_resources.append((res, str(exc)))
            captions = {}
    for name, kind in flows.items():
        by_kind[kind] += 1
        cap = (captions.get(name) or "").strip()
        if cap:
            with_caption[kind] += 1
            if len(samples[kind]) < 14:
                samples[kind].append(cap)

print("schemas with flows:", schemas)
# Reported UNCONDITIONALLY, so a clean run states that it was clean rather than staying silent about it.
# A reader comparing two runs' percentages has to know whether both read the same corpus.
print("skipped, unreadable metadata.json:", len(skipped_metadata))
print("skipped, unreadable resource.en-US.xml:", len(skipped_resources))
for path, exc in skipped_metadata[:10]:
    print("   metadata:", path, "->", exc)
for path, exc in skipped_resources[:10]:
    print("   resource:", path, "->", exc)
if skipped_metadata or skipped_resources:
    print("   NOTE: the shares below are computed over what was READ. A skipped resource file makes every")
    print("         flow in that schema look unlabelled, so the labelled shares are a LOWER bound.")
    print("   SCOPE: the skipped metadata.json files are not corrupt - they are the LEGACY key-value")
    print("          metadata format ('= MetaData.Schema.UId \"...\"'), which this scanner does not parse.")
    print("          Measured once: 389 of them on a 7.8.0 branch declare real flows (about 2 280")
    print("          sequence and 138 conditional), and 364 of those 389 are entity-EVENT processes")
    print("          embedded in an entity schema rather than processes anyone built in the designer.")
    print("          So the shares below describe standalone JSON-metadata processes. Including the")
    print("          legacy set would push the SEQUENCE share further down - auto-generated plumbing is")
    print("          not labelled - and add conditional flows whose label rate nobody has measured.")
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
