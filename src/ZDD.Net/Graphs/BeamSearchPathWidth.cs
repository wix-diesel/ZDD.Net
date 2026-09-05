using System;
using System.Collections.Generic;
using System.Threading;

namespace ZDD.Net.Graphs
{
    /// <summary>
    /// <see cref="EdgeOrderStrategy.BeamSearchPathWidth"/>: approximates the vertex order that minimizes
    /// the peak frontier (the graph's pathwidth) with a beam search, since the exact problem is NP-hard
    /// (M3-3, PLAN.md §8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>State and candidates.</b> The search state is the set of vertices visited so far; a step picks
    /// the next vertex to add. A candidate is restricted to an unvisited vertex adjacent to the visited
    /// region (or, once that region has no more unvisited neighbors, the lowest-degree unvisited vertex,
    /// which starts a new region on a disconnected graph) — adding a vertex unrelated to anything visited
    /// so far could never narrow the current frontier, only add an unrelated one, so this restriction both
    /// keeps branching cheap (bounded by the region's boundary degree, not <c>VertexCount</c>) and does not
    /// throw away any move that could help. Picking that lowest-degree restart is itself
    /// <c>O(VertexCount)</c> amortized over a whole trial, not per restart: <see cref="Node.RestartCursor"/>
    /// walks a degree-sorted vertex list computed once (<see cref="VerticesByDegreeAscending"/>) and only
    /// ever advances, so a graph with many disconnected components does not turn into
    /// <c>O(VertexCount²)</c> just to find where each one starts.
    /// </para>
    /// <para>
    /// <b>Evaluation.</b> A vertex enters the frontier the moment one of its edges resolves (both endpoints
    /// visited) and leaves once every one of its edges has — the same notion
    /// <see cref="EdgeOrdering.MaxFrontierSize"/> computes exactly from a finished edge order, tracked here
    /// incrementally as the vertex order is built (<see cref="Predict"/> / <see cref="Apply"/>). Candidates
    /// are ranked primarily by <b>the worst width reached so far</b> (the peak is what sits on the exponent
    /// of a build's cost, PLAN.md §8, so a path that is usually narrow but spikes once is worse than one
    /// that stays uniformly moderate) and, among ties, by <b>graph distance from the start vertex</b>, then
    /// <b>the running total</b>, then the candidate vertex's own index — and an insertion-order number as
    /// the final tie-break, since two different parent states can otherwise reach the same vertex with an
    /// identical score, which <c>List&lt;T&gt;.Sort</c> alone would not resolve deterministically. The
    /// distance tie-break matters more than it looks: minimizing width one vertex at a time with no other
    /// guidance is a known trap on graphs with real local structure (unlike a grid) — it tends to wander
    /// into a locally cheap-looking peninsula and leave the graph's bulk to be swept up later at a much
    /// wider frontier, which is worse than BFS's blind but uniform sweep would have been. Preferring the
    /// candidate a plain BFS would have reached next, whenever width alone does not already decide, keeps
    /// the search from making that trade — see docs/benchmarks.md's M3-3 section for the effect measured.
    /// </para>
    /// <para>
    /// <b>Beam width, trials, cancellation.</b> At each step, every surviving state's candidates are scored
    /// and only the <see cref="EdgeOrderOptions.BeamWidth"/> best survive to the next step — <see cref="Search"/>.
    /// <see cref="Compute"/> repeats this from several start vertices and keeps the narrowest result
    /// (<see cref="EdgeOrderOptions"/>'s start-vertex selection controls how many). Cancelling
    /// <see cref="EdgeOrderOptions.CancellationToken"/> does not abort a trial outright — from the next step
    /// on, the beam collapses to its single best survivor, which finishes the remaining vertices quickly
    /// (no more branching) so a complete, valid order is always returned.
    /// </para>
    /// <para>
    /// <b>Cost.</b> One step clones three <c>O(VertexCount)</c> arrays per surviving state, so one trial is
    /// <c>O(BeamWidth × VertexCount × (VertexCount + EdgeCount))</c>; a small default beam width and a small
    /// default number of trials keep this within a few seconds at the thousands-of-edges scale M3-3 targets
    /// (see docs/benchmarks.md's M3-3 section) — widen either only for a graph small enough to afford it.
    /// </para>
    /// </remarks>
    internal static class BeamSearchPathWidth
    {
        /// <summary>The beam width <see cref="EdgeOrderOptions.BeamWidth"/> of <c>0</c> falls back to.</summary>
        internal const int DefaultBeamWidth = 8;

