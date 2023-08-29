// <copyright file="PopEncodingTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Collections.Generic;
    using MetaOptimize;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Test that the pop encoding is working.
    /// </summary>
    [TestClass]
    public class PopEncodingTests<TVar, TSol>
    {
        /// <summary>
        /// Function to create a new solver.
        /// </summary>
        internal Func<ISolver<TVar, TSol>> CreateSolver;

        /// <summary>
        /// Test that the optimality encoder works for a topology with one edge.
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
            var popEncoder = new PopEncoder<TVar, TSol>(CreateSolver(), maxNumPaths: 1, numPartitions: 2, demandPartitions: partition);
            var encoding = popEncoder.Encoding(topology);
            var solverSolution = popEncoder.Solver.Maximize(encoding.GlobalObjective);
            var optimizationSolution = (TEMaxFlowOptimizationSolution)popEncoder.GetSolution(solverSolution);

            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolution, Newtonsoft.Json.Formatting.Indented));

            // sk todo: AreEqual will fail due to doubles not matching; edit as below
            Assert.IsTrue(Utils.IsApproximately(5, optimizationSolution.MaxObjective));
            Assert.IsTrue(5 <= optimizationSolution.Demands[("a", "b")]);
            Assert.IsTrue(Utils.IsApproximately(5, optimizationSolution.Flows[("a", "b")]));
            Assert.IsTrue(0 <= optimizationSolution.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolution.Flows[("b", "a")]);
        }

        /// <summary>
        /// Test the POP encoder on a more complex example.
        /// </summary>
        /// TODO: in the documentation make sure you state that for the adversarial input generator to work
        /// the heuristic and the optimum encoders should use the same solver instance.
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
            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSol>(solver, maxNumPaths: 1);

            var popEncoderG = new PopEncoder<TVar, TSol>(solver, maxNumPaths: 1, numPartitions: 2, demandPartitions: partition);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSol>(topology, maxNumPaths: 1);

            var (optimalSolutionG, popSolutionG) = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, popEncoderG);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(popSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = ((TEMaxFlowOptimizationSolution)optimalSolutionG).MaxObjective;
            var heuristic = ((TEMaxFlowOptimizationSolution)popSolutionG).MaxObjective;
            Assert.IsTrue(Math.Abs(optimal - 40.0) < 0.01, $"Optimal is {optimal} != 40");
            Assert.IsTrue(Math.Abs(heuristic - 20.0) < 0.01, $"Heuristic is {heuristic} != 20");

            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristic}");
        }
    }
}