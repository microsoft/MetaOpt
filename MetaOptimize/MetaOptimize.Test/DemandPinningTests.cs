namespace MetaOptimize.Test
{
    using System;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// tests demand pinning.
    /// </summary>
    public abstract class DemandPinningTests<TVar, TSol>
    {
        /// <summary>
        /// Function to create a new solver.
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
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSol>(solver, k: k);
            var heuristicEncoder = new DemandPinningEncoder<TVar, TSol>(solver, k: k, threshold: threshold);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSol>(topology, k: k);
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

        /// <summary>
        /// Paper example.
        /// </summary>
        [TestMethod]
        public void TestPaperExampleTopo()
        {
            var topology = new Topology();
            topology.AddNode("a");
            topology.AddNode("b");
            topology.AddNode("c");
            topology.AddNode("d");
            topology.AddNode("e");
            topology.AddEdge("a", "b", capacity: 100);
            topology.AddEdge("b", "c", capacity: 100);
            topology.AddEdge("a", "d", capacity: 50);
            topology.AddEdge("d", "e", capacity: 50);
            topology.AddEdge("e", "c", capacity: 50);

            double threshold = 50;
            int k = 2;

            // create the optimal encoder.
            var solver = CreateSolver();
            var optimalEncoder = new TEOptimalEncoder<TVar, TSol>(solver, k: k);
            var heuristicEncoder = new DemandPinningEncoder<TVar, TSol>(solver, k: k, threshold: threshold);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSol>(topology, k: k);
            var (optimalSolution, demandPinningSolution) = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder);

            var optimal = optimalSolution.TotalDemandMet;
            var heuristic = demandPinningSolution.TotalDemandMet;
            Assert.IsTrue(TestHelper.IsApproximately(250, optimal));
            Assert.IsTrue(TestHelper.IsApproximately(150, heuristic));
            Assert.IsTrue(TestHelper.IsApproximately(100, optimal - heuristic));
        }
    }
}
