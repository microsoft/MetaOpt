// <copyright file="Program.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System;
    using System.Diagnostics;
    using CommandLine;
    using Gurobi;
    using MetaOptimize;
    using ZenLib;

    /// <summary>
    /// Main entry point for the program.
    /// </summary>
    public class MainEntry
    {
        /// <summary>
        /// checks whether we get the solution we expect after running the solvers.
        /// </summary>
        /// <param name="args"></param>
        public static void TEExampleMain(string[] args)
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
            var solverG = new GurobiSOS();
            var optimalEncoderG = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solverG, maxNumPaths: 1);
            var popEncoderG = new PopEncoder<GRBVar, GRBModel>(solverG, maxNumPaths: 1, numPartitions: 2, demandPartitions: partition);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, maxNumPaths: 1);

            var (optimalSolutionG, popSolutionG) = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoderG, popEncoderG);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(popSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = optimalSolutionG.MaxObjective;
            var heuristic = popSolutionG.MaxObjective;
            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristic}");

            var demands = new Dictionary<(string, string), double>(optimalSolutionG.Demands);
            var optGSolver = new GurobiSOS();
            optimalEncoderG = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(optGSolver, maxNumPaths: 1);
            var popGSolver = new GurobiSOS();
            popEncoderG = new PopEncoder<GRBVar, GRBModel>(popGSolver, maxNumPaths: 1, numPartitions: 2, demandPartitions: partition);
            Utils.checkSolution(topology, popEncoderG, optimalEncoderG, heuristic, optimal, demands, "gurobiCheck");
        }

        /// <summary>
        /// Use this function to test our theorem for VBP.
        /// (see theorem 1 in our NSDI24 Paper).
        /// </summary>
        public static void vbpMain(string[] args)
        {
            // OPT = 2m + 3n
            // HUE = 4m + 6n
            // num jobs = 6m + 9n
            var solverG = new GurobiSOS(verbose: 0);

            for (int m = 1; m < 10; m++)
            {
                for (int n = 0; n < 2; n++)
                {
                    Console.WriteLine(String.Format("============ m = {0}, n = {1}", m, n));
                    var binSize = new List<double>();
                    binSize.Add(1.0001);
                    binSize.Add(1.0001);
                    var bins = new Bins(4 * m + 6 * n, binSize);
                    // TODO: need to change var name to be appropriate for the problem.
                    var demands = new Dictionary<int, List<double>>();
                    int nxt_key = 0;

                    // TODO: need a comment that describes what the constants are here. you also may benefit from changing the constants to have a name.
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.92, 0.0 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.91, 0.01 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.48, 0.2 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.68, 0 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.52, 0.12 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.32, 0.32 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.19, 0.45 };
                        nxt_key += 1;
                        demands[nxt_key] = new List<double>() { 0.42, 0.22 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.1, 0.54 };
                        nxt_key += 1;
                        demands[nxt_key] = new List<double>() { 0.1, 0.54 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.1, 0.53 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.06, 0.48 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.07, 0.47 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.01, 0.53 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < m; i++)
                    {
                        demands[nxt_key] = new List<double>() { 0.03, 0.51 };
                        nxt_key += 1;
                    }
                    for (int i = 0; i < demands.Count - 1; i++)
                    {
                        double total_sum_1 = 0;
                        double total_sum_2 = 0;
                        for (int j = 0; j < demands[0].Count; j++)
                        {
                            total_sum_1 += demands[i][j];
                            total_sum_2 += demands[i + 1][j];
                        }
                        Debug.Assert(total_sum_1 >= total_sum_2 - 0.00001);
                    }
                    solverG.CleanAll();
                    var optimalEncoder = new VBPOptimalEncoder<GRBVar, GRBModel>(solverG, demands.Count, demands[0].Count);
                    var optimalEncoding = optimalEncoder.Encoding(bins, inputEqualityConstraints: demands, verbose: false);
                    var solverSolutionOptimal = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
                    var optimizationSolutionOptimal = (VBPOptimizationSolution)optimalEncoder.GetSolution(solverSolutionOptimal);
                    Console.WriteLine(
                        String.Format("===== OPT {0}", optimizationSolutionOptimal.TotalNumBinsUsed));

                    solverG.CleanAll();
                    var ffdEncoder = new FFDItemCentricEncoder<GRBVar, GRBModel>(solverG, demands.Count, demands[0].Count);
                    var ffdEncoding = ffdEncoder.Encoding(bins, inputEqualityConstraints: demands, verbose: false);
                    var solverSolutionFFD = optimalEncoder.Solver.Maximize(ffdEncoding.MaximizationObjective);
                    var solutionFFD = (VBPOptimizationSolution)ffdEncoder.GetSolution(solverSolutionFFD);
                    Console.WriteLine(
                        String.Format("===== HUE {0}", solutionFFD.TotalNumBinsUsed));
                    Debug.Assert(optimizationSolutionOptimal.TotalNumBinsUsed * 2 == solutionFFD.TotalNumBinsUsed);
                }
            }
        }

        /// <summary>
        /// test MetaOpt on VBP.
        /// </summary>
        /// TODO: specify how this function is different from the previous.
        public static void Main(string[] args)
        {
            var binSize = new List<double>();
            binSize.Add(1.00001);
            binSize.Add(1.00001);
            var bins = new Bins(6, binSize);
            var numDemands = 9;
            var numDimensions = 2;
            var optimalBins = 3;
            var ffdMethod = FFDMethodChoice.FFDSum;
            List<IList<double>> demandList = null;
            double perIterationTimeout = 1000;
            // double perIterationTimeout = double.PositiveInfinity;
            var solverG = new GurobiSOS(timeout: perIterationTimeout, verbose: 1);
            var optimalEncoder = new VBPOptimalEncoder<GRBVar, GRBModel>(solverG, numDemands, numDimensions, BreakSymmetry: false);
            var ffdEncoder = new FFDItemCentricEncoder<GRBVar, GRBModel>(solverG, numDemands, numDimensions);
            var adversarialGenerator = new VBPAdversarialInputGenerator<GRBVar, GRBModel>(bins, numDemands, numDimensions);
            var (optimalSolutionG, ffdSolutionG) = adversarialGenerator.MaximizeOptimalityGapFFD(optimalEncoder, ffdEncoder,
                                                            optimalBins, ffdMethod: ffdMethod, itemList: demandList, verbose: true);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(ffdSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Optimal number of bins: " + optimalSolutionG.TotalNumBinsUsed);
            Console.WriteLine("FFD number of bins: " + ffdSolutionG.TotalNumBinsUsed);
        }

        /// <summary>
        /// test case for SP-PIFO.
        /// </summary>
        public static void PIFOTestMain(string[] args)
        {
            int maxRank = 8;
            int numPackets = 15;
            int numQueues = 4;
            int splitQueue = 2;
            int splitRank = 5;
            var solverG = new GurobiSOS(verbose: 0);

            var packetRankEqualityConstraint = new Dictionary<int, int>();
            packetRankEqualityConstraint[0] = 7;
            packetRankEqualityConstraint[1] = 2;
            packetRankEqualityConstraint[2] = 1;
            packetRankEqualityConstraint[3] = 0;
            packetRankEqualityConstraint[4] = 7;
            packetRankEqualityConstraint[5] = 7;
            packetRankEqualityConstraint[6] = 2;
            packetRankEqualityConstraint[7] = 1;
            packetRankEqualityConstraint[8] = 0;
            packetRankEqualityConstraint[9] = 2;
            packetRankEqualityConstraint[10] = 1;
            packetRankEqualityConstraint[11] = 0;
            packetRankEqualityConstraint[12] = 2;
            packetRankEqualityConstraint[13] = 1;
            packetRankEqualityConstraint[14] = 0;
            solverG.CleanAll();
            var optimalEncoder = new PIFOAvgDelayOptimalEncoder<GRBVar, GRBModel>(solverG, numPackets, maxRank);
            var optimalEncoding = optimalEncoder.Encoding(rankEqualityConstraints: packetRankEqualityConstraint);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
            var optimizationSolutionOptimal = (PIFOOptimizationSolution)optimalEncoder.GetSolution(solverSolutionOptimal);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimizationSolutionOptimal, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("===== OPT {0}", optimizationSolutionOptimal.Cost);

            solverG.CleanAll();
            var heuristicEncoder = new ModifiedSPPIFOAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, splitQueue, numQueues, splitRank, maxRank);
            var heuristicEncoding = heuristicEncoder.Encoding(rankEqualityConstraints: packetRankEqualityConstraint);
            var solverSolutionHeuristic = optimalEncoder.Solver.Maximize(heuristicEncoding.MaximizationObjective);
            var solutionHeuristic = (PIFOOptimizationSolution)heuristicEncoder.GetSolution(solverSolutionHeuristic);
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(solutionHeuristic, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("===== HUE {0}", solutionHeuristic.Cost);
        }

        /// <summary>
        /// test MetaOpt on PIFO.
        /// </summary>
        public static void PIFOMain(string[] args)
        {
            int maxRank = 8;
            int numPackets = 18;
            int numQueues = 4;
            // int splitQueue = 2;
            // int splitRank = 4;
            int maxQueueSize = 12;
            int windowSize = 12;
            double burstParam = 0.1;

            var solverG = new GurobiSOS(verbose: 1, timeout: 1000);
            // var optimalEncoder = new PIFOAvgDelayOptimalEncoder<GRBVar, GRBModel>(solverG, numPackets, maxRank);
            // var heuristicEncoder = new SPPIFOAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, numQueues, maxRank);
            // var optimalEncoder = new SPPIFOAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, numQueues, maxRank);
            // var heuristicEncoder = new ModifiedSPPIFOAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, splitQueue, numQueues,
            //     splitRank, maxRank);
            // var optimalEncoder = new PIFOWithDropAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, maxRank, maxQueueSize);
            var H1 = new SPPIFOWithDropAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, numQueues, maxRank, maxQueueSize);
            var H2 = new AIFOAvgDelayEncoder<GRBVar, GRBModel>(solverG, numPackets, maxRank, maxQueueSize, windowSize, burstParam);

            var adversarialGenerator = new PIFOAdversarialInputGenerator<GRBVar, GRBModel>(numPackets, maxRank);
            var (optimalSolutionG, heuristicSolutionG) = adversarialGenerator.MaximizeOptimalityGap(H1,
                H2, verbose: true);
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(heuristicSolutionG, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Optimal cost: " + optimalSolutionG.Cost);
            Console.WriteLine("Heuristic cost: " + heuristicSolutionG.Cost);

            var orderToRankOpt = new Dictionary<int, double>();
            var orderToRankHeu = new Dictionary<int, double>();
            for (int pid = 0; pid < numPackets; pid++) {
                if (optimalSolutionG.Admit[pid] == 1) {
                    orderToRankOpt[optimalSolutionG.Order[pid]] = optimalSolutionG.Ranks[pid];
                }
                if (heuristicSolutionG.Admit[pid] == 1) {
                    orderToRankHeu[heuristicSolutionG.Order[pid]] = heuristicSolutionG.Ranks[pid];
                }
            }

            int numInvOpt = 0;
            int numInvHeu = 0;
            for (int pid = 0; pid < numPackets; pid++)
            {
                numInvOpt += ComputeInversionNum(optimalSolutionG, orderToRankOpt, pid);
                numInvHeu += ComputeInversionNum(heuristicSolutionG, orderToRankHeu, pid);
            }
            Console.WriteLine("number of inversions in OPT: " + numInvOpt);
            Console.WriteLine("number of inversions in HEU: " + numInvHeu);
        }

        private static int ComputeInversionNum(PIFOOptimizationSolution optimalSolutionG, Dictionary<int, double> orderToRankOpt, int pid)
        {
            int numInvOpt = 0;
            if (optimalSolutionG.Admit[pid] >= 0.98)
            {
                int currOrder = optimalSolutionG.Order[pid];
                for (int prev = 0; prev < currOrder; prev++)
                {
                    if (orderToRankOpt[prev] > optimalSolutionG.Ranks[pid])
                    {
                        numInvOpt += 1;
                    }
                }
            }
            else
            {
                foreach (var (order, rank) in orderToRankOpt)
                {
                    if (rank > optimalSolutionG.Ranks[pid])
                    {
                        numInvOpt += 1;
                    }
                }
            }

            return numInvOpt;
        }

        /// <summary>
        /// Experiments for NSDI.
        /// </summary>
        public static void NSDIMain(string[] args)
        {
            // NSDIExp.compareGapDelayDiffMethodsDP();
            // NSDIExp.compareLargeScaleGapDelayDiffMethodsDP();
            // NSDIExp.compareGapDelayDiffMethodsPop();
            // NSDIExp.AblationStudyClusteringOnDP();
            // NSDIExp.BlackBoxParameterTunning();
            NSDIExp.AddRealisticConstraintsDP();
            // NSDIExp.gapThresholdDemandPinningForDifferentTopologies();
            // NSDIExp.ImpactNumPathsPartitionsExpectedPop();
            // NSDIExp.AblationStudyClusteringOnDP();
            // NSDIExp.BlackBoxParameterTunning();
            // NSDIExp.AnalyzeModifiedDP();
            // NSDIExp.ImpactNumNodesRadixSmallWordTopoDemandPinning();
            // NSDIExp.ImpactNumSamplesExpectedPop();
            // NSDIExp.AnalyzeParallelHeuristics();
        }

        /// <summary>
        /// Experiments for hotnets.
        /// </summary>
        public static void hotnetsMain(string[] args)
        {
            // var topology = Topology.RandomRegularGraph(8, 7, 1, seed: 0);
            // var topology = Topology.SmallWordGraph(5, 4, 1);
            // foreach (var edge in topology.GetAllEdges()) {
            //     Console.WriteLine(edge.Source + "_" + edge.Target);
            // }
            // foreach (var pair in topology.GetNodePairs()) {
            //     if (!topology.ContaintsEdge(pair.Item1, pair.Item2, 1)) {
            //         Console.WriteLine("missing link " + pair.Item1 + " " + pair.Item2);
            //     }
            // }
            // Experiment.printPaths();
            // HotNetsExperiment.impactOfDPThresholdOnGap();
            // Experiment.ImpactNumPathsDemandPinning();
            // Experiment.ImpactNumNodesRadixRandomRegularGraphDemandPinning();
            HotNetsExperiment.impactSmallWordGraphParamsDP();
            // Experiment.ImpactNumPathsPartitionsPop();
            // Experiment.compareGapDelayDiffMethodsPop();
            // Experiment.compareGapDelayDiffMethodsDP();
            // Experiment.compareTopoSizeLatency();
        }

        /// <summary>
        /// Main entry point for the program.
        /// The function takes the command line arguments and stores them in a
        /// static instance property of the CliOptions class.
        /// It then reads the topology and clusters from the files specified in the
        /// command line arguments and then proceeds to find the optimality gap.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public static void ssMain(string[] args)
        {
            // read the command line arguments.
            var opts = CommandLine.Parser.Default.ParseArguments<CliOptions>(args).MapResult(o => o, e => null);
            CliOptions.Instance = opts;

            if (opts == null)
            {
                Environment.Exit(0);
            }

            // read the topology and clusters.
            var (topology, clusters) = CliUtils.getTopology(opts.TopologyFile, opts.PathFile, opts.DownScaleFactor, opts.EnableClustering,
                                            opts.NumClusters, opts.ClusterDir, opts.Verbose);

            getSolverAndRunNetwork(topology, clusters);
        }

        // TODO: this function is missing proper commenting
        private static void getSolverAndRunNetwork(Topology topology, List<Topology> clusters)
        {
            var opts = CliOptions.Instance;
            // use the Z3 solver via the Zen wrapper library.
            switch (opts.SolverChoice)
            {
                case SolverChoice.Zen:
                    // run the zen optimizer.
                    RunNetwork(new SolverZen(), topology, clusters);
                    break;
                case SolverChoice.Gurobi:
                    var storeProgress = opts.StoreProgress & (opts.Method == MethodChoice.Direct);
                    if (opts.Heuristic == Heuristic.DemandPinning)
                    {
                        RunNetwork(new GurobiSOS(opts.Timeout, Convert.ToInt32(opts.Verbose),
                                                    timeToTerminateNoImprovement: opts.TimeToTerminateIfNoImprovement,
                                                    numThreads: opts.NumGurobiThreads,
                                                    recordProgress: storeProgress,
                                                    logPath: opts.LogFile),
                                topology, clusters);
                    }
                    else
                    {
                        RunNetwork(new GurobiSOS(opts.Timeout, Convert.ToInt32(opts.Verbose),
                                                    timeToTerminateNoImprovement: opts.TimeToTerminateIfNoImprovement,
                                                    numThreads: opts.NumGurobiThreads,
                                                    recordProgress: storeProgress,
                                                    logPath: opts.LogFile),
                                topology, clusters);
                    }
                    break;
                default:
                    throw new Exception("Other solvers are currently invalid.");
            }
        }

        // TODO: this function is missing proper commenting
        private static void RunNetwork<TVar, TSolution>(ISolver<TVar, TSolution> solver,
                Topology topology, List<Topology> clusters)
        {
            var opts = CliOptions.Instance;

            // setup the optimal encoder and adversarial input generator.
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, opts.Paths);
            TEAdversarialInputGenerator<TVar, TSolution> adversarialInputGenerator;
            adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSolution>(topology, opts.Paths, opts.NumProcesses);

            // setup the heuristic encoder and partitions.
            var heuristicSolver = solver;
            var (heuristicEncoder, partitioning, partitionList) = CliUtils.getHeuristic<TVar, TSolution>(heuristicSolver, topology, opts.Heuristic, opts.Paths, opts.PopSlices,
                        opts.DemandPinningThreshold * opts.DownScaleFactor, numSamples: opts.NumRandom, partitionSensitivity: opts.PartitionSensitivity,
                        scaleFactor: opts.DownScaleFactor, InnerEncoding: opts.InnerEncoding, maxShortestPathLen: opts.MaxShortestPathLen);

            // find an adversarial example and show the time taken.
            var demandList = new GenericDemandList((opts.DemandList.Split(",")).Select(x => double.Parse(x) * opts.DownScaleFactor).ToHashSet());
            Utils.logger(
                string.Format("Demand List:{0}", Newtonsoft.Json.JsonConvert.SerializeObject(demandList.demandList, Newtonsoft.Json.Formatting.Indented)),
                opts.Verbose);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Utils.logger("Starting setup", opts.Verbose);
            (TEOptimizationSolution, TEOptimizationSolution) result;
            switch (opts.Method)
            {
                case MethodChoice.Direct:
                    result = CliUtils.getMetaOptResult(adversarialInputGenerator, optimalEncoder, heuristicEncoder, opts.DemandUB, opts.InnerEncoding,
                                        demandList, opts.EnableClustering, opts.ClusterVersion, clusters, opts.NumInterClusterSamples, opts.NumNodesPerCluster,
                                        opts.NumInterClusterQuantizations, opts.Simplify, opts.Verbose, opts.MaxDensity, opts.LargeDemandLB, opts.maxLargeDistance,
                                        opts.maxSmallDistance, false, null);
                    break;
                case MethodChoice.Search:
                    Utils.logger("Going to use search to find a desirable gap", opts.Verbose);
                    result = adversarialInputGenerator.FindMaximumGapInterval(optimalEncoder, heuristicEncoder, opts.Confidencelvl, opts.StartingGap, opts.DemandUB,
                            demandList: demandList);
                    break;
                case MethodChoice.FindFeas:
                    Utils.logger("Going to find one feasible solution with the specified gap", opts.Verbose);
                    result = adversarialInputGenerator.FindOptimalityGapAtLeast(optimalEncoder, heuristicEncoder, opts.StartingGap, opts.DemandUB,
                            demandList: demandList, simplify: opts.Simplify);
                    break;
                case MethodChoice.Random:
                    Utils.logger("Going to do random search to find some advers inputs", opts.Verbose);
                    result = adversarialInputGenerator.RandomAdversarialGenerator(optimalEncoder, heuristicEncoder, opts.NumRandom, opts.DemandUB, seed: opts.Seed,
                        verbose: opts.Verbose, storeProgress: opts.StoreProgress, logPath: opts.LogFile, timeout: opts.Timeout);
                    break;
                case MethodChoice.HillClimber:
                    Utils.logger("Going to use HillClimber to find some advers inputs", opts.Verbose);
                    result = adversarialInputGenerator.HillClimbingAdversarialGenerator(optimalEncoder, heuristicEncoder, opts.NumRandom,
                        opts.NumNeighbors, opts.DemandUB, opts.StdDev, seed: opts.Seed, verbose: opts.Verbose, storeProgress: opts.StoreProgress,
                        logPath: opts.LogFile, timeout: opts.Timeout);
                    break;
                case MethodChoice.SimulatedAnnealing:
                    Utils.logger("Going to use Simulated Annealing to find some advers inputs", opts.Verbose);
                    Utils.logger(opts.LogFile, opts.Verbose);
                    result = adversarialInputGenerator.SimulatedAnnealing(optimalEncoder, heuristicEncoder, opts.NumRandom, opts.NumNeighbors,
                        opts.DemandUB, opts.StdDev, opts.InitTmp, opts.TmpDecreaseFactor, seed: opts.Seed, verbose: opts.Verbose, storeProgress: opts.StoreProgress,
                        logPath: opts.LogFile, timeout: opts.Timeout);
                    break;
                default:
                    throw new Exception("Wrong Method, please choose between available methods!!");
            }

            if (opts.FullOpt)
            {
                if (!opts.EnableClustering)
                {
                    throw new Exception("does not need to be enable for non-clustering method");
                }
                if (opts.InnerEncoding != InnerRewriteMethodChoice.PrimalDual)
                {
                    throw new Exception("inner encoding should be primal dual");
                }
                optimalEncoder.Solver.CleanAll(timeout: opts.FullOptTimer);
                var currDemands = new Dictionary<(string, string), double>(result.Item1.Demands);
                Utils.setEmptyPairsToZero(topology, currDemands);
                result = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, opts.DemandUB, innerEncoding: opts.InnerEncoding,
                        demandList: demandList, simplify: opts.Simplify, verbose: opts.Verbose, demandInits: currDemands);
                optimalEncoder.Solver.CleanAll(focusBstBd: false, timeout: opts.Timeout);
            }

            if (opts.UBFocus)
            {
                var currDemands = new Dictionary<(string, string), double>(result.Item1.Demands);
                optimalEncoder.Solver.CleanAll(focusBstBd: true, timeout: opts.UBFocusTimer);
                Utils.setEmptyPairsToZero(topology, currDemands);
                result = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, opts.DemandUB, innerEncoding: opts.InnerEncoding,
                        demandList: demandList, simplify: opts.Simplify, verbose: opts.Verbose, demandInits: currDemands);
                optimalEncoder.Solver.CleanAll(focusBstBd: false, timeout: opts.Timeout);
            }
            var optimal = result.Item1.MaxObjective;
            var heuristic = result.Item2.MaxObjective;
            var demands = new Dictionary<(string, string), double>(result.Item1.Demands);
            Utils.setEmptyPairsToZero(topology, demands);
            Console.WriteLine("##############################################");
            Console.WriteLine("##############################################");
            Console.WriteLine("##############################################");
            Console.WriteLine($"optimal={optimal}, heuristic={heuristic}, time={timer.ElapsedMilliseconds}ms");
            if (opts.Heuristic == Heuristic.ExpectedPop)
            {
                CliUtils.findGapExpectedPopAdversarialDemandOnIndependentPartitions<GRBVar, GRBModel>(opts, topology, demands, optimal);
            }
            Console.WriteLine("##############################################");
            Console.WriteLine("##############################################");
            Console.WriteLine("##############################################");
            var optGSolver = new GurobiBinary();
            var optimalEncoderG = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(optGSolver, maxNumPaths: opts.Paths);
            var optZSolver = new SolverZen();
            var optimalEncoderZen = new TEMaxFlowOptimalEncoder<Zen<Real>, ZenSolution>(optZSolver, maxNumPaths: opts.Paths);

            var gSolver = new GurobiBinary();
            var zSolver = new SolverZen();
            IEncoder<GRBVar, GRBModel> heuristicEncoderG;
            IEncoder<Zen<Real>, ZenSolution> heuristicEncoderZ;
            switch (opts.Heuristic)
            {
                case Heuristic.Pop:
                    Console.WriteLine("Starting exploring pop heuristic");
                    heuristicEncoderG = new PopEncoder<GRBVar, GRBModel>(gSolver, maxNumPaths: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: partitioning);
                    heuristicEncoderZ = new PopEncoder<Zen<Real>, ZenSolution>(zSolver, maxNumPaths: opts.Paths, numPartitions: opts.PopSlices, demandPartitions: partitioning);
                    break;
                case Heuristic.DemandPinning:
                    Console.WriteLine("Starting exploring demand pinning heuristic");
                    heuristicEncoderG = new DirectDemandPinningEncoder<GRBVar, GRBModel>(gSolver, k: opts.Paths, threshold: opts.DemandPinningThreshold * opts.DownScaleFactor);
                    heuristicEncoderZ = new DirectDemandPinningEncoder<Zen<Real>, ZenSolution>(zSolver, k: opts.Paths, threshold: opts.DemandPinningThreshold * opts.DownScaleFactor);
                    break;
                case Heuristic.ExpectedPop:
                    Console.WriteLine("Starting to explore expected pop heuristic");
                    heuristicEncoderG = new ExpectedPopEncoder<GRBVar, GRBModel>(gSolver, k: opts.Paths, numSamples: opts.NumRandom,
                        numPartitionsPerSample: opts.PopSlices, demandPartitionsList: partitionList);
                    heuristicEncoderZ = new ExpectedPopEncoder<Zen<Real>, ZenSolution>(zSolver, k: opts.Paths, numSamples: opts.NumRandom,
                        numPartitionsPerSample: opts.PopSlices, demandPartitionsList: partitionList);
                    break;
                case Heuristic.PopDp:
                    throw new Exception("Not Implemented Yet.");
                default:
                    throw new Exception("No heuristic selected.");
            }
            Utils.checkSolution(topology, heuristicEncoderG, optimalEncoderG, heuristic, optimal, demands, "gurobiCheck");
        }
    }
}