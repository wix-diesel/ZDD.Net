// Shared timing/memory helpers for the TdZdd comparison programs (issue #51 / M4-8).
// Not part of TdZdd itself — just a few lines every program here needs, kept in one place
// so the individual cases stay focused on the spec being measured.
#pragma once

#include <chrono>
#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>

namespace bench {

class Timer {
public:
    Timer() : start_(std::chrono::steady_clock::now()) {}

    double elapsedMs() const {
        auto now = std::chrono::steady_clock::now();
        return std::chrono::duration<double, std::milli>(now - start_).count();
    }

private:
    std::chrono::steady_clock::time_point start_;
};

// Peak resident set size in KB, read from /proc/self/status (Linux only — this repo's
// measurement environment throughout docs/benchmarks.md is Ubuntu, so no portability
// shim is needed). This is the same figure `/usr/bin/time -v` reports as "Maximum
// resident set size", used here so a single run prints a self-contained result without
// requiring the caller to wrap it externally.
inline long peakRssKb() {
    std::ifstream status("/proc/self/status");
    std::string line;
    while (std::getline(status, line)) {
        if (line.compare(0, 6, "VmHWM:") == 0) {
            std::istringstream iss(line.substr(6));
            long kb = 0;
            iss >> kb;
            return kb;
        }
    }
    return -1;
}

inline void report(char const* caseName, double elapsedMs, std::string const& count, size_t nodeCount) {
    std::printf(
        "%-28s elapsed=%10.2f ms  peakRSS=%8ld KB  nodes=%10zu  count=%s\n",
        caseName, elapsedMs, peakRssKb(), nodeCount, count.c_str());
}

} // namespace bench
