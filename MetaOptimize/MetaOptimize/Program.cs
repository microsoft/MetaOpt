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
            var optimalEncoder = new OptimalEncoder(topology, demandVariables);
            var optimalEncoding = optimalEncoder.Encoding();

            // solve the problem.
            var solution1 = Zen.Maximize(optimalEncoding.MaximizationObjective, subjectTo: optimalEncoding.FeasibilityConstraints);
            var solution2 = optimalEncoding.OptimalConstraints.Solve();

            // print the solution.
            if (!solution1.IsSatisfiable() || !solution2.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (max SMT):");
            optimalEncoder.DisplaySolution(solution1);

            Console.WriteLine();

            Console.WriteLine("Optimal (KKT):");
            optimalEncoder.DisplaySolution(solution2);
        }

        /// <summary>
        /// Test out a few simple optimization examples with KKT.
        /// </summary>
        private static void RunTests()
        {
            var x = Zen.Symbolic<Real>("x");
            var y = Zen.Symbolic<Real>("y");

            var encoder = new KktOptimizationEncoder(new HashSet<Zen<Real>>() { x, y });

            // x + 2y == 10
            encoder.AddEqZeroConstraint(new Polynomial(new PolynomialTerm(1, x), new PolynomialTerm(2, y), new PolynomialTerm(-10)));

            // x >= 0, y>= 0
            encoder.AddLeqZeroConstraint(new Polynomial(new PolynomialTerm(-1, x)));
            encoder.AddLeqZeroConstraint(new Polynomial(new PolynomialTerm(-1, y)));

            // maximize y - x by minimizing x - y
            var constraints = encoder.MinimizationConstraints(new Polynomial(new PolynomialTerm(1, x), new PolynomialTerm(-1, y)));

            // solve and print.
            var solution = constraints.Solve();
            Console.WriteLine($"x={solution.Get(x)}, y={solution.Get(y)}");
        }
    }
}
