"""Shared timing/memory helpers for the Graphillion comparison scripts (issue #51 /
M4-8). Not part of Graphillion itself — just what every case here needs, kept in one
place so the individual scripts stay focused on the GraphSet/VertexSetSet call being
measured.
"""
import resource
import time


def peak_rss_kb():
    """Peak resident set size in KB — ru_maxrss is already KB on Linux (it is bytes on
    macOS; this repo's measurement environment is Ubuntu throughout docs/benchmarks.md,
    so no platform check is needed here)."""
    return resource.getrusage(resource.RUSAGE_SELF).ru_maxrss


def report(case_name, elapsed_ms, count, node_count=None):
    nodes = f"{node_count:>10}" if node_count is not None else " " * 10
    print(f"{case_name:<28} elapsed={elapsed_ms:10.2f} ms  peakRSS={peak_rss_kb():8d} KB  "
          f"nodes={nodes}  count={count}")


class Timer:
    def __init__(self):
        self.start = time.perf_counter()

    def elapsed_ms(self):
        return (time.perf_counter() - self.start) * 1000.0
