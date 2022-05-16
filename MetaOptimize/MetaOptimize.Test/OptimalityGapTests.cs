// <copyright file="OptimalityGapTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Collections.Generic;
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Test that the optimiality gap maximization is working.
    /// </summary>
    [TestClass]
    public class OptimalityGapTests
    {
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
            var solver = new SolverZen();
            var optimalEncoder = new OptimalEncoder<Zen<Real>, ZenSolution>(solver, topology, k: 1);

            // create the pop encoder.
            var partition = topology.RandomPartition(2);
            var popEncoder = new PopEncoder<Zen<Real>, ZenSolution>(solver, topology, k: 1, numPartitions: 2, demandPartitions: partition);

            var (optimalSolution, popSolution) = AdversarialInputGenerator<Zen<Real>, ZenSolution>.MaximizeOptimalityGap(optimalEncoder, popEncoder);

            // Debugging information.
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(popSolution, Newtonsoft.Json.Formatting.Indented));

            Assert.AreEqual(40, optimalSolution.TotalDemandMet);
            Assert.AreEqual(20, popSolution.TotalDemandMet);

            // create the optimal encoder.
            var solverG = new SolverGuroubi();
            var optimalEncoderG = new OptimalEncoder<GRBVar, GRBModel>(solverG, topology, k: 1);

            var popEncoderG = new PopEncoder<GRBVar, GRBModel>(solverG, topology, k: 1, numPartitions: 2, demandPartitions: partition);

            var (optimalSolutionG, popSolutionG) = AdversarialInputGenerator<GRBVar, GRBModel>.MaximizeOptimalityGap(optimalEncoderG, popEncoderG);
            Assert.AreEqual(40, optimalSolutionG.TotalDemandMet);
            Assert.AreEqual(20, popSolutionG.TotalDemandMet);

            solverG.Delete();
        }
    }
}