"""Reading the shipped-process corpus: the metadata vocabulary and the walk both scans in this folder use.

Extracted because the two scans had grown identical copies of it - the constants, `kind_of`, the root
resolution and the walk - and a divergence between those copies would not look like a bug. It would look
like two numbers that disagree, each quoted as fact somewhere different: the caption shares live in shipped
MCP tool descriptions and published guidance, the split count picks a SEVERITY in package source and in the
validate-vs-build divergence docblock. Whichever copy drifted, the wrong figure would keep its provenance.

A COMMITTED sibling, and that distinction is the whole reason this file is allowed to exist. The caption
scan once `exec`'d its helpers out of a session-scoped scratchpad under one user's AppData\\Local\\Temp, so
it raised FileNotFoundError on every other machine and on the same machine once temp was cleaned; being
unable to re-derive a published figure was the real defect there, not the broken import. This module sits
beside its callers in git, and Python puts a script's own directory at the front of `sys.path`, so
`import process_corpus` resolves for `python <anywhere>/<scan>.py` with no path handling in either script.
"""
import io
import json
import os

# The flow-element container inside a process schema's metadata. A sub-process owns its children in
# its OWN BK4, which is why the walk below is recursive rather than a single lookup.
ELEMENTS_KEY = "BK4"

# A flow's endpoints. CI1 is the source and CI2 the target - confirmed by chaining: the flow whose
# CI1 is the start event's UId has a CI2 that is the next flow's CI1.
SOURCE_KEY = "CI1"

# A flow's kind is read from the CLR CLASS first and the FlowType enum (CI4) second - the order the
# run time reads them, and the order that makes the corpus's one anomaly (a default flow stamped with
# the sequence palette item) land where the platform would put it.
# ProcessSchemaEditSequenceFlowType: Sequence=0 (absent), Default=1, Conditional=2.
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
                    # Unconditional, and equivalent: this call is a no-op on anything that is neither
                    # a dict nor a list, so a non-dict entry costs a call and changes nothing.
                    collect_flow_elements(item, out)
            else:
                collect_flow_elements(value, out)
    elif isinstance(node, list):
        for item in node:
            collect_flow_elements(item, out)


def kind_of(element):
    """'sequence' | 'conditional' | 'default' for a flow element, None for anything that is not one."""
    kind = FLOW_CLASSES.get(element.get("BL1"))
    if kind is None:
        return None
    if kind == "conditional":
        return "conditional"
    return "default" if element.get("CI4") == FLOW_TYPE_DEFAULT else "sequence"


def resolve_root(argv):
    """The corpus root from argv[1], else CRT_PACKAGE_STORE, else the conventional local path."""
    root = (argv[1] if len(argv) > 1
            else os.environ.get("CRT_PACKAGE_STORE", r"C:/Projects/PackageStore"))
    if not os.path.isdir(root):
        raise SystemExit(
            "Not a directory: %s\nPass a PackageStore checkout as the first argument, or set "
            "CRT_PACKAGE_STORE." % root)
    return root


def read_schemas(root, skipped_metadata):
    """Yields (dirpath, parsed metadata.json) for every schema folder under `root`.

    Unreadable metadata is COUNTED into `skipped_metadata`, not swallowed: a half-read corpus is
    otherwise indistinguishable from a complete one, and the figures would simply shift and still look
    plausible. The exception list is narrow rather than `except Exception` so a defect in a caller - a
    typo, a bad regex - raises instead of silently reducing the denominator.
    """
    for dirpath, _, filenames in os.walk(root):
        if "metadata.json" not in filenames:
            continue
        if (os.sep + "Schemas" + os.sep) not in (dirpath + os.sep):
            continue
        try:
            with io.open(os.path.join(dirpath, "metadata.json"), encoding="utf-8-sig") as fh:
                data = json.load(fh)
        except (IOError, OSError, ValueError) as exc:
            skipped_metadata.append((dirpath, str(exc)))
            continue
        yield dirpath, data
