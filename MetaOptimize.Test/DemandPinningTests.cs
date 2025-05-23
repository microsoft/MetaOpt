﻿namespace MetaOptimize.Test
{
    using System;
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// tests demand pinning.
    /// </summary>
    [TestClass]
    public class DemandPinningTests<TVar, TSol>
    {
        /// <summary>
        /// Function to create a new solver.
        /// This uses a delegate method so that we can plug and play different solvers.
        /// </summary>
        internal Func<ISolver<TVar, TSol>> CreateSolver;

        /// <summary>
        /// Using a threshold of 5, tests the demandpinning solution
        /// on diamond topo.
        /// </summary>
        [TestMethod]
        public void TestDiamondTopo()
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

            double threshold = 5;
            int k = 2;

            // create the optimal encoder.
            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSol>(solver, maxNumPaths: k);
            var heuristicEncoder = new DemandPinningEncoder<TVar, TSol>(solver, maxNumPaths: k, threshold: threshold);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSol>(topology, maxNumPaths: k);
            var (optimalSolution, demandPinningSolution) = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(demandPinningSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = ((TEMaxFlowOptimizationSolution)optimalSolution).MaxObjective;
            var heuristic = ((TEMaxFlowOptimizationSolution)demandPinningSolution).MaxObjective;
            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristic}");
            // Assert.IsTrue(TestHelper.IsApproximately(40, optimal));
            // Assert.IsTrue(TestHelper.IsApproximately(35, heuristic));
            Assert.IsTrue(Utils.IsApproximately(10, optimal - heuristic));
        }
    }
}
