namespace MetaOptimize.Test
{
    using System;
    using System.Collections.Generic;
    using System.Configuration.Assemblies;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Runtime.InteropServices;
    using MetaOptimize;
    using MetaOptimize.FailureAnalysis;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Z3;
    /// <summary>
    /// The tests for the basic failure analysis class.
    /// </summary>
    [TestClass]
    public class FailureAnalysisBasicTests<TVar, TSolution>
    {
        /// <summary>
        /// Function to create a new solver.
        /// This uses a delegate method to plug and play different solvers.
        /// </summary>
        internal Func<ISolver<TVar, TSolution>> CreateSolver = null;
        /// <summary>
        /// This method checks the functionality of the extended capacity is implemented correctly
        /// inside the encoder. It does not yet check if we select the right backup path (that requires the adversarial generator).
        /// </summary>
        [TestMethod]
        public void checkBackupPathInternal()
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
            topology.AddEdge("a", "d", capacity: 5);
            Dictionary<(string, string), string[][]> primaryPaths = new Dictionary<(string, string), string[][]>();
            Dictionary<(string, string), string[][]> backupPaths = new Dictionary<(string, string), string[][]>();
            primaryPaths.Add(("a", "d"), new string[][] { new string[] { "a", "b", "d" } });
            primaryPaths.Add(("b", "d"), new string[][] { new string[] { "b", "d" } });
            primaryPaths.Add(("a", "c"), new string[][] { new string[] { "a", "c" } });
            backupPaths.Add(("a", "d"), new string[][] { new string[] { "a", "c", "d" }, new string[] { "a", "d" } });
            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            var capacities = new Dictionary<(string, string), double>();
            capacities.Add(("a", "b"), 10);
            capacities.Add(("a", "c"), 10);
            capacities.Add(("b", "d"), 10);
            capacities.Add(("c", "d"), 0);
            capacities.Add(("a", "d"), 5);

            var extensionCapacities = new Dictionary<string[], double>(new PathComparer());
            extensionCapacities.Add(new string[] { "a", "b", "d" }, 40);
            extensionCapacities.Add(new string[] { "a", "c", "d" }, 0);
            extensionCapacities.Add(new string[] { "b", "d" }, 40);
            extensionCapacities.Add(new string[] { "a", "c" }, 40);
            extensionCapacities.Add(new string[] { "a", "d" }, 0);

            var solver = CreateSolver();
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 3);
            var optimalEncoding = optimalCutEncoder.Encoding(topology, false, demandEqualityConstraints: demands, capacityEqualityConstraints: capacities, pathExtensionCapacityConstraints: extensionCapacities);
            var optimalSolved = optimalCutEncoder.Solver.Maximize(optimalEncoding.GlobalObjective);
            var optimalSolution = (FailureAnalysisOptimizationSolution)optimalCutEncoder.GetSolution(optimalSolved);
            Assert.AreEqual(15, optimalSolution.MaxObjective);

