// <copyright file="OptimalEncodingTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Threading.Tasks.Dataflow;
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Test that the optimal encoding is working.
    /// </summary>
    [TestClass]
    public class OptimalEncodingTests
    {
        /// <summary>
        /// Test that the optimality encoder works for a topology with one edge.
        /// </summary>
        [TestMethod]
        public void TestOptimalityGapSimple()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddEdge("a", "b", capacity: 10);

            var solver = new SolverZen();
            var optimalEncoder = new OptimalEncoder<Zen<Real>, ZenSolution>(solver, topology, k: 1);
            var encoding = optimalEncoder.Encoding();
            var solverSolution = optimalEncoder.Solver.Maximize(encoding.MaximizationObjective);
            var optimizationSolution = optimalEncoder.GetSolution(solverSolution);

            // Debugging information.
            /* foreach (var c in solver.ConstraintExprs)
            {
                Console.WriteLine(c);
            }

            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolution, Newtonsoft.Json.Formatting.Indented)); */

            Assert.AreEqual(10, optimizationSolution.TotalDemandMet);
            Assert.AreEqual(10, optimizationSolution.Demands[("a", "b")]);
            Assert.AreEqual(10, optimizationSolution.Flows[("a", "b")]);
            Assert.AreEqual(0, optimizationSolution.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolution.Flows[("b", "a")]);

            // Test Gurobi now
            var solverG = new SolverGuroubi();
            var optimalEncoderG = new OptimalEncoder<GRBVar, GRBModel>(solverG, topology, k: 1);
            var encodingG = optimalEncoderG.Encoding();
            var solverSolutionG = optimalEncoderG.Solver.Maximize(encodingG.MaximizationObjective);
            var optimizationSolutionG = optimalEncoderG.GetSolution(solverSolutionG);

            Assert.AreEqual(10, optimizationSolutionG.TotalDemandMet);
            Assert.IsTrue(10 <= optimizationSolutionG.Demands[("a", "b")]);
            Assert.AreEqual(10, optimizationSolutionG.Flows[("a", "b")]);
            Assert.IsTrue(0 <= optimizationSolutionG.Demands[("b", "a")]);
            Assert.AreEqual(0, optimizationSolutionG.Flows[("b", "a")]);
        }
        /// <summary>
        /// Test that the optimal encoder works with a diamond topology.
        /// </summary>
        [TestMethod]
        public void TestOptimalityGapDiamond()
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

            var solver = new SolverZen();
            var optimalEncoder = new OptimalEncoder<Zen<Real>, ZenSolution>(solver, topology, k: 1);
            var encoding = optimalEncoder.Encoding();
            var solverSolution = optimalEncoder.Solver.Maximize(encoding.MaximizationObjective);
            var optimizationSolution = optimalEncoder.GetSolution(solverSolution);

            // Debugging information.
            /* foreach (var c in solver.ConstraintExprs)
            {
                Console.WriteLine(c);
            }

            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolution, Newtonsoft.Json.Formatting.Indented)); */

            Assert.AreEqual(40, optimizationSolution.TotalDemandMet);
            Assert.IsTrue(10 <= optimizationSolution.Demands[("a", "b")]);
            Assert.AreEqual(10, optimizationSolution.Flows[("a", "b")]);
            Assert.IsTrue(10 <= optimizationSolution.Demands[("a", "c")]);
            Assert.AreEqual(10, optimizationSolution.Flows[("a", "c")]);
            Assert.IsTrue(10 <= optimizationSolution.Demands[("b", "d")]);
            Assert.AreEqual(10, optimizationSolution.Flows[("c", "d")]);
            Assert.AreEqual(0, optimizationSolution.Flows[("a", "d")]);

            var solverG = new SolverGuroubi();
            var optimalEncoderG = new OptimalEncoder<GRBVar, GRBModel>(solverG, topology, k: 1);
            var encodingG = optimalEncoderG.Encoding();
            var solverSolutionG = optimalEncoderG.Solver.Maximize(encodingG.MaximizationObjective);
            var optimizationSolutionG = optimalEncoderG.GetSolution(solverSolutionG);

            Assert.AreEqual(40, optimizationSolutionG.TotalDemandMet);
            Assert.IsTrue(10 <= optimizationSolutionG.Demands[("a", "b")]);
            Assert.AreEqual(10, optimizationSolutionG.Flows[("a", "b")]);
            Assert.IsTrue(10 <= optimizationSolutionG.Demands[("a", "c")]);
            Assert.AreEqual(10, optimizationSolutionG.Flows[("a", "c")]);
            Assert.IsTrue(10 <= optimizationSolutionG.Demands[("b", "d")]);
            Assert.AreEqual(10, optimizationSolutionG.Flows[("c", "d")]);
            Assert.AreEqual(0, optimizationSolutionG.Flows[("a", "d")]);

            solverG.Delete();
        }
    }
}