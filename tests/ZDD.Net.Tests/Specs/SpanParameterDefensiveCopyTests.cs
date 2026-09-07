using System;
using Xunit;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Graphs;
using ZDD.Net.Specs;

namespace ZDD.Net.Tests.Specs
{
    /// <summary>Verifies that specs own copies of caller-provided span data.</summary>
    public class SpanParameterDefensiveCopyTests
    {
        [Fact]
        public void LinearConstraintSpecCopiesCoefficients()
        {
            int[] coefficients = { 1, 2, 3 };
            var spec = new LinearConstraintSpec(coefficients, LinearConstraintOperator.Equal, 3);
            coefficients.AsSpan().Fill(100);

            using var manager = new ZddManager(3);
            Zdd actual = FrontierBuilder.Build<LinearConstraintSpec, long>(manager, spec);
            Zdd expected = FrontierBuilder.Build<LinearConstraintSpec, long>(
                manager, new LinearConstraintSpec(new[] { 1, 2, 3 }, LinearConstraintOperator.Equal, 3));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void KnapsackSpecCopiesWeights()
        {
            int[] weights = { 1, 2, 3 };
            var spec = new KnapsackSpec(weights, capacity: 3);
            weights.AsSpan().Fill(100);

            using var manager = new ZddManager(3);
            Zdd actual = FrontierBuilder.Build<KnapsackSpec, long>(manager, spec);
            Zdd expected = FrontierBuilder.Build<KnapsackSpec, long>(
                manager, new KnapsackSpec(new[] { 1, 2, 3 }, capacity: 3));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DegreeConstraintSpecCopiesBounds()
        {
            Graph graph = Graph.Path(3);
            int[] lo = { 1, 1, 0 };
            int[] hi = { 1, 2, 1 };
            var spec = new DegreeConstraintSpec(graph, lo, hi);
            lo.AsSpan().Fill(100);
            hi.AsSpan().Fill(100);

            using var manager = new ZddManager(graph.EdgeCount);
            Zdd actual = FrontierBuilder.Build<DegreeConstraintSpec>(manager, spec);
            Zdd expected = FrontierBuilder.Build<DegreeConstraintSpec>(
                manager, new DegreeConstraintSpec(graph, new[] { 1, 1, 0 }, new[] { 1, 2, 1 }));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DegreeDistributionSpecCopiesCounts()
        {
            Graph graph = Graph.Path(3);
            int[] counts = { 0, 2, 1 };
            var spec = new DegreeDistributionSpec(graph, counts);
            counts.AsSpan().Fill(100);

            using var manager = new ZddManager(graph.EdgeCount);
            Zdd actual = FrontierBuilder.Build<DegreeDistributionSpec>(manager, spec);
            Zdd expected = FrontierBuilder.Build<DegreeDistributionSpec>(
                manager, new DegreeDistributionSpec(graph, new[] { 0, 2, 1 }));

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DirectedDegreeConstraintSpecCopiesBounds()
        {
            DirectedGraph graph = DirectedGraph.Path(3);
            int[] inLo = { 0, 1, 0 };
            int[] inHi = { 0, 1, 1 };
            int[] outLo = { 1, 0, 0 };
            int[] outHi = { 1, 1, 0 };
            var spec = new DirectedDegreeConstraintSpec(graph, inLo, inHi, outLo, outHi);
            inLo.AsSpan().Fill(100);
            inHi.AsSpan().Fill(100);
            outLo.AsSpan().Fill(100);
            outHi.AsSpan().Fill(100);

            using var manager = new ZddManager(graph.EdgeCount);
            Zdd actual = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(manager, spec);
            Zdd expected = FrontierBuilder.Build<DirectedDegreeConstraintSpec>(
                manager,
                new DirectedDegreeConstraintSpec(
                    graph,
                    new[] { 0, 1, 0 },
                    new[] { 0, 1, 1 },
                    new[] { 1, 0, 0 },
                    new[] { 1, 1, 0 }));

            Assert.Equal(expected, actual);
        }
    }
}