        /// <summary>
        /// How many start vertices are tried under the default <see cref="StartVertexSelection.MinimumDegree"/>
        /// selection. Unlike <see cref="EdgeOrderStrategy.Bfs"/> / <see cref="EdgeOrderStrategy.Dfs"/>, trying
        /// several starts is part of what this strategy does, not an opt-in (PLAN.md §8).
        /// </summary>
        internal const int DefaultStartVertexTrials = 3;

        /// <summary>Returns the permutation this strategy produces: new edge index → <paramref name="topology"/>'s edge index.</summary>
        internal static int[] Compute(EdgeTopology topology, EdgeOrderOptions options)
        {
            int edgeCount = topology.EdgeCount;
            if (edgeCount == 0)
            {
                return EdgeOrdering.Identity(edgeCount);
            }

            int beamWidth = options.BeamWidth > 0 ? options.BeamWidth : DefaultBeamWidth;
            CancellationToken cancellationToken = options.CancellationToken;

            // Node.MaxWidth/WidthSum (an incremental estimate — see Predict) is only trusted to prune
            // candidates cheaply while a trial is in progress. Once a trial has produced a complete vertex
            // order, its actual peak frontier is just as cheap to compute exactly
            // (EdgeOrdering.MaxFrontierSize is O(VertexCount + EdgeCount)), so picking the best *trial* —
            // across beam survivors and across start vertices — is done with the exact number, not the
            // estimate. That keeps any looseness in the estimate from ever picking a worse final order than
            // the search actually found.
            int[]? bestOrder = null;
            int bestWidth = int.MaxValue;

            foreach (int start in StartVertices(topology, options))
            {
                foreach (Node survivor in Search(topology, beamWidth, start, cancellationToken))
                {
                    int[] order = EdgeOrdering.EdgeOrderFromVertexOrder(topology, VertexOrder(survivor, topology.VertexCount));
                    int width = EdgeOrdering.MaxFrontierSize(topology, order);
                    if (width < bestWidth)
                    {
                        bestWidth = width;
                        bestOrder = order;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            return bestOrder!;
        }

        private static IReadOnlyList<int> StartVertices(EdgeTopology topology, EdgeOrderOptions options)
        {
            if (options.Selection == StartVertexSelection.Specified)
            {
                if ((uint)options.StartVertex >= (uint)topology.VertexCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        options.StartVertex,
                        $"The start vertex must be in 0 .. {topology.VertexCount - 1}.");
                }

                return new[] { options.StartVertex };
            }

            // topology.EdgeCount > 0 here (Compute already returned for the empty-edge case), so there is at
            // least one vertex of positive degree to sort.
            List<int> sorted = EdgeOrdering.DegreeSortedVertices(topology);
            int trials = options.Selection == StartVertexSelection.BestOfCandidates
                ? (options.MaxCandidates > 0 ? Math.Min(options.MaxCandidates, sorted.Count) : sorted.Count)
                : Math.Min(DefaultStartVertexTrials, sorted.Count);

            return sorted.GetRange(0, trials);
        }

        private static int[] VertexOrder(Node finished, int vertexCount)
        {
            var order = new int[vertexCount];
            PathLink? link = finished.Path;
            for (int i = vertexCount - 1; i >= 0; i--)
            {
                order[i] = link!.Vertex;
                link = link.Parent;
            }

            return order;
        }

        /// <summary>
        /// The vertices chosen up to some point in the search, as a linked list rather than a copy per
        /// state: reconstructing the final order costs one <c>O(VertexCount)</c> walk regardless of how
        /// many generations the search went through, and — crucially — a state does not have to keep every
        /// ancestor generation's full arrays reachable just to let the final order be recovered later.
        /// </summary>
        private sealed class PathLink
        {
            internal PathLink(PathLink? parent, int vertex)
            {
                Parent = parent;
                Vertex = vertex;
            }

            internal PathLink? Parent { get; }

            internal int Vertex { get; }
        }

        /// <summary>
        /// One partial vertex order. <see cref="Growable"/> holds, in its first <see cref="GrowableCount"/>
        /// slots, the visited vertices that still have an unvisited neighbor — the source of the next
        /// step's candidates. <see cref="Remaining"/> is, for every vertex, how many of its incident edges
        /// still have an unvisited endpoint (so a visited vertex's <see cref="Remaining"/> hitting zero is
        /// what retires it from <see cref="Growable"/> and from the frontier).
        /// </summary>
        private sealed class Node
        {
            internal PathLink? Path;
            internal bool[] Visited = Array.Empty<bool>();
            internal int[] Remaining = Array.Empty<int>();
            internal int[] Growable = Array.Empty<int>();
            internal int GrowableCount;
            internal int OpenCount;
            internal int MaxWidth;
            internal long WidthSum;

            /// <summary>
            /// How far into the trial's degree-sorted vertex list <see cref="RestartVertex"/> has already
            /// confirmed every entry is visited. Visited status never reverts, so this only ever moves
            /// forward — carried from parent to child in <see cref="Apply"/> — which is what keeps repeated
            /// restarts (a graph with many components, or many isolated vertices) from rescanning the
            /// whole list every time.
            /// </summary>
            internal int RestartCursor;
        }

        /// <summary>Runs one trial from <paramref name="start"/> and returns its final beam (up to <paramref name="beamWidth"/> complete vertex orders).</summary>
        private static List<Node> Search(EdgeTopology topology, int beamWidth, int start, CancellationToken cancellationToken)
        {
            int vertexCount = topology.VertexCount;
            var remaining = new int[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                remaining[v] = topology.Degree(v);
            }

            var root = new Node
            {
                Visited = new bool[vertexCount],
                Remaining = remaining,
                Growable = new int[vertexCount],
            };

            int[] distance = BfsDistance(topology, start);
            int[] verticesByDegree = VerticesByDegreeAscending(topology);

            var beam = new List<Node> { Apply(topology, root, start, verticesByDegree) };
            var candidateStamp = new int[vertexCount];
            int stamp = 0;

            for (int placed = 1; placed < vertexCount; placed++)
            {
                // Cancellation does not stop the search outright: from here on the beam collapses to its
                // single best survivor, which finishes the remaining vertices with no more branching.
                int width = cancellationToken.IsCancellationRequested ? 1 : beamWidth;
                beam = Advance(topology, beam, width, distance, verticesByDegree, candidateStamp, ref stamp);
            }

            return beam;
        }

        /// <summary>Every vertex (including isolated ones), lowest degree first, ties broken by index.</summary>
        /// <remarks>
        /// Computed once per trial and shared read-only across every state: it only depends on the graph,
        /// not on what has been visited. <see cref="RestartVertex"/> walks this list via each state's own
        /// <see cref="Node.RestartCursor"/> rather than rescanning <see cref="Graph.VertexCount"/> vertices
        /// from scratch every time a disconnected graph's next component needs a start.
        /// </remarks>
        private static int[] VerticesByDegreeAscending(EdgeTopology topology)
        {
            var vertices = new int[topology.VertexCount];
            for (int v = 0; v < vertices.Length; v++)
            {
                vertices[v] = v;
            }

            Array.Sort(vertices, (a, b) =>
            {
                int byDegree = topology.Degree(a).CompareTo(topology.Degree(b));
                return byDegree != 0 ? byDegree : a.CompareTo(b);
            });

            return vertices;
        }

        /// <summary>Breadth-first distance from <paramref name="start"/> (unreached vertices get <see cref="int.MaxValue"/>).</summary>
        private static int[] BfsDistance(EdgeTopology topology, int start)
        {
            var distance = new int[topology.VertexCount];
            Array.Fill(distance, int.MaxValue);
            distance[start] = 0;

            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int edgeIndex in topology.IncidentEdges(v))
                {
                    int u = topology.Other(edgeIndex, v);
                    if (distance[u] == int.MaxValue)
                    {
                        distance[u] = distance[v] + 1;
                        queue.Enqueue(u);
                    }
                }
            }

            return distance;
        }

