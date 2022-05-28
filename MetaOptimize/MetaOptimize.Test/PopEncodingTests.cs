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

            // sk todo: AreEqual will fail due to doubles not matching; edit as below
            Assert.IsTrue(Math.Abs(5 - optimizationSolution.TotalDemandMet) <= 0.01);
            Assert.IsTrue(5 <= optimizationSolution.Demands[("a", "b")]);
            Assert.AreEqual(5, optimizationSolution.Flows[("a", "b")]);
            Assert.IsTrue(0 <= optimizationSolution.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolution.Flows[("b", "a")]);

            var popEncoderG = new PopEncoder<GRBVar, GRBModel>(new SolverGurobiNoParams(), topology, k: 1, numPartitions: 2, demandPartitions: partition);
            var encodingG = popEncoderG.Encoding();
            var solverSolutionG = popEncoderG.Solver.Maximize(encodingG.MaximizationObjective);
            var optimizationSolutionG = popEncoderG.GetSolution(solverSolutionG);

            var solverG = (SolverGurobiNoParams)popEncoderG.Solver;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolutionG, Newtonsoft.Json.Formatting.Indented));

            Assert.AreEqual(5, optimizationSolutionG.TotalDemandMet);
            Assert.IsTrue(5 <= optimizationSolutionG.Demands[("a", "b")]);
            Assert.AreEqual(5, optimizationSolutionG.Flows[("a", "b")]);
            Assert.IsTrue(0 <= optimizationSolutionG.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolutionG.Flows[("b", "a")]);
        }

        /// <summary>
        /// Test the POP encoder on a more complex example.
        /// </summary>
        [TestMethod]
        public void TestPopGapSK()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddEdge("a", "b", capacity: 10);
            topology.AddEdge("a", "c", capacity: 10);
            topology.AddEdge("b", "d", capacity: 10);
            topology.AddEdge("c", "d", capacity: 10);

            var partition = topology.RandomPartition(2);
            // create the optimal encoder.
            var solverG = new SolverGurobiNoParams();
            var optimalEncoderG = new OptimalEncoder<GRBVar, GRBModel>(solverG, topology, k: 1);

            var popEncoderG = new PopEncoder<GRBVar, GRBModel>(solverG, topology, k: 1, numPartitions: 2, demandPartitions: partition);

            var (optimalSolutionG, popSolutionG) = AdversarialInputGenerator<GRBVar, GRBModel>.MaximizeOptimalityGap(optimalEncoderG, popEncoderG);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(popSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = optimalSolutionG.TotalDemandMet;
            var heuristic = popSolutionG.TotalDemandMet;
            Assert.IsTrue(Math.Abs(optimal - 40.0) < 0.01, $"Optimal is {optimal} != 40");
            Assert.IsTrue(Math.Abs(heuristic - 20.0) < 0.01, $"Heuristic is {heuristic} != 20");

            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristic}");
        }
    }
}