using System;
using System.Diagnostics;
using System.IO;
using ZDD.Net.Core;
using ZDD.Net.Frontier;
using ZDD.Net.Io;

namespace ZDD.Net.Benchmarks
{
    /// <summary>
    /// Times <see cref="ZddBinaryFormat.Write"/> / <see cref="ZddBinaryFormat.Read"/> against the build
    /// time of the same family, and records file size per node. <c>dotnet run -c Release -- serialize</c>
    /// runs this; docs/benchmarks.md's M5-1 section is its output (issue #53).
    /// </summary>
    internal static class SerializationReport
    {
        public static void Run()
        {
            Console.WriteLine(
                $"{"Case",-40} {"Build",10} {"Write",10} {"Read",10} {"FileSize",12} {"Bytes/Node",10} {"Nodes",10}");

            foreach ((string name, Func<ZddManager, BuildOptions?, Zdd> build, int variableCount) in Cases.All)
            {
                Measure(name, variableCount, build);
            }
        }

        private static void Measure(string name, int variableCount, Func<ZddManager, BuildOptions?, Zdd> build)
        {
            using ZddManager manager = new ZddManager(variableCount);

            Stopwatch buildWatch = Stopwatch.StartNew();
            Zdd result = build(manager, null);
            buildWatch.Stop();

            long nodeCount = manager.NodeCount;

            using MemoryStream stream = new MemoryStream();
            Stopwatch writeWatch = Stopwatch.StartNew();
            ZddBinaryFormat.Write(result, stream);
            writeWatch.Stop();

            long fileSize = stream.Length;
            stream.Position = 0;

            Stopwatch readWatch = Stopwatch.StartNew();
            Zdd restored = ZddBinaryFormat.Read(stream);
            readWatch.Stop();

            using ZddManager restoredManager = restored.Manager;
            if (restored.Id != result.Id || restoredManager.NodeCount != nodeCount)
            {
                throw new InvalidOperationException($"{name}: round trip did not reproduce the same node IDs.");
            }

            double bytesPerNode = nodeCount == 0 ? 0 : (double)fileSize / nodeCount;

            Console.WriteLine(
                $"{name,-40} {buildWatch.Elapsed.TotalMilliseconds,7:F2}ms {writeWatch.Elapsed.TotalMilliseconds,7:F2}ms " +
                $"{readWatch.Elapsed.TotalMilliseconds,7:F2}ms {fileSize,10:N0}B {bytesPerNode,10:F2} {nodeCount,10:N0}");
        }
    }
}
