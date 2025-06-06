// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using Gurobi;
    using MetaOptimize.Cli;
    /// <summary>
    /// We use the functions in this class for our hotnets experiments.
    /// </summary>
    /// TODO: list the series of experiments that we run and what function does what.
    public static class HotNetsExperiment
    {
        /// <summary>
        /// This function looks at the impact of one particular parameter
        /// for the demand pinning heuristic: the threshold.
        /// The demand pinning heuristic uses the threshold to decide whether to directly
        /// send a demand on its shortest path or not---demands below the threshold go on the shortest path.
        /// This function sweeps through different values of the threshold on three different topologies.
        /// B4, SWAN and Abilene.
        /// </summary>
        /// TODO: rewrite to take the topology as an argument.
        public static void impactOfDPThresholdOnGap()
        {
            var topologies = new Dictionary<string, string>();
            topologies["B4"] = @"../Topologies/b4-teavar.json";
            // topologies["SWAN"] = @"../Topologies/swan.json";
            // topologies["Abilene"] = @"../Topologies/abilene.json";
            Heuristic heuristicName = Heuristic.DemandPinning;
            string logDir = @"../logs/demand_pinning_sweep_thresh/" + Utils.GetFID() + @"\";
            double timeToTerminate = 1800;
            int numPaths = 2;
            double start = 5;
            double step = 2.5;
            double end = 6;
            int numProcessors = 16;

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);

            // goes through topologies one by one and sweeps through the threshold.
            foreach (var (topoName, topoPath) in topologies)
            {
                var topology = Parser.ReadTopologyJson(topoPath);
                var maxThreshold = topology.MinCapacity();
                string logFile = topoName + @"_" + heuristicName + ".txt";
                // Utils.CreateFile(logDir, logFile, removeIfExist: true);
                // Utils.AppendToFile(logDir, logFile, maxThreshold.ToString());
                for (double i = start; i <= end; i += step) {
                    var threshold = i * maxThreshold / 100;
                    var (optimal, heuristic, demands) = CliUtils.maximizeOptimalityGapDemandPinning<GRBVar, GRBModel>(
                            solver: solver, topology: topology, numPaths: numPaths, threshold: threshold, numProcessors: numProcessors);
                    var gap = optimal - heuristic;
                    // Utils.AppendToFile(logDir, logFile, i + ", " + threshold + ", " + optimal + ", " + heuristic + ", " + gap);
                    Console.WriteLine("==== Gap --> " + " i=" + i + " threshold=" + threshold + " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                }
            }
        }
        /// <summary>
        /// print paths between every pairs of topology.
        /// </summary>
        // TODO: comment is unclear.
        // TODO: maybe re-write so that it takes the topology and its path as input?
        public static void printPaths()
        {
            var topologies = new Dictionary<string, string>();
            topologies["B4"] = @"..\Topologies\b4-teavar.json";
            topologies["SWAN"] = @"..\Topologies\swan.json";
            topologies["Abilene"] = @"..\Topologies\abilene.json";

            int numPaths = 1;
            Dictionary<string, List<int>> splist = new Dictionary<string, List<int>>();
            string logDir = @"..\logs\path_stat\" + Utils.GetFID() + @"\";
            foreach (var (topoName, topoPath) in topologies)
            {
                Console.WriteLine("================== " + topoName);
                string logFile = topoName + @".txt";
                Utils.CreateFile(logDir, logFile, removeIfExist: true);
                var topology = Parser.ReadTopologyJson(topoPath);
                splist[topoName] = new List<int>();
                foreach (var pair in topology.GetNodePairs())
                {
                    // Console.WriteLine("==== pair " + pair);
                    var paths = topology.ShortestKPaths(numPaths, pair.Item1, pair.Item2);
                    foreach (var simplePath in paths)
                    {
                        // Console.WriteLine(string.Join("_", simplePath));
                        splist[topoName].Add(simplePath.Count());
                    }
                }
                Console.WriteLine("===== path distribution for topo " + topoName);
                Console.WriteLine("dimaeter = " + topology.diameter());
                Utils.AppendToFile(logDir, logFile, "diameter=" + topology.diameter());
                Console.WriteLine("avg splt = " + topology.avgShortestPathLength());
                Utils.AppendToFile(logDir, logFile, "aspl=" + topology.avgShortestPathLength());
                splist[topoName].Sort();
                foreach (var plen in splist[topoName])
                {
                    Console.WriteLine(plen.ToString());
                    Utils.AppendToFile(logDir, logFile, plen.ToString());
                }
            }
        }
        /// <summary>
        /// impact of number of paths on gap of demand pinning
        /// This function starts with numpaths = 1 on each topology
        /// and increases the number of paths by 1 each time and recomputes the gap.
        /// for B4, Abilene and SWAN.
        /// </summary>
        // TODO: what is threshold prec?
        public static void impactNumPathsDP()
        {
            var topologies = new Dictionary<string, string>();
            topologies["B4"] = @"..\Topologies\b4-teavar.json";
            topologies["SWAN"] = @"..\Topologies\swan.json";
            topologies["Abilene"] = @"..\Topologies\abilene.json";

            Heuristic heuristicName = Heuristic.DemandPinning;
            string logDir = @"..\logs\demand_pinning_sweep_paths\" + Utils.GetFID() + @"\";

            // TODO: ideally the problem parameters should be inputs to the function.
            double thresholdPerc = 5;
            double timeToTerminate = 1200;
            int numProcessors = 16;
            int start = 1;
            int step = 1;
            int end = 6;
            int end_try = 6;

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);

            foreach (var (topoName, topoPath) in topologies)
            {
                var topology = Parser.ReadTopologyJson(topoPath);
                var maxThreshold = topology.MinCapacity();
                string logFile = topoName + @"_" + heuristicName + ".txt";

                Utils.CreateFile(logDir, logFile, removeIfExist: true);
                Utils.AppendToFile(logDir, logFile, maxThreshold.ToString());

                var threshold = thresholdPerc * maxThreshold / 100;

                // TODO: shouldn't you check to see if the new number of paths actually exist?
                for (int i = start; i <= end; i += step)
                {
                    int numPaths = i;
                    var (optimal, heuristic, demands) = CliUtils.maximizeOptimalityGapDemandPinning<GRBVar, GRBModel>(
                        solver: solver, topology: topology, numPaths: numPaths, threshold: threshold, numProcessors: numProcessors);
                    Console.WriteLine("trying the demands on the same topo with increased paths");
                    for (int j = start; j <= end_try; j += step)
                    {
                        var (optimalG, heuristicG) = CliUtils.getOptimalDemandPinningTotalDemand(solver: solver,
                            demands: (Dictionary<(string, string), double>)demands, topology: topology, numPaths: j, threshold: threshold);
                        Console.WriteLine("=== try: numPaths=" + j + " optimal= " + optimal + " heuristic= " + heuristic + " gap= " + (optimal - heuristic));
                    }
                    double gap = optimal - heuristic;
                    Utils.AppendToFile(logDir, logFile, numPaths + ", " + threshold + ", " + optimal + ", " + heuristic + ", " + gap);
                    Console.WriteLine("==== Gap --> " + " numPaths=" + numPaths + " threshold=" + threshold + " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                }
            }
        }

        /// <summary>
        /// Vaying the number of nodes in a random regular topology and seeing the
        /// gap vs num nodes effect.
        /// </summary>
        public static void impactRandomRegularGraphParamsDP()
        {
            // TODO: ideally these things should be inputs.
            double capacity = 5000;
            List<int> seedList = new List<int>() { 0, 1, 2, 3 };
            int thresholdPerc = 5;
            int numPaths = 2;
            int timeToTerminate = 1800;
            string logDir = @"..\logs\demand_pinning_sweep_topo\" + Utils.GetFID() + @"\";
            string logFile = @"random_regular_graphs_" + Heuristic.DemandPinning + ".txt";
            Utils.CreateFile(logDir, logFile, removeIfExist: true);
            int numProcessors = 16;
            // evaluation sweep parameters
            int startNodes = 8;
            int stepNodes = 2;
            int endNodes = 14;
            int startRadix = 3;
            int stepRadix = 2;
            int endRadix = 7;

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);

            foreach (int seed in seedList)
            {
                for (int sn = startNodes; sn <= endNodes; sn += stepNodes)
                {
                    for (int sr = startRadix; sr <= endRadix; sr += stepRadix)
                    {
                        var topo = Topology.RandomRegularGraph(sn, sr, capacity, seed: seed);
                        var maxThreshold = topo.MinCapacity();
                        var threshold = thresholdPerc * maxThreshold / 100;
                        var (optimal, heuristic, demands) = CliUtils.maximizeOptimalityGapDemandPinning<GRBVar, GRBModel>(
                            solver: solver, topology: topo, numPaths: numPaths, threshold: threshold, numProcessors: numProcessors);
                        double gap = optimal - heuristic;
                        var diameter = topo.diameter();
                        var avgShortestPathLen = topo.avgShortestPathLength();
                        Utils.AppendToFile(logDir, logFile, seed + ", " + sn + ", " + sr + ", " + numPaths + ", " +
                            threshold + ", " + diameter + ", " + avgShortestPathLen + ", " + optimal + ", " + heuristic + ", " + gap);
                        Console.WriteLine("==== Gap --> " + "seed=" + seed + " numNodes=" + sn + " numRadix=" + sr + " numPaths=" + numPaths + " threshold=" + threshold +
                            " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                    }
                }
            }
        }

        /// <summary>
        /// Vaying the number of nodes and neighbors in small word topology.
        /// </summary>
        /// TODO: explain what a small world topology is.
        public static void impactSmallWordGraphParamsDP()
        {
            double capacity = 5000;
            int thresholdPerc = 5;
            int numPaths = 2;
            int timeToTerminate = 3600;
            string logDir = @"..\logs\demand_pinning_sweep_topo\" + Utils.GetFID() + @"\";
            string logFile = @"small_word_graphs_" + Heuristic.DemandPinning + ".txt";
            Utils.CreateFile(logDir, logFile, removeIfExist: true);
            int numProcessors = 16;
            // evaluation sweep parameters
            int startNodes = 13;
            int stepNodes = 2;
            int endNodes = 13;
            int startRadix = 4;
            int stepRadix = 2;
            int endRadix = 8;

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);
            for (int sn = startNodes; sn <= endNodes; sn += stepNodes)
            {
                for (int sr = startRadix; sr <= endRadix; sr += stepRadix)
                {
                    var topo = Topology.SmallWordGraph(sn, sr, capacity);
                    var maxThreshold = topo.MinCapacity();
                    var threshold = thresholdPerc * maxThreshold / 100;
                    var (optimal, heuristic, demands) = CliUtils.maximizeOptimalityGapDemandPinning<GRBVar, GRBModel>(
                        solver: solver, topology: topo, numPaths: numPaths, threshold: threshold, numProcessors: numProcessors);
                    double gap = optimal - heuristic;
                    var diameter = topo.diameter();
                    var avgShortestPathLen = topo.avgShortestPathLength();
                    Utils.AppendToFile(logDir, logFile, sn + ", " + sr + ", " + numPaths + ", " +
                        threshold + ", " + diameter + ", " + avgShortestPathLen + ", " + optimal + ", " + heuristic + ", " + gap);
                    Console.WriteLine("==== Gap --> " + " numNodes=" + sn + " numRadix=" + sr + " numPaths=" + numPaths + " threshold=" + threshold +
                        " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
                }
            }
        }

        /// <summary>
        /// evaluating impact of number of paths and partitions for pop.
        /// </summary>
        /// TODO: This says it evaluates the number of paths and partitions for pop but also contains
        /// Code that references demand pinning? fix the comment and function title or separate them.
        public static void impactNumPathsPartitionsPop()
        {
            // TODO: same comment as before about function parameters.
            var topology = Parser.ReadTopologyJson(@"..\Topologies\b4-teavar.json");
            Heuristic heuristicName = Heuristic.Pop;
            int numProcessors = 16;
            int demandPinningThreshold = 100;
            double demandUB = -1;
            int numThreads = 1;
            int minPartition = 2;
            int maxPartition = 5;
            int partitionStep = 1;
            int minPaths = 1;
            int maxPaths = 4;
            int pathStep = 1;
            string fid = DateTime.Now.Year + "_" + DateTime.Now.Month + "_" + DateTime.Now.Day + "_" + DateTime.Now.Hour + "_" + DateTime.Now.Minute + "_" +
                DateTime.Now.Second + "_" + DateTime.Now.Millisecond;
            string logDir = @"..\logs\pop_diff_paths_diff_partitions\" + heuristicName + "_";

            switch (heuristicName)
            {
                case Heuristic.Pop:
                    logDir = logDir + "_" + minPartition + "_" + maxPartition + "_" + partitionStep + "_" + minPaths + "_" + maxPaths + "_" + pathStep;
                    break;
                case Heuristic.DemandPinning:
                    logDir = logDir + demandPinningThreshold + "_";
                    break;
                default:
                    throw new Exception("heuristic name not found!");
            }

            logDir = logDir + fid + @"\";
            string kktFile = @"kkt_" + heuristicName + ".txt";
            Utils.CreateFile(logDir, kktFile, true);
            int numPartitions = minPartition;
            // TODO: the other functions are using GurobiSOS but this one uses GurobiBinary why?
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(10, 1, numThreads);

            while (numPartitions <= maxPartition)
            {
                IDictionary<(string, string), int> partition = topology.RandomPartition(numPartitions);

                int numPaths = minPaths;

                while (numPaths <= maxPaths)
                {
                    // foreach (int i in Enumerable.Range(1, 20)) {
                    // int timeout = i * 6;
                    int timeout = 1800;
                    solver.CleanAll();
                    solver.SetTimeout(timeout);
                    var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, numPartitions, demandPinningThreshold,
                        partition: partition, partitionSensitivity: 0.1);
                    var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                    (TEOptimizationSolution, TEOptimizationSolution) result = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB);
                    var dur = timer.ElapsedMilliseconds;
                    double optimal = result.Item1.MaxObjective;
                    double heuristic = result.Item2.MaxObjective;
                    var gap = optimal - heuristic;
                    Utils.AppendToFile(logDir, kktFile, dur + ", " + numPartitions + ", " + numPaths + ", " + gap);
                    Console.WriteLine("==== KKT --> " + " partition=" + numPartitions + " paths=" + numPaths + " dur=" + dur + " gap=" + gap +
                            " optimal=" + optimal + " heuristic=" + heuristic);
                    // }
                    numPaths += pathStep;
                }
                numPartitions += partitionStep;
            }
        }

        /// <summary>
        /// evaluating gap vs time for different methods on DP.
        /// </summary>
        // TODO: add a comment to explain what the different methods are.
        // I also see from the code that you are comparing to hillclimbing and others.
        public static void compareGapDelayDiffMethodsDP()
        {
            var topology = Parser.ReadTopologyJson(@"..\Topologies\b4-teavar.json");

            int numPaths = 2;
            int numThreads = 1;
            double timeout = 5000;
            int numProcessors = 16;
            var heuristicName = Heuristic.DemandPinning;
            var demandUB = -1;
            var demandPinningThreshold = 250;

            List<int> seedList = new List<int>() { 0, 1, 2, 3 };
            string logDir = @"..\logs\gap_vs_time\" + heuristicName + "_";
            logDir = logDir + Utils.GetFID() + @"\";
            string kktFile = @"kkt_" + heuristicName + ".txt";
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(timeout, 0, numThreads, recordProgress: true, logPath: Path.Combine(logDir, kktFile));
            var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver: solver, topology: topology, h: heuristicName, numPaths: numPaths, demandPinningThreshold: demandPinningThreshold);
            var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
            (TEOptimizationSolution, TEOptimizationSolution) result = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder);
            double optimal = result.Item1.MaxObjective;
            double heuristic = result.Item2.MaxObjective;
            var gap = optimal - heuristic;
            solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(timeout, 0, numThreads, recordProgress: false);
            Console.WriteLine("==== KKT --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            foreach (var seed in seedList)
            {
                string hillClimbingFile = @"hillclimbing_" + heuristicName + "_" + seed + ".txt";
                string simulatedAnnealingFile = @"simulatedannealing_" + heuristicName + "_" + seed + ".txt";
                string randomSearchFile = @"randomSearch_" + heuristicName + "_" + seed + ".txt";

                int numNeighbors = 100;
                double stddev = 500;
                int numDemands = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, demandPinningThreshold: demandPinningThreshold);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.HillClimbingAdversarialGenerator(optimalEncoder, heuristicEncoder, numTrials: numDemands,
                    numNeighbors: numNeighbors, demandUB: demandUB, stddev: stddev, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, hillClimbingFile),
                    timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== HillClimber --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);

                numNeighbors = 100;
                stddev = 500;
                int numTmpSteps = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, demandPinningThreshold: demandPinningThreshold);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.SimulatedAnnealing(optimalEncoder, heuristicEncoder, numTmpSteps,
                    numNeighbors, demandUB, stddev, initialTmp: 500, tmpDecreaseFactor: 0.1, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, simulatedAnnealingFile),
                    timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== SA --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);

                numDemands = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, demandPinningThreshold: demandPinningThreshold);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.RandomAdversarialGenerator(optimalEncoder, heuristicEncoder, numDemands,
                    demandUB, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, randomSearchFile), timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== Random --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            }
        }

        /// <summary>
        /// evaluating gap vs time for different approaches on Pop.
        /// </summary>
        public static void compareGapDelayDiffMethodsPop()
        {
            var topology = Parser.ReadTopologyJson(@"..\Topologies\b4-teavar.json");

            int numProcessors = 16;
            int numPaths = 2;
            Heuristic heuristicName = Heuristic.Pop;
            int numSlices = 2;
            int demandPinningThreshold = 100;
            double demandUB = -1;
            int numThreads = 1;
            double timeout = 1000;
            string logDir = @"..\logs\gap_vs_time\" + heuristicName + "_";

            // TODO: this seems pointless since your fixating on pop?
            switch (heuristicName)
            {
                case Heuristic.Pop:
                    logDir = logDir + numSlices + "_";
                    break;
                case Heuristic.DemandPinning:
                    logDir = logDir + demandPinningThreshold + "_";
                    break;
                default:
                    throw new Exception("heuristic name not found!");
            }

            logDir = logDir + Utils.GetFID() + @"\";
            string kktFile = @"kkt_" + heuristicName + ".txt";
            IDictionary<(string, string), int> partition = topology.RandomPartition(numSlices);

            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(timeout, 0, numThreads, recordProgress: true, logPath: Path.Combine(logDir, kktFile));
            var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, numSlices, demandPinningThreshold,
                partition: partition);
            var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
            var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
            (TEOptimizationSolution, TEOptimizationSolution) result = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB);
            double optimal = result.Item1.MaxObjective;
            double heuristic = result.Item2.MaxObjective;
            var gap = optimal - heuristic;
            Console.WriteLine("==== KKT --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);

            List<int> seedList = new List<int>() { 2, 3 };
            solver = (ISolver<GRBVar, GRBModel>)new GurobiBinary(timeout, 0, numThreads, recordProgress: false);
            foreach (var seed in seedList)
            {
                string hillClimbingFile = @"hillclimbing_" + heuristicName + "_" + seed + ".txt";
                string simulatedAnnealingFile = @"simulatedannealing_" + heuristicName + "_" + seed + ".txt";
                string randomSearchFile = @"randomSearch_" + heuristicName + "_" + seed + ".txt";
                int numNeighbors = 100;
                double stddev = 500;
                int numDemands = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, numSlices, demandPinningThreshold,
                    partition: partition);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.HillClimbingAdversarialGenerator(optimalEncoder, heuristicEncoder, numTrials: numDemands,
                    numNeighbors: numNeighbors, demandUB: demandUB, stddev: stddev, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, hillClimbingFile),
                    timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== HillClimber --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);

                numNeighbors = 100;
                stddev = 500;
                int numTmpSteps = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, numSlices, demandPinningThreshold,
                    partition: partition);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.SimulatedAnnealing(optimalEncoder, heuristicEncoder, numTmpSteps,
                    numNeighbors, demandUB, stddev, initialTmp: 500, tmpDecreaseFactor: 0.1, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, simulatedAnnealingFile),
                    timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== SA --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);

                numDemands = 1000000;
                solver.CleanAll();
                (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName, numPaths, numSlices, demandPinningThreshold,
                    partition: partition);
                optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
                adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths, numProcessors);
                result = adversarialInputGenerator.RandomAdversarialGenerator(optimalEncoder, heuristicEncoder, numDemands,
                    demandUB, seed: seed, storeProgress: true, logPath: Path.Combine(logDir, randomSearchFile), timeout: timeout);
                optimal = result.Item1.MaxObjective;
                heuristic = result.Item2.MaxObjective;
                gap = optimal - heuristic;
                Console.WriteLine("==== Random --> " + " gap=" + gap + " optimal=" + optimal + " heuristic=" + heuristic);
            }
        }

        /// <summary>
        /// compare problem size vs topo size and latency vs topo size.
        /// </summary>
        /// TODO: which approach is missing from the comment.
        // TODO: remove commented out code.
        // TODO: mention in the summary comment how the code does this.
        // TODO: I think what you are really doing here is benchmarking the inner problems against each other and not really using metaopt?
        // TODO: also i dont see hwo you are varying the topology? I think either remove this function or fix it to do explicitly what you are describing.
        public static void compareTopoSizeLatency()
        {
            Heuristic heuristicName = Heuristic.DemandPinning;

            var topoName = "B4";
            var topology = Parser.ReadTopologyJson(@"..\Topologies\b4-teavar.json");
            string logDir = @"..\logs\scale_latency_problem\" + Utils.GetFID() + @"\";
            double thresholdPerc = 5;
            double timeToTerminate = 10;
            int numPaths = 2;
            int numPartitions = 2;

            var partition = topology.RandomPartition(numPartitions);
            ISolver<GRBVar, GRBModel> solver = (ISolver<GRBVar, GRBModel>)new GurobiSOS(verbose: 1, timeToTerminateNoImprovement: timeToTerminate);
            string logFile = topoName + "_" + heuristicName + @"_" + heuristicName;
            Utils.CreateFile(logDir, logFile, removeIfExist: true);
            var maxThreshold = topology.MinCapacity();
            Utils.AppendToFile(logDir, logFile, maxThreshold + ", " + timeToTerminate);
            var threshold = thresholdPerc * maxThreshold / 100;

            // // gurobi outer
            // solver.CleanAll();
            // solver.GetModel().Parameters.LogFile = Path.Combine(logDir, logFile + "_outer.txt");
            // var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName,
            //     numPaths, demandPinningThreshold: threshold, numSlices: numPartitions, partition: partition);
            // var optimalEncoder = new OptimalEncoder<GRBVar, GRBModel>(solver, topology, numPaths);
            // var adversarialInputGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, numPaths);
            // var timer = Stopwatch.StartNew();
            // (OptimizationSolution, OptimizationSolution) result =
            //     adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder);
            // var outer_gurobi_time = timer.ElapsedMilliseconds;
            // double optimal = result.Item1.TotalDemandMet;
            // double heuristic = result.Item2.TotalDemandMet;
            // var demands = result.Item1.Demands;
            // var outer_sos_constr = solver.GetModel().NumSOS;
            // var outer_vars = solver.GetModel().NumVars;
            // var outer_lin_constraints = solver.GetModel().NumConstrs;
            // double gap = optimal - heuristic;
            // var dic_demands = (Dictionary<(string, string), double>)demands;
            // Console.WriteLine("=====metaoptimize " + outer_gurobi_time + " sos=" + outer_sos_constr + " lin " + outer_lin_constraints);
            Dictionary<(string, string), double> dic_demands = new Dictionary<(string, string), double>();
            var rng = new Random();
            foreach (var pair in topology.GetNodePairs())
            {
                dic_demands[pair] = rng.NextDouble() * 5000 * 0.5;
            }
            solver.CleanAll();
            solver.GetModel().Parameters.LogFile = Path.Combine(logDir, logFile + "_inner_heuristic.txt");
            var (heuristicEncoder, _, _) = CliUtils.getHeuristic<GRBVar, GRBModel>(solver, topology, heuristicName,
                numPaths, demandPinningThreshold: threshold, DirectEncoder: true, numSlices: numPartitions, partition: partition);

            Stopwatch timer;
            if (heuristicName == Heuristic.Pop)
            {
                for (int i = 0; i < numPartitions; i++)
                {
                    solver.CleanAll();
                    var partDemand = new Dictionary<(string, string), double>();
                    foreach (var pair in topology.GetNodePairs())
                    {
                        if (partition[pair] == i & dic_demands.ContainsKey(pair))
                        {
                            partDemand[pair] = dic_demands[pair];
                        }
                        else
                        {
                            partDemand[pair] = 0;
                        }
                    }
                    var heuristicEncoding = heuristicEncoder.Encoding(topology, inputEqualityConstraints: partDemand, noAdditionalConstraints: true);
                    // gurobi heuristic inner
                    timer = Stopwatch.StartNew();
                    var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(heuristicEncoding.MaximizationObjective);
                    var inner_heuristic_time = timer.ElapsedMilliseconds;
                    var inner_heuristic_lin_constraints = solver.GetModel().NumConstrs;
                    var inner_heuristic_vars = solver.GetModel().NumVars;
                    Console.WriteLine("=====inner heuristic " + i + " " + inner_heuristic_time + " tot demand=" +
                                ((TEMaxFlowOptimizationSolution)heuristicEncoder.GetSolution(solverSolutionHeuristic)).MaxObjective);
                }
            }
            else
            {
                var heuristicEncoding = heuristicEncoder.Encoding(topology, inputEqualityConstraints: dic_demands, noAdditionalConstraints: true);
                // gurobi heuristic inner
                timer = Stopwatch.StartNew();
                var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(heuristicEncoding.MaximizationObjective);
                var inner_heuristic_time = timer.ElapsedMilliseconds;
                var inner_heuristic_lin_constraints = solver.GetModel().NumConstrs;
                var inner_heuristic_vars = solver.GetModel().NumVars;
                Console.WriteLine("=====inner heuristic " + inner_heuristic_time + " tot demand=" +
                        ((TEMaxFlowOptimizationSolution)heuristicEncoder.GetSolution(solverSolutionHeuristic)).MaxObjective);
            }

            solver.CleanAll();
            solver.GetModel().Parameters.LogFile = Path.Combine(logDir, logFile + "_inner_optimal.txt");
            var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, numPaths);
            var optimalEncoding = optimalEncoder.Encoding(topology, inputEqualityConstraints: dic_demands, noAdditionalConstraints: true);
            // gurobi optimal inner
            timer = Stopwatch.StartNew();
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
            var inner_optimal_time = timer.ElapsedMilliseconds;
            var inner_optimal_linear_constraints = solver.GetModel().NumConstrs;
            var inner_optimal_vars = solver.GetModel().NumVars;
            Console.WriteLine("====inner optimal " + inner_optimal_time);
            // Utils.AppendToFile(logDir, logFile, numPaths + ", " + threshold + ", " + radix + ", " + i + ", " + optimal + ", " + heuristic + ", " + gap +
            //     ", " + outer_gurobi_time + ", " + outer_lin_constraints + ", " + outer_sos_constr + ", " + outer_vars +
            //     ", " + inner_heuristic_time + ", " + inner_heuristic_lin_constraints + ", " + inner_heuristic_vars + ", " + inner_optimal_time +
            //     ", " + inner_optimal_linear_constraints + ", " + inner_optimal_vars);
            // Console.WriteLine("==== Gap --> " + " numPaths=" + numPaths + " threshold=" + threshold + " optimal=" + optimal + " heuristic=" + heuristic + " gap=" + gap);
        }
    }
}