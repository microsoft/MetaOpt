// <copyright file="PopEncodingTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Collections.Generic;
    using Gurobi;
    using MetaOptimize;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Test that the pop encoding is working.
    /// </summary>
    [TestClass]
    public class PopEncodingTests
    {
        /// <summary>
        /// Test that the optimality encoder works for a topology with one edge.
        /// Solver Zen.
        /// </summary>
        [TestMethod]
        public void TestPopGapSimple()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddEdge("a", "b", capacity: 10);

            var partition = new Dictionary<(string, string), int>();
            partition.Add(("a", "b"), 0);
            partition.Add(("b", "a"), 1);
            var popEncoder = new PopEncoder<Zen<Real>, ZenSolution>(new SolverZen(), topology, k: 1, numPartitions: 2, demandPartitions: partition);
            var encoding = popEncoder.Encoding();
            var solverSolution = popEncoder.Solver.Maximize(encoding.MaximizationObjective);
            var optimizationSolution = popEncoder.GetSolution(solverSolution);

            var solver = (SolverZen)popEncoder.Solver;

            // Debugging information.
            foreach (var c in solver.ConstraintExprs)
            {
                Console.WriteLine(c);
            }

            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolution, Newtonsoft.Json.Formatting.Indented));

            Assert.AreEqual(5, optimizationSolution.TotalDemandMet);
            Assert.AreEqual(5, optimizationSolution.Demands[("a", "b")]);
            Assert.AreEqual(5, optimizationSolution.Flows[("a", "b")]);
            Assert.AreEqual(0, optimizationSolution.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolution.Flows[("b", "a")]);
        }
    }
}