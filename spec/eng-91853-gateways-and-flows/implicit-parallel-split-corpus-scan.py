"""How many SHIPPED processes contain an implicit parallel split, and on what kind of element?

An implicit parallel split is clio's R12: a NON-gateway element with more than one outgoing plain
`sequence` flow. Nothing decides between them, so the platform starts every one - `FlowSchema.
GetNextFlowElements` returns every outgoing flow's target and performs no selection at all.

The figure this produces decides a SEVERITY, which is why it is a committed script rather than a
number in a commit message. `CrtProcessBuilder` raises a notice for the shape instead of refusing it,
and the only argument for that is the count below: refusing would reject shipped content that runs.
The same count is quoted on `IProcessGraphBuilder.ReportImplicitParallelSplits`, in the divergence
docblock, in the bundled-archive provenance pin on the clio side and in two test descriptions, so being
unable to re-derive it would be the real defect - see the sibling script
`flow-caption-corpus-scan.py`, whose docstring says the same thing after it happened there.

Run it against a PackageStore checkout:

    python implicit-parallel-split-corpus-scan.py [path-to-PackageStore]

The path may also come from the CRT_PACKAGE_STORE environment variable; it defaults to
C:/Projects/PackageStore.
"""
import collections
import io
import json
import os
import sys

# The flow-element container inside a process schema's metadata. A sub-process owns its children in
# its OWN BK4, which is why the walk below is recursive rather than a single lookup.
ELEMENTS_KEY = "BK4"

# A flow's endpoints. CI1 is the source and CI2 the target - confirmed by chaining: the flow whose
# CI1 is the start event's UId has a CI2 that is the next flow's CI1.
SOURCE_KEY = "CI1"

# A flow's kind is read from the CLR CLASS first and the FlowType enum (CI4) second - the order the
# run time reads them. ProcessSchemaEditSequenceFlowType: Sequence=0 (absent), Default=1.
FLOW_CLASSES = {
    "Terrasoft.Core.Process.ProcessSchemaSequenceFlow": "sequence",
    "Terrasoft.Core.Process.ProcessSchemaConditionalFlow": "conditional",
}
FLOW_TYPE_DEFAULT = 1


def collect_flow_elements(node, out):
    """Appends every flow-element dict under an ELEMENTS_KEY list, sub-process children included."""
    if isinstance(node, dict):
        for key, value in node.items():
            if key == ELEMENTS_KEY and isinstance(value, list):
                for item in value:
                    if isinstance(item, dict):
                        out.append(item)
                    collect_flow_elements(item, out)
            else:
                collect_flow_elements(value, out)
    elif isinstance(node, list):
        for item in node:
            collect_flow_elements(item, out)


def index_every_node(node, out):
    """UId -> element for EVERY dict in the tree carrying a UId and a class, not just BK4 children.

    Indexing BK4 alone is the trap that makes this scan quietly wrong, and it is wrong in BOTH
    directions at once. An entity schema inherits process elements that live outside its own BK4
    (`BaseEntity`'s `BaseEntityStartMessage1`, `BaseEditPage`'s `OpenMessageUserTask223`), so 14
    sources resolved to nothing - they were still COUNTED, because an unresolved source is not a
    gateway, and they were reported under a made-up element class. One of those 14 turned out to be
    a gateway once resolved, which is an over-count the other way. Measured: the BK4-only index
    reported 75 sources and a class histogram naming a class that does not exist; this one reports
    74 and resolves every source.
    """
    if isinstance(node, dict):
        if node.get("UId") and node.get("BL1"):
            out[node["UId"]] = node
        for value in node.values():
            index_every_node(value, out)
    elif isinstance(node, list):
        for item in node:
            index_every_node(item, out)


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

schemas = 0
skipped_metadata = []
by_source_class = collections.Counter()
broad_by_source_class = collections.Counter()
unresolved = []
examples = []

for dirpath, _, filenames in os.walk(ROOT):
    if "metadata.json" not in filenames:
        continue
    if (os.sep + "Schemas" + os.sep) not in (dirpath + os.sep):
        continue
    try:
        with io.open(os.path.join(dirpath, "metadata.json"), encoding="utf-8-sig") as fh:
            data = json.load(fh)
    except (IOError, OSError, ValueError) as exc:
        # COUNTED, not swallowed, for the reason the sibling script gives: a half-read corpus is
        # otherwise indistinguishable from a complete one, and the number would simply shift and
        # still look plausible. Narrowed from `except Exception` so a defect in this script raises.
        skipped_metadata.append((dirpath, str(exc)))
        continue

    elements = []
    collect_flow_elements(data, elements)
    flows = [(element, kind) for element in elements if kind_of(element)
             for kind in [kind_of(element)]]
    if not flows:
        continue
    schemas += 1

    nodes = {}
    index_every_node(data, nodes)
    kinds_by_source = collections.defaultdict(list)
    for element, kind in flows:
        source_uid = element.get(SOURCE_KEY)
        if source_uid:
            kinds_by_source[source_uid].append(kind)

    package_path = dirpath.replace("\\", "/").split("/PackageStore/")[-1]
    for source_uid, kinds in kinds_by_source.items():
        source = nodes.get(source_uid)
        source_class = (source or {}).get("BL1") or "<unresolved>"
        short_class = source_class.rsplit(".", 1)[-1]
        if "Gateway" in source_class:
            # A gateway is excluded by R12 itself: a parallel or event-based one with several plain
            # flows is the EXPLICIT split, and a deciding one cannot hold two plain flows at all.
            continue
        if source_class == "<unresolved>":
            unresolved.append((package_path, source_uid))
        plain = kinds.count("sequence")
        unconditional = plain + kinds.count("default")
        if plain > 1:
            by_source_class[short_class] += 1
            if len(examples) < 12:
                examples.append((package_path, (source or {}).get("A2"), short_class, sorted(kinds)))
        if unconditional > 1:
            broad_by_source_class[short_class] += 1

total = sum(by_source_class.values())
broad_total = sum(broad_by_source_class.values())
print("schemas with flows:", schemas)
print("skipped, legacy key-value metadata (not parsed by this scanner):", len(skipped_metadata))
# Reported UNCONDITIONALLY so a clean run states that it was clean. An unresolved source is counted
# as a non-gateway, so it inflates the headline - the number has to be visible, not inferred.
print("sources whose element could not be resolved:", len(unresolved))
for package_path, source_uid in unresolved[:10]:
    print("   ", package_path, source_uid)
print()

print("R12 - NON-gateway sources with MORE THAN ONE outgoing plain 'sequence' flow:", total)
for short_class, count in by_source_class.most_common():
    print("   %-34s %d" % (short_class, count))
print()
# The reachable population is what decides the severity. A start event with a second outgoing flow
# is already refused on both sides - clio's R1, and ProcessGraphBuilder.ValidateStructure's "start
# event 'X' must have a single outgoing flow" - so it can never reach the notice through a build.
starts = sum(count for short_class, count in by_source_class.items() if "Start" in short_class)
print("   of which START events, unreachable through a BUILD (start-arity rule):", starts)
print("   REACHABLE on the build path - ordinary activities, the severity population:", total - starts)
print()

print("BROAD reading - counting a 'default' flow as unconditional too:", broad_total)
print("   difference from R12's strictly-'sequence' predicate:", broad_total - total)
print("   (widening to this reading would trade a closed divergence for a new one pointing the")
print("    other way, since clio's R12 counts only flows whose kind is 'sequence')")
print()

print("--- examples ---")
for package_path, name, short_class, kinds in examples:
    print("   %-64s %-26s %-30s %s" % (package_path, name, short_class, kinds))
