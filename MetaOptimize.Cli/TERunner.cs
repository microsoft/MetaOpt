using System.Diagnostics;
using CommandLine;
using Gurobi;
using ZenLib;
using ZenLib.ModelChecking;

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Main entry point for traffic engineering experiments.
    /// Executes adversarial optimization to find worst-case demand patterns for routing heuristics.
    /// </summary>
    public static class TERunner
    {
        /// <summary>
        /// Runs Traffic Engineering optimization.
        /// Uses CliUtils for topology loading and heuristic setup (same as original ssMain).
        /// </summary>
        public static void RunAdvanced(string[] args)
        {
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
            var demandList = new GenericList((opts.DemandList.Split(",")).Select(x => double.Parse(x) * opts.DownScaleFactor).ToHashSet());
            Utils.logger(
                string.Format("Demand List:{0}", Newtonsoft.Json.JsonConvert.SerializeObject(demandList.List, Newtonsoft.Json.Formatting.Indented)),
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

        /// <summary>
        /// Runs Traffic Engineering optimization.
        /// Uses CliUtils for topology loading and heuristic setup (same as original ssMain).
        /// </summary>
        public static void RunSimple(CliArgs args)
        {
            var topologyFile = args.Get("--topologyFile", "simple.json");
            var heuristic = args.Get("--heuristic", "Pop");
            var paths = args.GetInt("--paths", 1);
            var verbose = args.GetBool("--verbose", false);
            var timeout = args.GetDouble("--timeout", 1000);
            var numThreads = args.GetInt("--numThreads", 1);
            var popSlices = args.GetInt("--popSlices", 2);

            Console.WriteLine($"Topology File: {topologyFile}");
            Console.WriteLine($"Heuristic: {heuristic}");
            Console.WriteLine($"Paths per pair: {paths}");

            // Load topology from JSON (simple loading, not CliUtils)
            var topology = ReadTopologyFromFile(topologyFile);

            // Create solver
            var solver = new GurobiSOS(
                timeout: timeout,
                verbose: Convert.ToInt32(verbose),
                numThreads: numThreads);

            // Create optimal encoder
            var optimalEncoder = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(solver, maxNumPaths: paths);

            // Create heuristic encoder
            IEncoder<GRBVar, GRBModel> heuristicEncoder;
            IDictionary<(string, string), int> partition = null;

            switch (heuristic)
            {
                case "Pop":
                    partition = topology.RandomPartition(popSlices);
                    heuristicEncoder = new PopEncoder<GRBVar, GRBModel>(
                        solver, maxNumPaths: paths, numPartitions: popSlices, demandPartitions: partition);
                    break;
                case "DemandPinning":
                    var dpThreshold = args.GetDouble("--dpThreshold", 0.5);
                    heuristicEncoder = new DirectDemandPinningEncoder<GRBVar, GRBModel>(
                        solver, k: paths, threshold: dpThreshold);
                    break;
                default:
                    throw new Exception($"Unsupported heuristic: {heuristic}");
            }

            // Create adversarial generator
            var adversarialGenerator = new TEAdversarialInputGenerator<GRBVar, GRBModel>(topology, maxNumPaths: paths);

            // Run optimization - SIMPLE call like original TEMain
            var timer = Stopwatch.StartNew();
            var (optimalSolution, heuristicSolution) = adversarialGenerator.MaximizeOptimalityGap(
                optimalEncoder, heuristicEncoder);
            timer.Stop();

            // Display results
            Console.WriteLine("Optimal:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(optimalSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");
            Console.WriteLine("Heuristic:");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(heuristicSolution, Newtonsoft.Json.Formatting.Indented));
            Console.WriteLine("****");

            var optimal = optimalSolution.MaxObjective;
            var heuristicObj = heuristicSolution.MaxObjective;
            Console.WriteLine($"optimalG={optimal}, heuristicG={heuristicObj}, time={timer.ElapsedMilliseconds}ms");

            // Validation - like original TEMain
            var demands = new Dictionary<(string, string), double>(optimalSolution.Demands);

            var optGSolver = new GurobiSOS();
            var optimalEncoderG = new TEMaxFlowOptimalEncoder<GRBVar, GRBModel>(optGSolver, maxNumPaths: paths);

            var popGSolver = new GurobiSOS();
            IEncoder<GRBVar, GRBModel> heuristicEncoderG;

            switch (heuristic)
            {
                case "Pop":
                    heuristicEncoderG = new PopEncoder<GRBVar, GRBModel>(
                        popGSolver, maxNumPaths: paths, numPartitions: popSlices, demandPartitions: partition);
                    break;
                case "DemandPinning":
                    var dpThreshold = args.GetDouble("--dpThreshold", 0.5);
                    heuristicEncoderG = new DirectDemandPinningEncoder<GRBVar, GRBModel>(
                        popGSolver, k: paths, threshold: dpThreshold);
                    break;
                default:
                    throw new Exception($"Unsupported heuristic: {heuristic}");
            }

            Utils.checkSolution(topology, heuristicEncoderG, optimalEncoderG, heuristicObj, optimal, demands, "gurobiCheck");
        }

        private static Topology ReadTopologyFromFile(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var topologiesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Topologies"));
            var filePath = Path.Combine(topologiesDir, fileName);

            if (!File.Exists(filePath))
            {
                topologiesDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Topologies"));
                filePath = Path.Combine(topologiesDir, fileName);
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Topology file not found: {fileName}");
            }

            Console.WriteLine($"Loading topology from: {filePath}");

            var json = File.ReadAllText(filePath);
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<TopologyJson>(json);

            var topology = new Topology();

            foreach (var node in data.Nodes)
            {
                topology.AddNode(node.Id.ToString());
            }

            foreach (var link in data.Links)
            {
                topology.AddEdge(link.Source.ToString(), link.Target.ToString(), capacity: link.Capacity);
            }

            Console.WriteLine($"Loaded: {data.Nodes.Count} nodes, {data.Links.Count} edges");
            return topology;
        }

        private class TopologyJson
        {
            public List<NodeJson> Nodes { get; set; } = new ();
            public List<LinkJson> Links { get; set; } = new ();
        }

        private class NodeJson
        {
            [Newtonsoft.Json.JsonProperty("id")]
            public object Id { get; set; }
        }

        private class LinkJson
        {
            [Newtonsoft.Json.JsonProperty("source")]
            public object Source { get; set; }
            [Newtonsoft.Json.JsonProperty("target")]
            public object Target { get; set; }
            [Newtonsoft.Json.JsonProperty("capacity")]
            public double Capacity { get; set; }
        }
    }
}