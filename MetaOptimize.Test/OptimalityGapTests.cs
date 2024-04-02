// <copyright file="OptimalityGapTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Test that the optimiality gap maximization is working.
    /// </summary>
    [TestClass]
    public class OptimalityGapTests<TVar, TSol>
    {
        /// <summary>
        /// Function to create a new solver.
        /// </summary>
        internal Func<ISolver<TVar, TSol>> CreateSolver;

        /// <summary>
        /// Test that the optimality encoder works for a topology with one edge.
        /// </summary>
        [TestMethod]
        public void TestOptimialityGap()
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

            // create the optimal encoder.
            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSol>(solver, maxNumPaths: 1);

            // create the pop encoder.
            var partition = topology.RandomPartition(2);
            var popEncoder = new PopEncoder<TVar, TSol>(solver, maxNumPaths: 1, numPartitions: 2, demandPartitions: partition);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSol>(topology, maxNumPaths: 1);

            var (optimalSolution, popSolution) = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, popEncoder);

            // Debugging information.
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(popSolution, Newtonsoft.Json.Formatting.Indented));

            Assert.IsTrue(Utils.IsApproximately(40, ((TEMaxFlowOptimizationSolution)optimalSolution).MaxObjective));
            Assert.IsTrue(Utils.IsApproximately(20, ((TEMaxFlowOptimizationSolution)popSolution).MaxObjective));
        }
    }
}