            // Now that we have the MetaNode version and the one where we separate primary and backup paths. I think we should check those work as expected as well.
            solver.CleanAll();
            var optimalCutEncoder2 = new FailureAnalysisEncoderWithUnequalPaths<TVar, TSolution>(solver);
            var encoding = optimalCutEncoder2.Encoding(topology, false, primaryPaths: primaryPaths, backupPaths: backupPaths, demandEqualityConstraints: demands, capacityEqualityConstraints: capacities, pathExtensionCapacityConstraints: extensionCapacities);
            optimalSolved = optimalCutEncoder2.Solver.Maximize(encoding.GlobalObjective);
            optimalSolution = (FailureAnalysisOptimizationSolution)optimalCutEncoder2.GetSolution(optimalSolved);
            Assert.AreEqual(15, optimalSolution.MaxObjective);
        }
        /// <summary>
        /// This method checks the functionality of the extended capacity is implemented correctly.
        /// It does not yet check if we select the right backup path (that requires the adversarial generator).
        /// </summary>
        [TestMethod]
        public void checkBackupPathInternalV2()
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
            topology.AddEdge("a", "d", capacity: 5);

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            var capacities = new Dictionary<(string, string), double>();
            capacities.Add(("a", "b"), 10);
            capacities.Add(("a", "c"), 10);
            capacities.Add(("b", "d"), 10);
            capacities.Add(("c", "d"), 0);
            capacities.Add(("a", "d"), 5);

            var extensionCapacities = new Dictionary<string[], double>(new PathComparer());
            extensionCapacities.Add(new string[] { "a", "b", "d" }, 40);
            extensionCapacities.Add(new string[] { "a", "c", "d" }, 0);
            extensionCapacities.Add(new string[] { "b", "d" }, 40);
            extensionCapacities.Add(new string[] { "a", "c" }, 40);
            extensionCapacities.Add(new string[] { "a", "d" }, 40);

            Dictionary<(string, string), string[][]> primaryPaths = new Dictionary<(string, string), string[][]>();
            Dictionary<(string, string), string[][]> backupPaths = new Dictionary<(string, string), string[][]>();
            primaryPaths.Add(("a", "d"), new string[][] { new string[] { "a", "b", "d" } });
            primaryPaths.Add(("b", "d"), new string[][] { new string[] { "b", "d" } });
            primaryPaths.Add(("a", "c"), new string[][] { new string[] { "a", "c" } });
            backupPaths.Add(("a", "d"), new string[][] { new string[] { "a", "c", "d" }, new string[] { "a", "d" } });

            var solver = CreateSolver();
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var optimalEncoding = optimalCutEncoder.Encoding(topology, false, demandEqualityConstraints: demands, capacityEqualityConstraints: capacities, pathExtensionCapacityConstraints: extensionCapacities);
            var optimalSolved = optimalCutEncoder.Solver.Maximize(optimalEncoding.GlobalObjective);
            var optimalSolution = (FailureAnalysisOptimizationSolution)optimalCutEncoder.GetSolution(optimalSolved);
            Assert.AreEqual(20, optimalSolution.MaxObjective);
            // Now that we have the MetaNode version and the one where we separate primary and backup paths. I think we should check those work as expected as well.
            solver.CleanAll();
            var optimalCutEncoder2 = new FailureAnalysisEncoderWithUnequalPaths<TVar, TSolution>(solver);
            var encoding = optimalCutEncoder2.Encoding(topology, false, primaryPaths: primaryPaths, backupPaths: backupPaths, demandEqualityConstraints: demands, capacityEqualityConstraints: capacities, pathExtensionCapacityConstraints: extensionCapacities);
            optimalSolved = optimalCutEncoder2.Solver.Maximize(encoding.GlobalObjective);
            optimalSolution = (FailureAnalysisOptimizationSolution)optimalCutEncoder2.GetSolution(optimalSolved);
            Assert.IsTrue(Utils.IsApproximately(20, optimalSolution.MaxObjective));
        }
        /// <summary>
        /// This method checks the implementation of the failure analysis encoder.
        /// It does not yet check if we select the right backup path.
        /// </summary>]
        [TestMethod]
        public void adversarialGenWithPrimalDualAndConstrainedDemands()
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

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            demandSet.Add(5);
            demandSet.Add(10);
            var demandList = new GenericList(demandSet);

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var failureAnalysisEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, failureAnalysisEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList);

            // Check that only one link has failed.
            var numLinkDown = adversarialGenerator.GetLinkDownEvents();
            var linkDowns = numLinkDown.Select(x => x ? 1 : 0).Sum();
            Assert.AreEqual(linkDowns, 1);

            Assert.IsTrue(Utils.IsApproximately(10, optimalSol.MaxObjective - failureSol.MaxObjective));

            topology.AddNode("Meta_a");
            topology.AddNode("Meta_b");
            topology.AddNode("Meta_c");
            topology.AddNode("Meta_d");
            topology.AddEdge("Meta_a", "a", capacity: 40);
            topology.AddEdge("a", "Meta_a", capacity: 40);
            topology.AddEdge("Meta_b", "b", capacity: 40);
            topology.AddEdge("b", "Meta_b", capacity: 40);
            topology.AddEdge("Meta_c", "c", capacity: 40);
            topology.AddEdge("c", "Meta_c", capacity: 40);
            topology.AddEdge("Meta_d", "d", capacity: 40);
            topology.AddEdge("d", "Meta_d", capacity: 40);

            demands = new Dictionary<(string, string), double>();
            demands.Add(("Meta_a", "Meta_d"), 10);
            demands.Add(("Meta_b", "Meta_d"), 5);
            demands.Add(("Meta_a", "Meta_c"), 5);
            demands.Add(("Meta_c", "Meta_d"), 0);
            demands.Add(("Meta_a", "Meta_b"), 0);
            demands.Add(("a", "d"), 0);
            demands.Add(("b", "d"), 0);
            demands.Add(("a", "c"), 0);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);

            var MetaNodesToEdges = new Dictionary<string, HashSet<string>>();
            MetaNodesToEdges["Meta_a"] = new HashSet<string> { "a" };
            MetaNodesToEdges["Meta_b"] = new HashSet<string> { "b" };
            MetaNodesToEdges["Meta_c"] = new HashSet<string> { "c" };
            MetaNodesToEdges["Meta_d"] = new HashSet<string> { "d" };
            Dictionary<(string, string), string[][]> primaryPaths = new Dictionary<(string, string), string[][]>();
            Dictionary<(string, string), string[][]> backupPaths = new Dictionary<(string, string), string[][]>();
            primaryPaths.Add(("a", "d"), new string[][] { new string[] { "a", "b", "d" }, new string[] { "Meta_a", "a", "c", "d", "Meta_d" } });
            primaryPaths.Add(("b", "d"), new string[][] { new string[] { "b", "d" } });
            primaryPaths.Add(("a", "c"), new string[][] { new string[] { "a", "c" } });
            primaryPaths.Add(("Meta_a", "Meta_d"), new string[][] { new string[] { "Meta_a", "a", "b", "d", "Meta_d" } });
            primaryPaths.Add(("Meta_b", "Meta_d"), new string[][] { new string[] { "Meta_b", "b", "d", "Meta_d" } });
            primaryPaths.Add(("Meta_a", "Meta_c"), new string[][] { new string[] { "Meta_a", "a", "c", "Meta_c" } });
            // backupPaths.Add(("a", "d"), new string[][] { new string[] { "a", "c", "d" } });
            // backupPaths.Add(("Meta_a", "Meta_d"), new string[][] { new string[] { "Meta_a", "a", "c", "d", "Meta_d" } });
            solver.CleanAll();
            var failureEncoder = new FailureAnalysisEncoderWithUnequalPaths<TVar, TSolution>(solver);
            var adversarialGeneratorUnequalPaths = new FailureAnalysisAdversarialGeneratorForUnequalPaths<TVar, TSolution>(topology,  metaNodeToActualNode: MetaNodesToEdges);
            var (optimalSol2, failureSol2) = adversarialGeneratorUnequalPaths.MaximizeOptimalityGap(optimalEncoder, failureEncoder, primaryPaths, backupPaths, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList);
            // Check that only one link has failed.
            numLinkDown = adversarialGeneratorUnequalPaths.GetLinkDownEvents();
            linkDowns = numLinkDown.Select(x => x ? 1 : 0).Sum();
            Assert.AreEqual(linkDowns, 1);

            Assert.IsTrue(Utils.IsApproximately(10, optimalSol2.MaxObjective - failureSol2.MaxObjective));
        }
        /// <summary>
        /// Same as above, but adding one more path option and increasing the
        /// number of extra path option as well. This one is a bit of a toy test. Will gradually make it more complex.
        /// </summary>
        [TestMethod]
        public void adversarialGenWithPrimalDualAndConstrainedDemands2()
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
            topology.AddEdge("a", "d", capacity: 5);

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            demandSet.Add(5);
            demandSet.Add(10);
            var demandList = new GenericList(demandSet);

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1);

            // Check that only one link has failed.
            var numLinkDown = adversarialGenerator.GetLinkDownEvents();
            var linkDowns = numLinkDown.Select(x => x ? 1 : 0).Sum();
            Assert.AreEqual(linkDowns, 1);

            Assert.IsTrue(Utils.IsApproximately(5, optimalSol.MaxObjective - failureSol.MaxObjective));
        }
        /// <summary>
        /// Same as above, but adding one more path option and increasing the number of
        /// extra path option as well. Compared to the above test, it is adding one more path to the paths available.
        /// </summary>
        [TestMethod]
        public void adversarialGenWithPrimalDualAndLags()
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
            topology.AddEdge("a", "d", capacity: 5);
            topology.AddEdge("b", "c", capacity: 3);
            topology.AddLinkToEdge("a", "b", "1", 0.01);
            topology.AddLinkToEdge("a", "b", "2", 0.01);
            topology.AddLinkToEdge("a", "d", "1", 0.01);
            topology.AddLinkToEdge("a", "d", "2", 0.01);
            topology.AddLinkToEdge("a", "c", "1", 0.01);
            topology.AddLinkToEdge("a", "c", "2", 0.01);
            topology.AddLinkToEdge("b", "d", "1", 0.01);
            topology.AddLinkToEdge("b", "d", "2", 0.01);
            topology.AddLinkToEdge("c", "d", "1", 0.01);
            topology.AddLinkToEdge("b", "c", "1", 0.01);

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            demands.Add(("b", "c"), 0);
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            demandSet.Add(2.0);
            demandSet.Add(3.0);
            demandSet.Add(5);
            demandSet.Add(10);
            var demandList = new GenericList(demandSet);

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, useLinkFailures: true);

            // Check that only one link has failed.
            var numLinkDown = adversarialGenerator.GetLinkDownEvents();
            var linkDowns = numLinkDown.Select(x => x ? 1 : 0).Sum();
            Assert.AreEqual(linkDowns, 0);

            Assert.IsTrue(Utils.IsApproximately(0, optimalSol.MaxObjective - failureSol.MaxObjective));
        }
        /// <summary>
        /// This time we are going to test whether we are modeling probabilities correctly.
        /// We start with a simple case: we are only allowing one-failure but the failure proability has to be above
        /// a threshold. We test two scenarios to see if the solver does the right thing.
        /// </summary>
        [TestMethod]
        public void adversarialGeneratorWithProbabilityThreshold()
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
            topology.AddEdge("a", "d", capacity: 5);
            topology.AddEdge("b", "c", capacity: 3);

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            demands.Add(("b", "c"), 0);
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            demandSet.Add(5);
            demandSet.Add(10);
            var demandList = new GenericList(demandSet);

            var probs = new Dictionary<(string, string), double>();
            probs[("a", "d")] = 0.3;
            probs[("b", "d")] = 0.2;
            probs[("a", "c")] = 0;
            probs[("a", "b")] = 0;
            probs[("c", "d")] = 0;
            probs[("b", "c")] = 0;

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, failureProbThreshold: 0.25);

            Assert.IsTrue(Utils.IsApproximately(0, optimalSol.MaxObjective - failureSol.MaxObjective));

            solver.CleanAll();
            (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, scenarioProbThreshold: 0.23);

            Assert.IsTrue(Utils.IsApproximately(0, optimalSol.MaxObjective - failureSol.MaxObjective));

            solver.CleanAll();

            (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, failureProbThreshold: 0.1);
            Assert.IsTrue(Utils.IsApproximately(2, optimalSol.MaxObjective - failureSol.MaxObjective));

            solver.CleanAll();

            (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, scenarioProbThreshold: 0.14);
            Assert.IsTrue(Utils.IsApproximately(2, optimalSol.MaxObjective - failureSol.MaxObjective));

            // Next allow for two failures.
            solver.CleanAll();
            probs[("a", "d")] = 0;
            probs[("b", "d")] = 0.2;
            probs[("a", "c")] = 0.3;
            probs[("a", "b")] = 0;
            probs[("c", "d")] = 0;
            probs[("b", "c")] = 0;
            (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 2, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, failureProbThreshold: 0.05);
            Assert.IsTrue(Utils.IsApproximately(12, optimalSol.MaxObjective - failureSol.MaxObjective));

            solver.CleanAll();
            (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 2, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, failureProbThreshold: 0.1);
            Assert.IsTrue(Utils.IsApproximately(2, optimalSol.MaxObjective - failureSol.MaxObjective));
        }
        /// <summary>
        /// Tests that the instance based gap generator is correct.
        /// </summary>
        [TestMethod]
        public void testGetGapMethod()
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

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 5);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            var capacities = new Dictionary<(string, string), double>();
            capacities.Add(("a", "b"), 10);
            capacities.Add(("a", "c"), 10);
            capacities.Add(("b", "d"), 10);
            capacities.Add(("c", "d"), 0);

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            adversarialGenerator.SetPath(1);
            var gap = adversarialGenerator.GetGap(optimalEncoder, optimalCutEncoder, demands, capacities, InnerRewriteMethodChoice.PrimalDual);
            Assert.IsTrue(Utils.IsApproximately(5, gap.Item1));
        }
        /// <summary>
        /// This method tests that if I run the adversarial generator but I pre-specify the demands then I get the same demands back.
        /// </summary>
        [TestMethod]
        public void TestDemandInitializationsWork()
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
            topology.AddEdge("a", "d", capacity: 5);
            topology.AddEdge("b", "c", capacity: 3);

            var demands = new Dictionary<(string, string), double>();
            demands.Add(("a", "d"), 10);
            demands.Add(("b", "d"), 5);
            demands.Add(("a", "c"), 3);
            demands.Add(("c", "d"), 0);
            demands.Add(("a", "b"), 0);
            demands.Add(("b", "c"), 0);
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            demandSet.Add(3.0);
            demandSet.Add(5);
            demandSet.Add(10);
            var demandList = new GenericList(demandSet);

            var probs = new Dictionary<(string, string), double>();
            probs[("a", "d")] = 0.3;
            probs[("b", "d")] = 0.2;
            probs[("a", "c")] = 0;
            probs[("a", "b")] = 0;
            probs[("c", "d")] = 0;
            probs[("b", "c")] = 0;

            var solver = CreateSolver();
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, 2);
            var optimalCutEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(optimalEncoder, optimalCutEncoder, innerEncoding: InnerRewriteMethodChoice.PrimalDual, constrainedDemands: demands, maxNumFailures: 1, demandList: demandList, numExtraPaths: 1, lagFailureProbabilities: probs, failureProbThreshold: 0.25);
            var outputDemands = failureSol.Demands;
            Assert.IsTrue(Utils.IsApproximately(outputDemands[("a", "d")], demands[("a", "d")]));
            Assert.IsTrue(Utils.IsApproximately(outputDemands[("a", "c")], demands[("a", "c")]));
            Assert.IsTrue(Utils.IsApproximately(outputDemands[("a", "b")], demands[("a", "b")]));
            Assert.IsTrue(Utils.IsApproximately(outputDemands[("b", "d")], demands[("b", "d")]));
            Assert.IsTrue(Utils.IsApproximately(outputDemands[("c", "d")], demands[("c", "d")]));
        }
        /// <summary>
        /// Tests the rndCapacityGenerator to make sure it does the right thing for clustering.
        /// </summary>
        [TestMethod]
        public void testRndCapacityGeneratorForClustering()
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
            topology.AddEdge("a", "d", capacity: 5);

            int maxNumFailures = 1;
            var probs = new Dictionary<(string, string), double>();
            probs[("a", "d")] = 0.6;
            probs[("b", "d")] = 0.2;
            probs[("a", "c")] = 0;
            probs[("a", "b")] = 0;
            probs[("c", "d")] = 0;
            // Lets first not give it failure probabilities.
            Dictionary<(string, string), double> capacities = null;
            var failureAnalysisGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            int i = 0;
            while (capacities == null && i < 100)
            {
                Console.WriteLine("we are currently at iteration {0}", i++);
                capacities = failureAnalysisGenerator.getRandomCapacities(maxNumFailures: maxNumFailures, lagFailureProbabilities: probs);
            }
            if (capacities == null)
            {
                Console.WriteLine("I was not able to generate a none-null capacity");
            }
            if (capacities != null)
            {
                Assert.AreEqual(capacities.Count, 5);
                Assert.IsTrue(capacities.Select(x => x.Value > 0 ? 0 : 1).Sum() <= maxNumFailures);
                Assert.AreEqual(capacities[("a", "c")], 10);
                Assert.AreEqual(capacities[("a", "b")], 10);
                Assert.AreEqual(capacities[("c", "d")], 10);
            }
            capacities = null;
            int exactNumFailures = 1;
            failureAnalysisGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            i = 0;
            while (capacities == null && i < 100)
            {
                Console.WriteLine("we are currently at iteration {0}", i++);
                capacities = failureAnalysisGenerator.getRandomCapacities(exactNumFailures: exactNumFailures, lagFailureProbabilities: probs);
            }
            if (capacities == null)
            {
                Console.WriteLine("I was not able to generate a none-null capacity");
            }
            if (capacities != null)
            {
                Assert.AreEqual(capacities.Count, 5);
                Assert.IsTrue(capacities.Select(x => x.Value > 0 ? 0 : 1).Sum() <= maxNumFailures);
                Assert.AreEqual(capacities[("a", "c")], 10);
                Assert.AreEqual(capacities[("a", "b")], 10);
                Assert.AreEqual(capacities[("c", "d")], 10);
            }
            probs[("a", "d")] = 0.3;
            probs[("b", "d")] = 0.2;
            probs[("a", "c")] = 0;
            probs[("a", "b")] = 0;
            probs[("c", "d")] = 0;
            double failureProbThreshold = 0.25;
            failureAnalysisGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            i = 0;
            capacities = null;
            while (capacities == null && i < 100)
            {
                Console.WriteLine("we are currently at iteration {0}", i++);
                capacities = failureAnalysisGenerator.getRandomCapacities(lagFailureProbabilities: probs, failureProbThreshold: failureProbThreshold);
            }
            if (capacities == null)
            {
                Console.WriteLine("I was not able to generate a none-null capacity");
            }
            if (capacities != null)
            {
                Assert.AreEqual(capacities.Count, 5);
                Assert.IsTrue((capacities.Select(x => x.Value > 0 ? 0 : 1).Sum() == 0) || (capacities[("a", "c")] == 0));
                Assert.AreEqual(capacities[("a", "c")], 10);
                Assert.AreEqual(capacities[("a", "b")], 10);
                Assert.AreEqual(capacities[("c", "d")], 10);
            }
            failureProbThreshold = 0.05;
            failureAnalysisGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(topology, 2);
            i = 0;
            capacities = null;
            while (capacities == null && i < 100)
            {
                Console.WriteLine("we are currently at iteration {0}", i++);
                capacities = failureAnalysisGenerator.getRandomCapacities(lagFailureProbabilities: probs, failureProbThreshold: failureProbThreshold);
            }
            if (capacities == null)
            {
                Console.WriteLine("I was not able to generate a none-null capacity");
            }
            if (capacities != null)
            {
                Assert.AreEqual(capacities.Count, 5);
                Assert.IsTrue(capacities.Select(x => x.Value > 0 ? 0 : 1).Sum() <= 2);
                Assert.AreEqual(capacities[("a", "c")], 10);
                Assert.AreEqual(capacities[("a", "b")], 10);
                Assert.AreEqual(capacities[("c", "d")], 10);
            }
        }
    }
}