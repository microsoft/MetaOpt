// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Linq;

    /// <summary>
    /// Main entry point for the program.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point for the program.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public static void Main(string[] args)
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddNode("e");
            topology.AddNode("f");
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "c", capacity: 10);
            topology.AddEdge("d", "e", capacity: 10);
            topology.AddEdge("d", "f", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            for (int i = 0; i < 10; i++)
            {
                var optimalEncoder = new OptimalEncoder(topology, k: 2);
                var heuristicEncoder = new PopEncoder(topology, k: 2, numPartitions: 2, demandPartitions: topology.RandomPartition(2));

                var timer = System.Diagnostics.Stopwatch.StartNew();
                var result = AdversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, (e1, e2) =>
                {
                    var demandConstraints = topology.GetNodePairs().Select(p => e1.DemandExpressions[p] == e2.DemandExpressions[p]);
                    return demandConstraints.Aggregate(Zen.And);
                });

                Console.WriteLine($"optimal={result.Item1.TotalDemandMet}, heuristic={result.Item2.TotalDemandMet}, time={timer.ElapsedMilliseconds}ms");
            }
        }
    }
}
