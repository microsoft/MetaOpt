// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
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
        /// <returns></returns>
        public static void Main(string[] args)
        {
            // create a topology
            //   b
            //  / \
            // a   d
            //  \ /
            //   c
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            // create the demand variables shared between encodings.
            var demandVariables = new Dictionary<(string, string), Zen<Real>>();
            foreach (var pair in topology.GetNodePairs())
            {
                demandVariables[pair] = Zen.Symbolic<Real>("demand" + pair.Item1 + "_" + pair.Item2);
            }

            // create the optimal encoding.
            var optimalEncoding = new OptimalEncoding2(topology, demandVariables);
            var optimalConstraints = optimalEncoding.Constraints();
            // var optimalObjective = optimalEncoding.MaximizationObjective();

            // create a heuristic encoding.
            // var heuristicEncoding = new ThresholdHeuristicEncoding(topology, demandVariables, threshold: 5, ensureLocalOptimum: true);
            // var heuristicConstraints = heuristicEncoding.Constraints();
            // var heuristicObjective = heuristicEncoding.MaximizationObjective();

            // collect all the constraints
            var constraints = optimalConstraints;

            // solve the problem
            // var solution = Zen.Maximize(optimalObjective, subjectTo: constraints);
            var solution = constraints.Solve();

            if (!solution.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal:");
            optimalEncoding.DisplaySolution(solution);

            // Console.WriteLine();
            // Console.WriteLine("Heuristic:");
            // heuristicEncoding.DisplaySolution(solution);
        }
    }
}