        private static List<Node> Advance(
            EdgeTopology topology, List<Node> beam, int beamWidth, int[] distance, int[] verticesByDegree, int[] candidateStamp, ref int stamp)
        {
            // Order is the index a candidate was added at: two candidates can tie on every scored field
            // (same Vertex reached from two different Parent states with an identical Peak/distance/sum, or
            // even the same Parent — Predict is pure, so a repeated (node, vertex) pair scores identically)
            // and List<T>.Sort is not stable, so without a final tie-break the result could vary between
            // runs and .NET versions despite every other key being reproducible.
            var scored = new List<(Node Parent, int Vertex, int Peak, int NewOpenCount, int Order)>();
            foreach (Node node in beam)
            {
                stamp++;
                foreach (int candidate in Candidates(topology, node, verticesByDegree, candidateStamp, stamp))
                {
                    Predict(topology, node, candidate, out int newOpenCount, out int peak);
                    scored.Add((node, candidate, peak, newOpenCount, scored.Count));
                }
            }

            scored.Sort((a, b) =>
            {
                int aMax = Math.Max(a.Parent.MaxWidth, a.Peak);
                int bMax = Math.Max(b.Parent.MaxWidth, b.Peak);
                int byMax = aMax.CompareTo(bMax);
                if (byMax != 0)
                {
                    return byMax;
                }

                // Among candidates that leave the same peak so far, prefer the one closer (in graph
                // distance from the start) to what a plain BFS would visit next. Unrestricted, a pure
                // width-greedy tie-break tends to wander into cheap-looking peninsulas that must be paid
                // for later — see docs/benchmarks.md's M3-3 section for the effect this has in practice.
                int byDistance = distance[a.Vertex].CompareTo(distance[b.Vertex]);
                if (byDistance != 0)
                {
                    return byDistance;
                }

                long aSum = a.Parent.WidthSum + a.NewOpenCount;
                long bSum = b.Parent.WidthSum + b.NewOpenCount;
                int bySum = aSum.CompareTo(bSum);
                if (bySum != 0)
                {
                    return bySum;
                }

                int byVertex = a.Vertex.CompareTo(b.Vertex);
                return byVertex != 0 ? byVertex : a.Order.CompareTo(b.Order);
            });

            var next = new List<Node>(Math.Min(beamWidth, scored.Count));
            for (int i = 0; i < scored.Count && next.Count < beamWidth; i++)
            {
                next.Add(Apply(topology, scored[i].Parent, scored[i].Vertex, verticesByDegree));
            }

            return next;
        }

