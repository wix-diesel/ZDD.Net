# Builds the TdZdd side of the Graphillion/TdZdd comparison (issue #51 / M4-8).
# See ../README.md for how to obtain TdZdd itself; this Makefile only compiles the
# handful of *.cpp files in this directory against its headers (TdZdd is header-only).
#
#   make TDZDD_INCLUDE=/path/to/TdZdd/include
#
# TDZDD_INCLUDE defaults to a TdZdd checkout as a sibling of this repository's clone
# (../../../../TdZdd/include, i.e. next to the ZDD.Net checkout, matching the `cd ..`
# step in ../README.md's setup instructions) — override it if TdZdd lives elsewhere.
TDZDD_INCLUDE ?= ../../../../TdZdd/include

CXX      ?= g++
CXXFLAGS += -std=c++17 -O3 -DNDEBUG -Wall -I$(TDZDD_INCLUDE)

TARGETS = gen_grid gen_complete grid_paths spanning_tree matching independent_set cardinality

.PHONY: all clean
all: $(TARGETS)

gen_grid: gen_grid.cpp
	$(CXX) $(CXXFLAGS) -o $@ $<

gen_complete: gen_complete.cpp
	$(CXX) $(CXXFLAGS) -o $@ $<

grid_paths: grid_paths.cpp common.hpp
	$(CXX) $(CXXFLAGS) -o $@ $<

spanning_tree: spanning_tree.cpp common.hpp
	$(CXX) $(CXXFLAGS) -o $@ $<

matching: matching.cpp common.hpp
	$(CXX) $(CXXFLAGS) -o $@ $<

independent_set: independent_set.cpp common.hpp
	$(CXX) $(CXXFLAGS) -o $@ $<

cardinality: cardinality.cpp common.hpp
	$(CXX) $(CXXFLAGS) -o $@ $<

clean:
	$(RM) $(TARGETS) *.dat
