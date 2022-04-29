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
            // RunTests();
            // RunOptimal();
            // RunPop();
            RunOptimalPopDiff();
        }

        /// <summary>
        /// Run the optimal encoding.
        /// </summary>
        private static void RunOptimal()
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

            // add some demand constraints.
            var demandConstraints = new Dictionary<(string, string), Real>()
            {
                { ("a", "b"), 1 },
                { ("a", "c"), 1 },
            };

            // create the optimal encoding.
            var encoder = new OptimalEncoder(topology, demandConstraints);
            var encoding = encoder.Encoding();

            // solve the problem.
            var solution1 = Zen.Maximize(encoding.MaximizationObjective, subjectTo: encoding.FeasibilityConstraints);
            var solution2 = encoding.OptimalConstraints.Solve();

            // print the solution.
            if (!solution1.IsSatisfiable() || !solution2.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (max SMT):");
            encoder.DisplaySolution(solution1);

            Console.WriteLine();

            Console.WriteLine("Optimal (KKT):");
            encoder.DisplaySolution(solution2);
        }

        /// <summary>
        /// Run the pop encoding.
        /// </summary>
        private static void RunPop()
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

            // create the demand partitioning.
            var demandPartitions = new Dictionary<(string, string), int>()
            {
                { ("a", "b"), 0 },
                { ("a", "d"), 0 },
                { ("a", "c"), 1 },
                { ("b", "d"), 1 },
                { ("c", "d"), 1 },
            };

            // create the optimal encoding.
            var encoder = new PopEncoder(topology, 2, demandPartitions);
            var encoding = encoder.Encoding();

            // solve the problem.
            var solution1 = Zen.Maximize(encoding.MaximizationObjective, subjectTo: encoding.FeasibilityConstraints);
            var solution2 = encoding.OptimalConstraints.Solve();

            // print the solution.
            if (!solution1.IsSatisfiable() || !solution2.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (max SMT):");
            encoder.DisplaySolution(solution1);

            Console.WriteLine();

            Console.WriteLine("Optimal (KKT):");
            encoder.DisplaySolution(solution2);
        }

        /// <summary>
        /// Run the optimal - pop encoding.
        /// </summary>
        private static void RunOptimalPopDiff()
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

            // create the demand partitioning.
            var demandPartitions = new Dictionary<(string, string), int>()
            {
                { ("a", "b"), 0 },
                { ("a", "d"), 0 },
                { ("a", "c"), 1 },
                { ("b", "d"), 1 },
                { ("c", "d"), 1 },
            };

            // create the optimal encoding.
            var optEncoder = new OptimalEncoder(topology);
            var optEncoding = optEncoder.Encoding();

            // create the pop encoding.
            var popEncoder = new PopEncoder(topology, 2, demandPartitions);
            var popEncoding = popEncoder.Encoding();

            // solve the problem.
            var constraints = Zen.And(optEncoding.OptimalConstraints, popEncoding.OptimalConstraints);

            var objective = optEncoder.TotalDemandMetVariable - popEncoder.PartitionEncoders.Select(x => x.TotalDemandMetVariable).Aggregate(Zen.Plus);
            var solution = Zen.Maximize(objective, constraints);

            // print the solution.
            if (!solution.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (Opt):");
            optEncoder.DisplaySolution(solution);

            Console.WriteLine();

            Console.WriteLine("Optimal (Pop):");
            popEncoder.DisplaySolution(solution);
        }

        /// <summary>
        /// Test out a few simple optimization examples with KKT.
        /// </summary>
        private static void RunTests()
        {
            var x = Zen.Symbolic<Real>("x");
            var y = Zen.Symbolic<Real>("y");

            var encoder = new KktOptimizationGenerator(new HashSet<Zen<Real>>() { x, y });

            // x + 2y == 10
            encoder.AddEqZeroConstraint(new Polynomial(new Term(1, x), new Term(2, y), new Term(-10)));

            // x >= 0, y>= 0
            encoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, x)));
            encoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, y)));

            // maximize y - x by minimizing x - y
            var constraints = encoder.MinimizationConstraints(new Polynomial(new Term(1, x), new Term(-1, y)));

            // solve and print.
            var solution = constraints.Solve();
            Console.WriteLine($"x={solution.Get(x)}, y={solution.Get(y)}");
        }
    }
}