        /// <summary>
        /// Unvisited vertices worth trying next from <paramref name="node"/>: the unvisited neighbors of
        /// its growable vertices, deduplicated with a shared stamped buffer rather than a fresh set per
        /// call. Once nothing is growable, the lowest-degree unvisited vertex restarts a new region.
        /// </summary>
        private static IEnumerable<int> Candidates(EdgeTopology topology, Node node, int[] verticesByDegree, int[] candidateStamp, int mark)
        {
            if (node.GrowableCount == 0)
            {
                yield return RestartVertex(node, verticesByDegree);
                yield break;
            }

            for (int i = 0; i < node.GrowableCount; i++)
            {
                foreach (int edgeIndex in topology.IncidentEdges(node.Growable[i]))
                {
                    int u = topology.Other(edgeIndex, node.Growable[i]);
                    if (!node.Visited[u] && candidateStamp[u] != mark)
                    {
                        candidateStamp[u] = mark;
                        yield return u;
                    }
                }
            }
        }

        /// <summary>
        /// The lowest-degree unvisited vertex, found by walking <paramref name="verticesByDegree"/> forward
        /// from <paramref name="node"/>'s <see cref="Node.RestartCursor"/> — not by rescanning every vertex.
        /// Read-only: the cursor itself only advances when a state is actually materialized, in <see cref="Apply"/>.
        /// </summary>
        private static int RestartVertex(Node node, int[] verticesByDegree)
        {
            int i = node.RestartCursor;
            while (node.Visited[verticesByDegree[i]])
            {
                i++;
            }

            return verticesByDegree[i];
        }

