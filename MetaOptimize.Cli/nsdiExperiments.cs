// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using System.Linq.Expressions;
    using System.Runtime.InteropServices;
    using Gurobi;
    using MetaOptimize.Cli;
    /// <summary>
    /// Implements evaluations for NSDI.
    /// </summary>
    public static class NSDIExp
    {
        /// <summary>
        /// evaluating gap vs time for different methods on DP (large scale with clustering).
        /// </summary>
        public static void compareLargeScaleGapDelayDiffMethodsDP()
        {
            // comparison parameters
            bool baselineGrey = false;
            bool runMetaopt = true;
            bool runHillClimber = false;
            bool runSimAnnealing = false;
            bool runRandom = false;
            bool metaOptRndInit = false;
            int gurobiVerbose = 1;
            bool metaOptVerbose = true;
            List<int> seedList = new List<int>() { 0, 1, 2 };
            // topo parameters
            // string topoName = "Cogentco";
            string topoName = "b4";
            string topoPath = "";
            string clusterDir = null;
            string pathFile = null;
            int numClusters = 1;
            double downScaleFactor = 1;
            bool enableClustering = false;
            int clusterVersion = -1;
            double perClusterTimeout = 0;
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                clusterDir = @"../Topologies/partition_log/Cogentco_10_fm_partitioning/";
                pathFile = @"../Topologies/outputs/paths/Cogentco_sp.json";
                numClusters = 10;
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterTimeout = 1200;
            }
            else if (topoName == "b4")
            {
                topoPath = @"../Topologies/b4-teavar.json";
                pathFile = @"../Topologies/outputs/paths/b4_sp.json";
                perClusterTimeout = 5000;
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                    numClusters, clusterDir, false);
            double linkCap = topology.AverageCapacity();
            int numPaths = 4;
            // solver parameters
            int numThreads = 1;
            int numProcessors = MachineStat.numProcessors;
            // heuristic parameters
            var heuristicName = Heuristic.ExpectedPop;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            // dp variables
            var demandUBRatio = 0.5;
            double demandPinningRatio = 0.05;
            // pop variables
            int numSlices = 2;
            int numSamples = 5;
            // timeout for baselines
            double timeout = 43800;
            if (topoName == "Cogentco")
            {
                if (demandPinningRatio == 0.01)
                {
                    timeout = 63945;
                }
                if (heuristicName == Heuristic.ExpectedPop)
                {
                    timeout = 28295;
                }
            }
            else if (topoName == "b4")
            {
                if (heuristicName == Heuristic.ExpectedPop)
                {
                    timeout = 1200;
                }
                else
                {
                    if (demandPinningRatio == 0.01)
                    {
                        timeout = 3600;
                    }
                    else
                    {
                        timeout = 1500;
                    }
                }
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            // compute gap
            var demandUB = linkCap * demandUBRatio;
            var demandPinningThreshold = linkCap * demandPinningRatio;
            var partition = topology.RandomPartition(numSlices);
            var partitionsList = new List<IDictionary<(string, string), int>>();
            for (int i = 0; i < numSamples; i++)
            {
                partitionsList.Add(topology.RandomPartition(numSlices));
            }
            Console.WriteLine(
                String.Format("======== link cap {0}, demand UB {1}, demandThresh {2}", linkCap, demandUB, demandPinningThreshold));
            var demandSet = new HashSet<double>();
            demandSet.Add(0.0);
            if (heuristicName == Heuristic.DemandPinning)
            {
                demandSet.Add(demandPinningThreshold);
            }
            demandSet.Add(demandUB);
            var demandList = new GenericList(demandSet);
            string logDir = @"../logs/gap_vs_time/" + topoName + "_" + numClusters + "_" + heuristicName
                                + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
            logDir = logDir + Utils.GetFID() + @"/";
            ISolver<GRBVar, GRBModel> solver;
            // Primal-Dual
            if (runMetaopt)
            {
                string kktFile = @"primal_dual_" + heuristicName + "_rnd_init_" + metaOptRndInit + ".txt";
                solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(perClusterTimeout, gurobiVerbose, numThreads, recordProgress: true,
                                                        logPath: Path.Combine(logDir, kktFile), focusBstBd: false);
                var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, numSlices: numSlices, demandPinningThreshold: demandPinningThreshold,
                                                    partition: partition, numSamples: numSamples, partitionsList: partitionsList, InnerEncoding: innerEncoding,
                                                    scaleFactor: downScaleFactor);
                var (heuristicDirectEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                        h: heuristicName, numPaths: numPaths, numSlices: numSlices, demandPinningThreshold: demandPinningThreshold,
                                                        partition: partition, numSamples: numSamples, partitionsList: partitionsList, InnerEncoding: innerEncoding,
                                                        scaleFactor: downScaleFactor, DirectEncoder: true);
                var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                // comment to go to hill climber and others
                (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                            demandUB, innerEncoding, demandList, enableClustering, clusterVersion, clusters, -1, -1, -1, false, metaOptVerbose, 1.0, -1, -1, -1,
                            metaOptRndInit, heuristicDirectEncoder);
                double optimal = result.Item1.MaxObjective;
                double heuristic = result.Item2.MaxObjective;
                var gap = optimal - heuristic;
                string demandFile = logDir + @"/metaopt/" + topoName + "_" + heuristicName + "_" +
                                numPaths + "_" + demandUB + "_" + demandPinningThreshold + "_" + perClusterTimeout + "_" + Utils.GetFID();
                Utils.writeDemandsToFile(demandFile, result.Item1.Demands);
                Console.WriteLine("==== PrimalDual --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            }
            solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(timeout, 0, numThreads, recordProgress: false);
            foreach (var seed in seedList)
            {
                if (runHillClimber)
                {
                    double stddev = 0.01 * linkCap;
                    string hillClimbingFile = @"hillclimbing_" + heuristicName + "_" + seed + "_" + stddev + "_grey_" + baselineGrey + ".txt";
                    Console.WriteLine("======== stddev = " + stddev);
                    int numNeighbors = 50;
                    int numDemands = 10000000;
                    solver.CleanAll();
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths,
                                                    demandPinningThreshold: demandPinningThreshold, partition: partition, numSamples: numSamples,
                                                    partitionsList: partitionsList, DirectEncoder: true, numSlices: numSlices);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    var result = adversarialInputGenerator.HillClimbingAdversarialGenerator(optimalEncoder, heuristicEncoder, numTrials: numDemands,
                        numNeighbors: numNeighbors, demandUB: demandUB, stddev: stddev, grey: baselineGrey, seed: seed, storeProgress: true,
                        logPath: Path.Combine(logDir, hillClimbingFile), timeout: timeout, verbose: true);
                    var optimal = result.Item1.MaxObjective;
                    var heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    string demandFile = logDir + @"/hillclimber/" + topoName + "_" + heuristicName + "_" +
                                    numPaths + "_" + demandUB + "_" + demandPinningThreshold + "_" + timeout + "_" + stddev +
                                    "_grey_" + baselineGrey + "_" + Utils.GetFID();
                    Utils.writeDemandsToFile(demandFile, result.Item1.Demands);
                    Console.WriteLine("==== HillClimber --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                }

                if (runSimAnnealing)
                {
                    double stddev = 0.01 * linkCap;
                    Console.WriteLine("======== stddev = " + stddev);
                    string simulatedAnnealingFile = @"simulatedannealing_" + heuristicName + "_" + seed + "_" + stddev + "_grey_" + baselineGrey + ".txt";
                    var numNeighbors = 50;
                    int numTmpSteps = 10000000;
                    int resetNoImprovement = Convert.ToInt32(1 * numNeighbors);
                    double initialTmp = 500 * downScaleFactor;
                    double tmpDecreaseFactor = 0.5;
                    solver.CleanAll();
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths,
                                                    demandPinningThreshold: demandPinningThreshold, partition: partition, numSamples: numSamples,
                                                    partitionsList: partitionsList, DirectEncoder: true, numSlices: numSlices);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    var result = adversarialInputGenerator.SimulatedAnnealing(optimalEncoder, heuristicEncoder, numTmpSteps,
                        numNeighbors, demandUB, stddev, initialTmp: initialTmp, tmpDecreaseFactor: tmpDecreaseFactor,
                        numNoIncreaseToReset: resetNoImprovement, grey: baselineGrey, seed: seed, storeProgress: true,
                        logPath: Path.Combine(logDir, simulatedAnnealingFile), timeout: timeout, verbose: true);
                    var optimal = result.Item1.MaxObjective;
                    var heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    var demandFile = logDir + @"/simulatedAnnealing/" + topoName + "_" + heuristicName + "_" +
                                    numPaths + "_" + demandUB + "_" + demandPinningThreshold + "_" + timeout + "_" + stddev +
                                    "_grey_" + baselineGrey + "_" + Utils.GetFID();
                    Utils.writeDemandsToFile(demandFile, result.Item1.Demands);
                    Console.WriteLine("==== SA --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                }

                if (runRandom)
                {
                    string randomSearchFile = @"randomSearch_" + heuristicName + "_" + seed + "_grey_" + baselineGrey + ".txt";
                    var numDemands = 10000000;
                    solver.CleanAll();
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths,
                                                    demandPinningThreshold: demandPinningThreshold, partition: partition, numSamples: numSamples,
                                                    partitionsList: partitionsList, DirectEncoder: true, numSlices: numSlices);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    var result = adversarialInputGenerator.RandomAdversarialGenerator(optimalEncoder, heuristicEncoder, numDemands,
                        demandUB, grey: baselineGrey, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, randomSearchFile),
                        timeout: timeout, verbose: true);
                    var optimal = result.Item1.MaxObjective;
                    var heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    var demandFile = logDir + @"/adversarial_demands/random/" + topoName + "_" + heuristicName + "_" +
                                    numPaths + "_" + demandUB + "_" + demandPinningThreshold + "_" + timeout +
                                    "_grey_" + baselineGrey + "_" + Utils.GetFID();
                    Utils.writeDemandsToFile(demandFile, result.Item1.Demands);
                    Console.WriteLine("==== Random --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                }
            }
        }

        /// <summary>
        /// sweeping through different values of pinned demand for
        /// B4, SWAN and Abilene topologies.
        /// </summary>
        public static void gapThresholdDemandPinningForDifferentTopologies()
        {
            // topo parameters
            var topologies = new Dictionary<string, string>();
            // topologies["B4"] = @"../Topologies/b4-teavar.json";
            topologies["SWAN"] = @"../Topologies/swan.json";
            // topologies["Abilene"] = @"../Topologies/abilene.json";
            int numPaths = 4;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            // heuristic parameters
            var heuristicName = Heuristic.DemandPinning;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            // timeout for baselines
            double timeToTerminate = 1800;
            // log files
            string logDir = @"../logs/demand_pinning_sweep_thresh/" + Utils.GetFID() + @"/";
            double start = 5;
            double step = 2.5;
            double end = 12.5;
            double demandUBRatio = 0.5;
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);
            foreach (var (topoName, topoPath) in topologies)
            {
                var topology = Parser.ReadTopologyJson(topoPath);
                double linkCap = topology.AverageCapacity();
                string logFile = topoName + @"_" + heuristicName + ".txt";
                Utils.CreateFile(logDir, logFile, removeIfExist: true);
                Utils.AppendToFile(logDir, logFile, linkCap.ToString());
                for (double i = start; i <= end; i += step)
                {
                    solver.CleanAll();
                    var threshold = i * linkCap / 100;
                    var demandUB = demandUBRatio * linkCap;
                    var demandSet = new HashSet<double>();
                    demandSet.Add(0.0);
                    demandSet.Add(threshold);
                    demandSet.Add(demandUB);
                    var demandList = new GenericList(demandSet);
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, demandPinningThreshold: threshold,
                                                    InnerEncoding: innerEncoding);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    // comment to go to hill climber and others
                    (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                                demandUB, innerEncoding, demandList, false, -1, null, -1, -1, -1, false, true, 1.0, -1, -1, -1,
                                false, null);
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    Utils.AppendToFile(logDir, logFile, i + ", " + threshold + ", " + optimal + ", " + heuristic + ", " + gap);
                    Console.WriteLine("==== Gap --> " + " i=" + i + " threshold=" + threshold + " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                }
            }
        }

        /// <summary>
        /// Vaying the number of nodes in a random regular topology and seeing the
        /// gap vs num nodes effect.
        /// </summary>
        public static void ImpactNumNodesRadixSmallWordTopoDemandPinning()
        {
            List<int> seedList = new List<int>() { 0, 1, 2 };
            string logDir = @"../logs/demand_pinning_sweep_topo/" + Utils.GetFID() + @"/";
            string logFile = @"small_word_graphs_" + Heuristic.DemandPinning + ".txt";
            Utils.CreateFile(logDir, logFile, removeIfExist: true);
            int numPaths = 4;
            int capacity = 5000;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            // heuristic parameters
            var heuristicName = Heuristic.DemandPinning;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            double demandUBRatio = 0.5;
            double demandPinningRatio = 0.05;
            // timeout for baselines
            double timeToTerminate = 1800;
            // evaluation sweep parameters
            int startNodes = 9;
            int stepNodes = 2;
            int endNodes = 13;
            int startRadix = 2;
            int stepRadix = 2;
            int endRadix = 8;

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);
            for (int sn = startNodes; sn <= endNodes; sn += stepNodes)
            {
                for (int sr = startRadix; sr <= endRadix; sr += stepRadix)
                {
                    solver.CleanAll();
                    var topology = Topology.SmallWordGraph(sn, sr, capacity);
                    var num_links = topology.GetAllEdges().Count();
                    var linkCap = topology.AverageCapacity();
                    var threshold = demandPinningRatio * linkCap;
                    var demandUB = demandUBRatio * linkCap;
                    Console.WriteLine(String.Format("=== {0}, {1}, {2}, {3}, {4}, {5}", sn, sr, num_links, linkCap, demandUB, threshold));
                    var demandSet = new HashSet<double>();
                    demandSet.Add(0.0);
                    demandSet.Add(threshold);
                    demandSet.Add(demandUB);
                    var demandList = new GenericList(demandSet);
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, demandPinningThreshold: threshold,
                                                    InnerEncoding: innerEncoding);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    // comment to go to hill climber and others
                    (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                                demandUB, innerEncoding, demandList, false, -1, null, -1, -1, -1, false, true, 1.0, -1, -1, -1,
                                false, null);
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    double gap = optimal - heuristic;
                    var diameter = topology.diameter();
                    var avgShortestPathLen = topology.avgShortestPathLength();
                    Utils.AppendToFile(logDir, logFile, sn + ", " + sr + ", " + numPaths + ", " + num_links + ", " +
                        threshold + ", " + diameter + ", " + avgShortestPathLen + ", " + optimal + ", " + heuristic + ", " + gap);
                    Console.WriteLine("==== Gap --> " + " numNodes=" + sn + " numRadix=" + sr + " numPaths=" + numPaths + " threshold=" + threshold +
                        " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                }
            }
        }

        /// <summary>
        /// evaluating impact of number of paths and partitions for pop.
        /// </summary>
        public static void ImpactNumPathsPartitionsExpectedPop()
        {
            var topology = Parser.ReadTopologyJson(@"../Topologies/b4-teavar.json");
            double linkCap = topology.AverageCapacity();
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = 1;
            // heuristic parameters
            Heuristic heuristicName = Heuristic.ExpectedPop;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            // pop variables
            double demandUBRatio = 0.5;
            int numSamples = 5;
            // sweep values
            int minPartition = 2;
            int maxPartition = 5;
            int partitionStep = 1;
            int minPaths = 1;
            int maxPaths = 4;
            int pathStep = 1;
            string fid = Utils.GetFID();
            string logDir = @"../logs/pop_diff_paths_diff_partitions/" + heuristicName + "_" + fid + "/";
            string kktFile = @"kkt_" + heuristicName + ".txt";
            int timeout = 1800;
            Utils.CreateFile(logDir, kktFile, true);
            int numPartitions = minPartition;
            double demandUB = demandUBRatio * linkCap;
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(verbose: 1, timeToTerminateNoImprovement: timeout);

            while (numPartitions <= maxPartition)
            {
                var partition = topology.RandomPartition(numPartitions);
                var partitionsList = new List<IDictionary<(string, string), int>>();
                for (int i = 0; i < numSamples; i++)
                {
                    partitionsList.Add(topology.RandomPartition(numPartitions));
                }
                int numPaths = minPaths;
                while (numPaths <= maxPaths)
                {
                    solver.CleanAll();
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, numSlices: numPartitions, partition: partition,
                                                    partitionsList: partitionsList, numSamples: numSamples, InnerEncoding: innerEncoding);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    var demandSet = new HashSet<double>();
                    demandSet.Add(0.0);
                    demandSet.Add(demandUB);
                    var demandList = new GenericList(demandSet);
                    // comment to go to hill climber and others
                    (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                                demandUB, innerEncoding, demandList, false, -1, null, -1, -1, -1, false, true, 1.0, -1, -1, -1, false, null);
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    Utils.AppendToFile(logDir, kktFile, numPartitions + ", " + numPaths + ", " + gap);
                    Console.WriteLine("==== KKT --> " + " partition=" + numPartitions + " paths=" + numPaths + " gap=" + gap +
                            " optimal=" + optimal + " heuristic=" + heuristic);
                    // }
                    numPaths += pathStep;
                }
                numPartitions += partitionStep;
            }
        }

        /// <summary>
        /// evaluating impact of number of paths and partitions for pop.
        /// </summary>
        public static void ImpactNumSamplesExpectedPop()
        {
            var topology = Parser.ReadTopologyJson(@"../Topologies/b4-teavar.json");
            double linkCap = topology.AverageCapacity();
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = 1;
            // heuristic parameters
            Heuristic heuristicName = Heuristic.ExpectedPop;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            // pop variables
            double demandUBRatio = 0.5;
            int numPartitions = 2;
            int numPaths = 4;
            // sweep values
            int minSamples = 1;
            int maxSamples = 10;
            int sampleStep = 2;
            string fid = Utils.GetFID();
            string logDir = @"../logs/pop_diff_num_samples/" + heuristicName + "_" + fid + "/";
            string kktFile = @"kkt_" + heuristicName + ".txt";
            int timeout = 1800;
            Utils.CreateFile(logDir, kktFile, true);
            int numSamples = minSamples;
            double demandUB = demandUBRatio * linkCap;
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(verbose: 1, timeToTerminateNoImprovement: timeout);

            var partition = topology.RandomPartition(numPartitions);
            var partitionsList = new List<IDictionary<(string, string), int>>();
            for (int i = 0; i < numSamples; i++)
            {
                partitionsList.Add(topology.RandomPartition(numPartitions));
            }
            while (numSamples <= maxSamples)
            {
                solver.CleanAll();
                var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                h: heuristicName, numPaths: numPaths, numSlices: numPartitions, partition: partition,
                                                partitionsList: partitionsList, numSamples: numSamples, InnerEncoding: innerEncoding);
                var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                var demandSet = new HashSet<double>();
                demandSet.Add(0.0);
                demandSet.Add(demandUB);
                var demandList = new GenericList(demandSet);
                // comment to go to hill climber and others
                (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                            demandUB, innerEncoding, demandList, false, -1, null, -1, -1, -1, false, true, 1.0, -1, -1, -1, false, null);
                double optimal = result.Item1.MaxObjective;
                double heuristic = result.Item2.MaxObjective;
                var gap = optimal - heuristic;
                var demands = new Dictionary<(string, string), double>(result.Item1.Demands);
                for (int i = 0; i < 100 - numSamples; i++)
                {
                    topology.RandomPartition(numPartitions);
                }
                int num_r = 10;
                double sum_r = 0;
                var randomPartitionList = new List<IDictionary<(string, string), int>>();
                for (int i = 0; i < num_r; i++)
                {
                    solver.CleanAll();
                    var newPartition = topology.RandomPartition(numPartitions);
                    randomPartitionList.Add(newPartition);
                    var newHeuristicEncoder = new PopEncoder<GRBVar, GRBModel>(solver, maxNumPaths: numPaths, numPartitions: numPartitions, demandPartitions: newPartition);
                    var encodingHeuristic = newHeuristicEncoder.Encoding(topology, demandEqualityConstraints: demands, noAdditionalConstraints: true);
                    var solverSolutionHeuristic = newHeuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
                    var optimizationSolutionHeuristic = (TEMaxFlowOptimizationSolution)newHeuristicEncoder.GetSolution(solverSolutionHeuristic);
                    var demandMet = optimizationSolutionHeuristic.MaxObjective;
                    Console.WriteLine("random sample" + i + "-->" + "total demand = " + demandMet + " gap = " + (optimal - demandMet));
                    sum_r += (optimal - demandMet);
                }
                Console.WriteLine("==================== avg gap on " + num_r + " random partitions: " + (sum_r / num_r));
                var test_gap = sum_r / num_r;
                Utils.AppendToFile(logDir, kktFile, numSamples + ", " + numPartitions + ", " + numPaths + ", " + gap + ", " + test_gap);
                Console.WriteLine("==== KKT --> " + " partition=" + numPartitions + " paths=" + numPaths + " gap=" + gap +
                        " optimal=" + optimal + " heuristic=" + heuristic);
                numPartitions += sampleStep;
                for (int i = 0; i < sampleStep; i++)
                {
                    partitionsList.Add(topology.RandomPartition(numPartitions));
                }
            }
        }

        /// <summary>
        /// adding realistic constraints for large topologies.
        /// </summary>
        public static void AddRealisticConstraintsDP()
        {
            // topo parameters
            string topoName = "Cogentco";
            // string topoName = "b4";
            bool metaOptRndInit = false;
            string topoPath = "";
            string clusterDir = null;
            string pathFile = null;
            int numClusters = 1;
            double downScaleFactor = 1;
            bool enableClustering = false;
            int clusterVersion = -1;
            double perClusterTimeout = 0;
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                clusterDir = @"../Topologies/partition_log/Cogentco_10_fm_partitioning/";
                pathFile = @"../Topologies/outputs/paths/Cogentco_sp.json";
                numClusters = 10;
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterTimeout = 1200;
            }
            else if (topoName == "b4")
            {
                topoPath = @"../Topologies/b4-teavar.json";
                perClusterTimeout = 5000;
            }
            else if (topoName == "Uninett2010")
            {
                topoPath = @"../Topologies/Uninett2010.json";
                clusterDir = @"../Topologies/partition_log/Uninett2010_8_fm_partitioning/";
                pathFile = @"../Topologies/outputs/paths/Uninett2010_sp.json";
                numClusters = 8;
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterTimeout = 1200;
                // perClusterTimeout = 27691;
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                    numClusters, clusterDir, false);
            var avgLinkCap = Math.Round(topology.AverageCapacity(), 4);
            int numPaths = 4;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            // hueristic parameters
            var heuristicName = Heuristic.DemandPinning;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            // dp variables
            var demandUBRatio = 0.5;
            var demandPinningRatio = 0.05;
            // pop variables
            int numSlices = 2;
            int numSamples = 5;
            var partition = topology.RandomPartition(numSlices);
            var partitionsList = new List<IDictionary<(string, string), int>>();
            for (int i = 0; i < numSamples; i++)
            {
                partitionsList.Add(topology.RandomPartition(numSlices));
            }
            // realistic parameters
            double density = 1.0;
            List<int> maxLargeDistanceList;
            if (topoName == "Cogentco")
            {
                maxLargeDistanceList = new List<int>() { 4 };
            }
            else if (topoName == "b4")
            {
                maxLargeDistanceList = new List<int>() { -1, 1, 2, 3, 4, 5 };
            }
            else if (topoName == "Uninett2010")
            {
                maxLargeDistanceList = new List<int>() { -1 };
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            var maxSmallDistanceList = new List<int>() { -1 };
            double largeDemandLB = 0.25 * avgLinkCap;
            int verbose = 1;

            // computing gap
            var demandPinningThreshold = Math.Round(demandPinningRatio * avgLinkCap, 4);
            var demandUB = demandUBRatio * avgLinkCap;
            Console.WriteLine(
                String.Format("======== avg link cap {0}, demand UB {1}, demandThresh {2}", avgLinkCap, demandUB, demandPinningThreshold));

            var demandSet = new HashSet<double>();
            demandSet.Add(0);
            if (heuristicName == Heuristic.DemandPinning)
            {
                demandSet.Add(demandPinningThreshold);
            }
            demandSet.Add(demandUB);
            var demandList = new GenericList(demandSet);
            // Primal-Dual
            string logDir = @"../logs/realistic_constraints/" + topoName + "_" + numClusters + "_" + heuristicName
                    + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
            logDir = logDir + Utils.GetFID() + @"/";
            string gapFile = @"gap.txt";
            Utils.CreateFile(logDir, gapFile, removeIfExist: false);

            foreach (var maxLargeDistance in maxLargeDistanceList)
            {
                foreach (var maxSmallDistance in maxSmallDistanceList)
                {
                    Console.WriteLine(
                        String.Format("=================== maxLargeDistance {0}, maxSmallDistance {1}, LargeDemandLB {2}", maxLargeDistance, maxSmallDistance, largeDemandLB));
                    string dirname = "primal_dual_" + heuristicName + "_density_" + density + "_maxLargeDistance_"
                            + maxLargeDistance + "_maxSmallDistance" + maxSmallDistance + "_LargeDemandLB_" + largeDemandLB + "/";
                    string demandFile = dirname + @"demands.txt";
                    string progressFile = dirname + @"progress.txt";
                    ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(perClusterTimeout, verbose, numThreads, recordProgress: true,
                                                        logPath: Path.Combine(logDir, progressFile), focusBstBd: false);
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, numSlices: numSlices, demandPinningThreshold: demandPinningThreshold,
                                                    partition: partition, numSamples: numSamples, partitionsList: partitionsList, InnerEncoding: innerEncoding,
                                                    scaleFactor: downScaleFactor);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                            demandUB, innerEncoding, demandList, enableClustering, clusterVersion, clusters, -1, -1, -1, false, false, density, largeDemandLB,
                            maxLargeDistance, maxSmallDistance, metaOptRndInit, null);
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    Utils.writeDemandsToFile(Path.Combine(logDir, demandFile), result.Item1.Demands);
                    Utils.AppendToFile(logDir, gapFile,
                                    String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", topoName, heuristicName, numClusters, numPaths,
                                                    perClusterTimeout, demandPinningRatio, numSlices, numSamples, demandUB, density, largeDemandLB,
                                                    maxLargeDistance, maxSmallDistance, numThreads, gap));
                    Console.WriteLine("==== PrimalDual --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                }
            }
        }
        /// <summary>
        /// ablation study of the clustering method.
        /// </summary>
        public static void AblationStudyClusteringOnDP()
        {
            // topo parameters
            string topoName = "Uninett2010";
            // string topoName = "b4";
            bool metaOptRndInit = false;
            string topoPath = "";
            var clusterDirList = new List<(string, string, int)>();
            string pathFile = null;
            double downScaleFactor = 1;
            bool enableClustering = false;
            int clusterVersion = -1;
            List<double> perClusterTimeoutList = new List<double>();
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_4_spectral_clustering/", "spectral_clustering", 4));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_4_fm_partitioning/", "fm_partitioning", 4));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_8_spectral_clustering/", "spectral_clustering", 8));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_8_fm_partitioning/", "fm_partitioning", 8));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_10_spectral_clustering/", "spectral_clustering", 10));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_10_fm_partitioning/", "fm_partitioning", 10));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_12_spectral_clustering/", "spectral_clustering", 12));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_12_fm_partitioning/", "fm_partitioning", 12));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_16_spectral_clustering/", "spectral_clustering", 16));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_16_fm_partitioning/", "fm_partitioning", 16));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_20_spectral_clustering/", "spectral_clustering", 20));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_20_fm_partitioning/", "fm_partitioning", 20));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_50_spectral_clustering/", "spectral_clustering", 50));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_50_fm_partitioning/", "fm_partitioning", 50));
                // clusterDirList.Add((@"../Topologies/partition_log/Cogentco_100_fm_partitioning/", "fm_partitioning", 100));
                clusterDirList.Add((@"../Topologies/partition_log/Cogentco_70_fm_partitioning/", "fm_partitioning", 70));
                pathFile = @"../Topologies/outputs/paths/Cogentco_sp.json";
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterTimeoutList.Add(30);
                // perClusterTimeoutList.Add(600);
                // perClusterTimeoutList.Add(1200);
                // perClusterTimeoutList.Add(1800);
                // perClusterTimeoutList.Add(2400);
            }
            else if (topoName == "Uninett2010")
            {
                topoPath = @"../Topologies/Uninett2010.json";
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_2_fm_partitioning/", "fm_partitioning", 2));
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_3_fm_partitioning/", "fm_partitioning", 3));
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_4_fm_partitioning/", "fm_partitioning", 4));
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_5_fm_partitioning/", "fm_partitioning", 5));
                clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_6_fm_partitioning/", "fm_partitioning", 6));
                clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_8_fm_partitioning/", "fm_partitioning", 8));
                clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_10_fm_partitioning/", "fm_partitioning", 10));
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_2_spectral_clustering/", "spectral_clustering", 2));
                // clusterDirList.Add((@"../Topologies/partition_log/Uninett2010_5_spectral_clustering/", "spectral_clustering", 5));
                pathFile = @"../Topologies/outputs/paths/Uninett2010_sp.json";
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                // perClusterTimeoutList.Add(600);
                perClusterTimeoutList.Add(1200);
                // perClusterTimeoutList.Add(1800);
                // perClusterTimeoutList.Add(2400);
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            int numPaths = 4;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            // hueristic parameters
            var heuristicName = Heuristic.DemandPinning;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            var demandUBRatio = 0.5;
            var demandPinningRatio = 0.05;
            int verbose = 1;
            // realistic parameters
            double density = 1.0;
            int maxLargeDistance = -1;
            int maxSmallDistance = -1;
            double largeDemandLB = -1;
            string fid = Utils.GetFID();

            foreach (var perClusterTimeout in perClusterTimeoutList)
            {
                foreach (var (clusterDir, clusterMethod, numClusters) in clusterDirList)
                {
                    Console.WriteLine(
                        String.Format("========== cluster method {0}, num clusters {1}, cluster dir {2}", clusterMethod, numClusters, clusterDir));
                    Console.WriteLine(String.Format("======== per cluster timeout: {0}", perClusterTimeout));
                    var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                            numClusters, clusterDir, false);
                    var avgLinkCap = topology.AverageCapacity();

                    // computing gap
                    var demandPinningThreshold = demandPinningRatio * avgLinkCap;
                    var demandUB = demandUBRatio * avgLinkCap;
                    Console.WriteLine(
                        String.Format("======== avg link cap {0}, demand UB {1}, demandThresh {2}", avgLinkCap, demandUB, demandPinningThreshold));

                    var demandSet = new HashSet<double>();
                    demandSet.Add(0);
                    demandSet.Add(demandPinningThreshold);
                    demandSet.Add(demandUB);
                    var demandList = new GenericList(demandSet);
                    // Primal-Dual
                    string logDir = @"../logs/parameter_tuning/primal_dual/" + topoName + "_" + heuristicName
                            + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
                    logDir = logDir + fid + @"/";
                    string gapFile = @"gap.txt";
                    Utils.CreateFile(logDir, gapFile, removeIfExist: false);
                    string dirname = clusterMethod + "_" + numClusters + "_" + perClusterTimeout + "/";
                    string demandFile = dirname + @"demands.txt";
                    string progressFile = dirname + @"progress.txt";
                    ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(perClusterTimeout, verbose, numThreads, recordProgress: true,
                                                        logPath: Path.Combine(logDir, progressFile), focusBstBd: false);
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                        h: heuristicName, numPaths: numPaths, demandPinningThreshold: demandPinningThreshold,
                                                        InnerEncoding: innerEncoding, scaleFactor: downScaleFactor);
                    var (heuristicDirectEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                        h: heuristicName, numPaths: numPaths, demandPinningThreshold: demandPinningThreshold,
                                                        InnerEncoding: innerEncoding, scaleFactor: downScaleFactor, DirectEncoder: true);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                            demandUB, innerEncoding, demandList, enableClustering, clusterVersion, clusters, -1, -1, -1, false, true, density, largeDemandLB,
                            maxLargeDistance, maxSmallDistance, metaOptRndInit, heuristicDirectEncoder);
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    Utils.writeDemandsToFile(Path.Combine(logDir, demandFile), result.Item1.Demands);
                    Utils.AppendToFile(logDir, gapFile,
                                    String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", topoName, heuristicName, numPaths,
                                                    demandPinningRatio, demandUB, density, largeDemandLB, maxLargeDistance, maxSmallDistance,
                                                    numThreads, clusterDir, clusterMethod, numClusters, perClusterTimeout, gap));
                    Console.WriteLine("==== PrimalDual --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                }
            }
        }

        /// <summary>
        /// tunning hill climber and simulated annealing parameters.
        /// </summary>
        public static void BlackBoxParameterTunning()
        {
            // topo parameters
            string topoName = "Cogentco";
            // string topoName = "b4";
            string topoPath = "";
            string clusterDir = null;
            string pathFile = null;
            int numClusters = 1;
            double downScaleFactor = 1;
            bool enableClustering = false;
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                downScaleFactor = 0.001;
            }
            else if (topoName == "b4")
            {
                topoPath = @"../Topologies/b4-teavar.json";
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                    numClusters, clusterDir, false);
            var avgLinkCap = topology.AverageCapacity();
            int numPaths = 4;
            // heuristic parameters
            var heuristicName = Heuristic.DemandPinning;
            var demandPinningRatio = 0.01;
            var demandUBRatio = 0.5;
            // parameters for grid search
            bool tuneSA = true;
            bool tuneHC = true;
            // common variables
            var stdDevRatioList = new List<double> { 0.001, 0.005, 0.01, 0.05 };
            var numNeighborsList = new List<int> { 10, 50, 100 };
            bool baselineGrey = false;
            // SA variables
            var initTmpList = new List<double> { 50 * downScaleFactor, 500 * downScaleFactor, 5000 * downScaleFactor };
            var tmpDecreaseFactorList = new List<double> { 0.1, 0.5, 0.9 };
            var resetNoImprovementRatioList = new List<double> { 0.5, 1, 2 };
            int numTmpSteps = 10000000;
            // HC variables
            int numDemands = 1000000;
            // start the search
            double demandPinningThreshold = demandPinningRatio * avgLinkCap;
            double demandUB = demandUBRatio * avgLinkCap;
            double timeout = 3600;
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            int seed = 0;
            var solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(timeout, 0, numThreads, recordProgress: false);
            // log dir
            string fid = Utils.GetFID();
            string gapFile = @"gap.txt";
            if (tuneHC)
            {
                string hillClimberLogDir = @"../logs/parameter_tuning/hillClimber/" + topoName + "_" + heuristicName
                        + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
                hillClimberLogDir = hillClimberLogDir + fid + @"/";
                Utils.CreateFile(hillClimberLogDir, gapFile, removeIfExist: false);
                foreach (var numNeighbors in numNeighborsList)
                {
                    foreach (var stdDevRatio in stdDevRatioList)
                    {
                        double stddev = stdDevRatio * avgLinkCap;
                        string hillClimbingFile = "hc_" + numNeighbors + "_" + stdDevRatio + ".txt";
                        solver.CleanAll();
                        var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths,
                                                        demandPinningThreshold: demandPinningThreshold, DirectEncoder: true);
                        var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                        var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                        var result = adversarialInputGenerator.HillClimbingAdversarialGenerator(optimalEncoder, heuristicEncoder, numTrials: numDemands,
                            numNeighbors: numNeighbors, demandUB: demandUB, stddev: stddev, grey: baselineGrey, seed: seed, storeProgress: true,
                            logPath: Path.Combine(hillClimberLogDir, hillClimbingFile), timeout: timeout, verbose: true);
                        var optimal = result.Item1.MaxObjective;
                        var heuristic = result.Item2.MaxObjective;
                        var gap = optimal - heuristic;
                        Console.WriteLine("==== HillClimber --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                        Utils.AppendToFile(hillClimberLogDir, gapFile,
                            String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}", topoName, downScaleFactor, numPaths, heuristicName,
                                demandPinningRatio, seed, demandUBRatio, numNeighbors, stdDevRatio, optimal, heuristic, gap));
                    }
                }
            }

            if (tuneSA)
            {
                string simulatedAnnealingLogDir = @"../logs/parameter_tuning/SA/" + topoName + "_" + heuristicName
                        + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
                simulatedAnnealingLogDir = simulatedAnnealingLogDir + fid + @"/";
                Utils.CreateFile(simulatedAnnealingLogDir, gapFile, removeIfExist: false);
                foreach (var numNeighbors in numNeighborsList)
                {
                    foreach (var stdDevRatio in stdDevRatioList)
                    {
                        foreach (var initialTmp in initTmpList)
                        {
                            foreach (var tmpDecreaseFactor in tmpDecreaseFactorList)
                            {
                                foreach (var resetNoImprovementRatio in resetNoImprovementRatioList)
                                {
                                    int resetNoImprovement = Convert.ToInt32(Math.Ceiling(resetNoImprovementRatio * numNeighbors));
                                    double stddev = stdDevRatio * avgLinkCap;
                                    string simulatedAnnealingFile = "sa_" + numNeighbors + "_" + stdDevRatio + "_" + initialTmp + "_" + tmpDecreaseFactor +
                                        "_" + resetNoImprovementRatio + ".txt";
                                    solver.CleanAll();
                                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths,
                                                                    demandPinningThreshold: demandPinningThreshold, DirectEncoder: true);
                                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                                    var result = adversarialInputGenerator.SimulatedAnnealing(optimalEncoder, heuristicEncoder, numTmpSteps,
                                        numNeighbors, demandUB, stddev, initialTmp: initialTmp, tmpDecreaseFactor: tmpDecreaseFactor,
                                        numNoIncreaseToReset: resetNoImprovement, grey: baselineGrey, seed: seed, storeProgress: true,
                                        logPath: Path.Combine(simulatedAnnealingLogDir, simulatedAnnealingFile), timeout: timeout, verbose: true);
                                    var optimal = result.Item1.MaxObjective;
                                    var heuristic = result.Item2.MaxObjective;
                                    var gap = optimal - heuristic;
                                    Console.WriteLine("==== SA --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
                                    Utils.AppendToFile(simulatedAnnealingLogDir, gapFile,
                                        String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", topoName, downScaleFactor,
                                            numPaths, heuristicName, demandPinningRatio, seed, demandUBRatio, numNeighbors, stdDevRatio,
                                            initialTmp, tmpDecreaseFactor, resetNoImprovementRatio, optimal, heuristic, gap));
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Modified DP.
        /// </summary>
        public static void AnalyzeModifiedDP()
        {
            // topo parameters
            string topoName = "Cogentco";
            // string topoName = "b4";
            bool metaOptRndInit = false;
            string topoPath = "";
            string clusterDir = null;
            string pathFile = null;
            int numClusters = 1;
            double downScaleFactor = 1;
            bool enableClustering = false;
            int clusterVersion = -1;
            double perClusterTimeout = 0;
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                clusterDir = @"../Topologies/partition_log/Cogentco_10_fm_partitioning/";
                pathFile = @"../Topologies/outputs/paths/Cogentco_sp.json";
                numClusters = 10;
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterTimeout = 1200;
            }
            else if (topoName == "b4")
            {
                topoPath = @"../Topologies/b4-teavar.json";
                perClusterTimeout = 5000;
                downScaleFactor = 1;
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            int numPaths = 4;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            // hueristic parameters
            var heuristicName = Heuristic.ModifiedDp;
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            var demandUBRatio = 0.5;
            var demandPinningRatio = 0.001;
            int verbose = 1;
            List<int> maxDistanceList = new List<int>();
            if (topoName == "Cogentco")
            {
                maxDistanceList = new List<int>() {
                    // 4,
                    // 6,
                    // 8,
                    32,
                    // 8,
                    // 16,
                };
            }
            else if (topoName == "b4")
            {
                maxDistanceList = new List<int>() { 2, 3, 4, 5 };
            }
            else {
                throw new Exception("no valid topo");
            }
            // realistic parameters
            double density = 1.0;
            int maxLargeDistance = -1;
            int maxSmallDistance = -1;
            double largeDemandLB = -1;
            string fid = Utils.GetFID();
            // topo
            var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                    numClusters, clusterDir, false);
            var avgLinkCap = Math.Round(topology.AverageCapacity(), 4);
            var demandPinningThreshold = Math.Round(demandPinningRatio * avgLinkCap, 4);
            var demandUB = demandUBRatio * avgLinkCap;
            Console.WriteLine(
                String.Format("======== avg link cap {0}, demand UB {1}, demandThresh {2}", avgLinkCap, demandUB, demandPinningThreshold));

            var demandSet = new HashSet<double>();
            demandSet.Add(0);
            demandSet.Add(demandPinningThreshold);
            demandSet.Add(demandUB);
            var demandList = new GenericList(demandSet);
            string logDir = @"../logs/modified_dp/" + topoName + "_" + heuristicName
                    + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_";
            logDir = logDir + fid + @"/";
            string gapFile = @"gap.txt";
            Utils.CreateFile(logDir, gapFile, removeIfExist: false);

            foreach (var maxDistance in maxDistanceList)
            {
                Console.WriteLine(
                    String.Format("========== max distance {0}, num clusters {1}, cluster dir {2}", maxDistance, numClusters, clusterDir));
                Console.WriteLine(String.Format("======== per cluster timeout: {0}", perClusterTimeout));
                // Primal-Dual
                string dirname = "modified_dp_" + maxDistance + "_" + numClusters + "_" + perClusterTimeout + "/";
                string demandFile = dirname + @"demands.txt";
                string progressFile = dirname + @"progress.txt";
                ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(perClusterTimeout, verbose, numThreads, recordProgress: true,
                                                    logPath: Path.Combine(logDir, progressFile), focusBstBd: false);
                var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, demandPinningThreshold: demandPinningThreshold,
                                                    InnerEncoding: innerEncoding, scaleFactor: downScaleFactor, maxShortestPathLen: maxDistance);
                var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                        demandUB, innerEncoding, demandList, enableClustering, clusterVersion, clusters, -1, -1, -1, false, false, density, largeDemandLB,
                        maxLargeDistance, maxSmallDistance, metaOptRndInit, null);
                double optimal = result.Item1.MaxObjective;
                double heuristic = result.Item2.MaxObjective;
                var gap = optimal - heuristic;
                Utils.writeDemandsToFile(Path.Combine(logDir, demandFile), result.Item1.Demands);
                Utils.AppendToFile(logDir, gapFile,
                                String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", topoName, heuristicName, numPaths,
                                                demandPinningRatio, demandUB, density, largeDemandLB, maxLargeDistance, maxSmallDistance,
                                                numThreads, clusterDir, numClusters, perClusterTimeout, maxDistance, gap));
                Console.WriteLine("==== PrimalDual --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            }
        }

        /// <summary>
        /// Modified DP.
        /// </summary>
        public static void AnalyzeParallelHeuristics()
        {
            // topo parameters
            string topoName = "Cogentco";
            // string topoName = "b4";
            bool metaOptRndInit = false;
            string topoPath = "";
            string clusterDir = null;
            string pathFile = null;
            int numClusters = 1;
            double downScaleFactor = 1;
            bool enableClustering = false;
            int clusterVersion = -1;
            double perClusterPerHeuristicTimeout = 0;
            if (topoName == "Cogentco")
            {
                topoPath = @"../Topologies/Cogentco.json";
                clusterDir = @"../Topologies/partition_log/Cogentco_10_fm_partitioning/";
                pathFile = @"../Topologies/outputs/paths/Cogentco_sp.json";
                numClusters = 10;
                downScaleFactor = 0.001;
                enableClustering = true;
                clusterVersion = 2;
                perClusterPerHeuristicTimeout = 1200;
            }
            else if (topoName == "b4")
            {
                topoPath = @"../Topologies/b4-teavar.json";
                perClusterPerHeuristicTimeout = 5000;
            }
            else
            {
                throw new Exception("no valid topo name");
            }
            int numPaths = 4;
            // solver parameters
            int numThreads = MachineStat.numThreads;
            int numProcessors = MachineStat.numProcessors;
            var heuristicNameList = new List<(Heuristic, int)>() {
                // (Heuristic.PopDp, 2),
                (Heuristic.ExpectedPopDp, 3),
                // (Heuristic.ParallelPop, 2),
                // (Heuristic.ParallelPopDp, 2),
            };
            var innerEncoding = InnerRewriteMethodChoice.PrimalDual;
            var demandUBRatio = 0.5;
            var demandPinningRatio = 0.05;
            int verbose = 1;
            int numSlices = 2;
            int numSamples = 5;
            string fid = Utils.GetFID();
            // topo
            var (topology, clusters) = CliUtils.getTopology(topoPath, pathFile, downScaleFactor, enableClustering,
                                                    numClusters, clusterDir, false);
            var avgLinkCap = topology.AverageCapacity();
            // realistic parameters
            double density = 1.0;
            int maxLargeDistance = 4;
            int maxSmallDistance = -1;
            double largeDemandLB = 0.25 * avgLinkCap;
            // hue params
            var demandPinningThreshold = demandPinningRatio * avgLinkCap;
            var demandUB = demandUBRatio * avgLinkCap;
            var partition = topology.RandomPartition(numSlices);
            var partitionsList = new List<IDictionary<(string, string), int>>();
            for (int i = 0; i < numSamples; i++)
            {
                partitionsList.Add(topology.RandomPartition(numSlices));
            }
            Console.WriteLine(
                String.Format("======== avg link cap {0}, demand UB {1}, demandThresh {2}", avgLinkCap, demandUB, demandPinningThreshold));

            string logDir = @"../logs/parallel_hue/" + topoName
                    + "_" + demandUB + "_" + demandPinningThreshold + "_" + numPaths + "_" + numSamples + "_" + numSlices;
            logDir = logDir + fid + @"/";
            string gapFile = @"gap.txt";
            Utils.CreateFile(logDir, gapFile, removeIfExist: false);

            foreach (var (heuristicName, timeoutMultiplier) in heuristicNameList)
            {
                var perClusterTimeout = perClusterPerHeuristicTimeout * timeoutMultiplier;
                Console.WriteLine(
                    String.Format("========== heuristic name {0}, num clusters {1}, cluster dir {2}", heuristicName, numClusters, clusterDir));
                Console.WriteLine(String.Format("======== per cluster timeout: {0}", perClusterTimeout));
                // Primal-Dual
                string dirname = "heuristic_" + heuristicName + "_" + numClusters + "_" + perClusterTimeout + "/";
                string demandFile = dirname + @"demands.txt";
                string progressFile = dirname + @"progress.txt";

                var demandSet = new HashSet<double>();
                demandSet.Add(0);
                if (heuristicName != Heuristic.ParallelPop)
                {
                    demandSet.Add(demandPinningThreshold);
                }
                demandSet.Add(demandUB);
                var demandList = new GenericList(demandSet);
                ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(perClusterTimeout, verbose, numThreads, recordProgress: true,
                                                    logPath: Path.Combine(logDir, progressFile), focusBstBd: false);
                var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology,
                                                    h: heuristicName, numPaths: numPaths, numSlices: numSlices, demandPinningThreshold: demandPinningThreshold,
                                                    partition: partition, numSamples: numSamples, partitionsList: partitionsList, InnerEncoding: innerEncoding,
                                                    scaleFactor: downScaleFactor);
                var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                (TEOptimizationSolution, TEOptimizationSolution) result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                        demandUB, innerEncoding, demandList, enableClustering, clusterVersion, clusters, -1, -1, -1, false, false, density, largeDemandLB,
                        maxLargeDistance, maxSmallDistance, metaOptRndInit, null);
                double optimal = result.Item1.MaxObjective;
                double heuristic = result.Item2.MaxObjective;
                var gap = optimal - heuristic;
                Utils.writeDemandsToFile(Path.Combine(logDir, demandFile), result.Item1.Demands);
                Utils.AppendToFile(logDir, gapFile,
                                String.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}", topoName, heuristicName, numPaths,
                                                demandPinningRatio, demandUB, density, largeDemandLB, maxLargeDistance, maxSmallDistance,
                                                numThreads, clusterDir, numClusters, perClusterTimeout, gap));
                Console.WriteLine("==== PrimalDual --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            }
        }
    }
}