        /// <summary>
        /// The frontier width right after <paramref name="node"/> visits <paramref name="v"/>
        /// (<paramref name="newOpenCount"/>) and the highest it would momentarily reach while doing so
        /// (<paramref name="peak"/>), without mutating <paramref name="node"/> — this runs once per
        /// candidate considered, most of which are discarded, so it must not pay for a state copy.
        /// </summary>
        private static void Predict(EdgeTopology topology, Node node, int v, out int newOpenCount, out int peak)
        {
            int opened = 0;
            int closed = 0;
            int touched = 0;

            foreach (int edgeIndex in topology.IncidentEdges(v))
            {
                int u = topology.Other(edgeIndex, v);
                if (!node.Visited[u])
                {
                    continue;
                }

                touched++;
                if (node.Remaining[u] == topology.Degree(u))
                {
                    opened++; // u's first edge to resolve is this one: it enters the frontier only now.
                }

                if (node.Remaining[u] == 1)
                {
                    closed++; // this was u's last unresolved edge: it leaves the frontier.
                }
            }

            if (touched > 0)
            {
                opened++; // v's first resolved edge is one of the ones above: v enters the frontier too.
                if (topology.Degree(v) - touched == 0)
                {
                    closed++; // every one of v's edges resolved at once: v leaves again immediately.
                }
            }

            peak = node.OpenCount + opened;
            newOpenCount = peak - closed;
        }

        /// <summary>Returns a new state with <paramref name="v"/> visited, leaving <paramref name="parent"/> untouched.</summary>
        private static Node Apply(EdgeTopology topology, Node parent, int v, int[] verticesByDegree)
        {
            var visited = (bool[])parent.Visited.Clone();
            var remaining = (int[])parent.Remaining.Clone();
            var growable = (int[])parent.Growable.Clone();
            int growableCount = parent.GrowableCount;

            visited[v] = true;

            int opened = 0;
            int closed = 0;
            int touched = 0;

            foreach (int edgeIndex in topology.IncidentEdges(v))
            {
                int u = topology.Other(edgeIndex, v);
                if (!visited[u])
                {
                    continue;
                }

                touched++;
                if (remaining[u] == topology.Degree(u))
                {
                    opened++;
                }

                remaining[u]--;
                if (remaining[u] == 0)
                {
                    closed++;
                    RemoveGrowable(growable, ref growableCount, u);
                }
            }

            remaining[v] = topology.Degree(v) - touched;
            if (touched > 0)
            {
                opened++;
                if (remaining[v] > 0)
                {
                    growable[growableCount++] = v;
                }
                else
                {
                    closed++;
                }
            }
            else if (topology.Degree(v) > 0)
            {
                growable[growableCount++] = v;
            }

            int peak = parent.OpenCount + opened;
            int openCount = peak - closed;

            // Advances past whatever prefix of verticesByDegree is now confirmed visited, so the next
            // restart on this lineage (if any) does not re-walk ground parent already covered.
            int restartCursor = parent.RestartCursor;
            while (restartCursor < verticesByDegree.Length && visited[verticesByDegree[restartCursor]])
            {
                restartCursor++;
            }

            return new Node
            {
                Path = new PathLink(parent.Path, v),
                Visited = visited,
                Remaining = remaining,
                Growable = growable,
                GrowableCount = growableCount,
                OpenCount = openCount,
                MaxWidth = Math.Max(parent.MaxWidth, peak),
                WidthSum = parent.WidthSum + openCount,
                RestartCursor = restartCursor,
            };
        }

        private static void RemoveGrowable(int[] growable, ref int count, int vertex)
        {
            for (int i = 0; i < count; i++)
            {
                if (growable[i] == vertex)
                {
                    count--;
                    growable[i] = growable[count];
                    return;
                }
            }
        }
    }
}
