// <copyright file="TERunner.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System.Diagnostics;
    using CommandLine;
    using Gurobi;
    using ZenLib;
    using ZenLib.ModelChecking;

    /// <summary>
    /// Runner for Traffic Engineering adversarial optimization.
    /// Finds demand patterns that maximize the gap between optimal routing and heuristic algorithms.
    /// </summary>
    /// <remarks>
    /// Flow: Load topology → Configure heuristic → Run adversarial optimization → Validate results.
    ///
    /// Supports multiple heuristics:
    /// - Pop: Partition-based routing with random demand partitioning
    /// - DemandPinning: Threshold-based path selection
    /// - ExpectedPop: Average performance across multiple partitions
    /// - PopDp: Combined Pop and DemandPinning
    ///
    /// Supports multiple search methods:
    /// - Direct: Find maximum gap directly via bilevel optimization
    /// - Search: Binary search for gap within interval
    /// - FindFeas: Find any solution with gap >= threshold
    /// - Random: Random sampling for gap estimation
    /// - HillClimber: Local search with neighborhood exploration
    /// - SimulatedAnnealing: Probabilistic local search with temperature cooling
    ///
    /// Includes clustering support for scalable optimization on large topologies.
    /// </remarks>
    public static class TERunner
    {
        /// <summary>
        /// Runs Traffic Engineering adversarial optimization.
        /// Main entry point that loads topology and dispatches to solver.
        /// </summary>
        /// <param name="opts">Command-line options containing TE parameters.</param>
        public static void Run(CliOptions opts)
        {
            if (opts == null)
            {
                Console.WriteLine("ERROR: Options not parsed correctly.");
                Environment.Exit(1);
            }

            // Load topology and optional cluster configurations
            var (topology, clusters) = CliUtils.getTopology(
                opts.TopologyFile,
                opts.PathFile,
                opts.DownScaleFactor,
                opts.EnableClustering,
                opts.NumClusters,
                opts.ClusterDir,
                opts.Verbose);

            GetSolverAndRunNetwork(topology, clusters);
        }

        /// <summary>
        /// Creates the appropriate solver and runs network optimization.
        /// Dispatches to Zen (SMT) or Gurobi (MIP) based on configuration.
        /// </summary>
        /// <param name="topology">The network topology to optimize.</param>
        /// <param name="clusters">Optional cluster topologies for hierarchical optimization.</param>
        /// <exception cref="Exception">Thrown when an unsupported solver is specified.</exception>
        private static void GetSolverAndRunNetwork(Topology topology, List<Topology> clusters)
        {
            var opts = CliOptions.Instance;

            switch (opts.SolverChoice)
            {
                case SolverChoice.OrTools:
                    RunNetwork(new ORToolsSolver(), topology, clusters);
                    break;

                case SolverChoice.Zen:
                    RunNetwork(new SolverZen(), topology, clusters);
                    break;

                case SolverChoice.Gurobi:
                    var storeProgress = opts.StoreProgress && (opts.Method == MethodChoice.Direct);
                    var solver = new GurobiSOS(
                        opts.Timeout,
                        Convert.ToInt32(opts.Verbose),
                        timeToTerminateNoImprovement: opts.TimeToTerminateIfNoImprovement,
                        numThreads: opts.NumGurobiThreads,
                        recordProgress: storeProgress,
                        logPath: opts.LogFile);
                    RunNetwork(solver, topology, clusters);
                    break;

                default:
                    throw new Exception($"Unsupported solver: {opts.SolverChoice}. Valid options: OrTools, Gurobi, Zen");
            }
        }

        /// <summary>
        /// Generic implementation of traffic engineering adversarial optimization.
        /// </summary>
        /// <typeparam name="TVar">Solver variable type (GRBVar or Zen).</typeparam>
        /// <typeparam name="TSolution">Solver solution type (GRBModel or ZenSolution).</typeparam>
        /// <param name="solver">The solver instance to use.</param>
        /// <param name="topology">The network topology to optimize.</param>
        /// <param name="clusters">Optional cluster topologies for hierarchical optimization.</param>
        /// <remarks>
        /// Execution phases:
        /// 1. Setup: Create optimal encoder, heuristic encoder, and adversarial generator
        /// 2. Optimization: Run selected method (Direct, Search, Random, etc.)
        /// 3. Post-processing: Optional FullOpt and UBFocus refinement
        /// 4. Validation: Verify solution with independent solver instances.
        /// </remarks>
        private static void RunNetwork<TVar, TSolution>(
            ISolver<TVar, TSolution> solver,
            Topology topology,
            List<Topology> clusters)
        {
            var opts = CliOptions.Instance;

            // Setup optimal encoder for maximum flow
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, opts.Paths);

            // Setup adversarial input generator
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSolution>(
                topology, opts.Paths, opts.NumProcesses);

            // Setup heuristic encoder based on selected algorithm
            var (heuristicEncoder, partitioning, partitionList) = CliUtils.getHeuristic<TVar, TSolution>(
                solver,
                topology,
                opts.Heuristic,
                opts.Paths,
                opts.PopSlices,
                opts.DemandPinningThreshold * opts.DownScaleFactor,
                numSamples: opts.NumRandom,
                partitionSensitivity: opts.PartitionSensitivity,
                scaleFactor: opts.DownScaleFactor,
                InnerEncoding: opts.InnerEncoding,
                maxShortestPathLen: opts.MaxShortestPathLen);

            // Parse demand quantization levels
            var demandList = new GenericList(
                opts.DemandList.Split(",")
                    .Select(x => double.Parse(x) * opts.DownScaleFactor)
                    .ToHashSet());

            Utils.logger(
                $"Demand List:{Newtonsoft.Json.JsonConvert.SerializeObject(demandList.List, Newtonsoft.Json.Formatting.Indented)}",
                opts.Verbose);

            var timer = Stopwatch.StartNew();
            Utils.logger("Starting optimization", opts.Verbose);

            // Run selected optimization method
            (TEOptimizationSolution, TEOptimizationSolution) result;
            switch (opts.Method)
            {
                case MethodChoice.Direct:
                    result = CliUtils.getMetaOptResult(
                        adversarialInputGenerator, optimalEncoder, heuristicEncoder,
                        opts.DemandUB, opts.InnerEncoding, demandList,
                        opts.EnableClustering, opts.ClusterVersion, clusters,
                        opts.NumInterClusterSamples, opts.NumNodesPerCluster,
                        opts.NumInterClusterQuantizations, opts.Simplify, opts.Verbose,
                        opts.MaxDensity, opts.LargeDemandLB, opts.maxLargeDistance,
                        opts.maxSmallDistance, false, null);
                    break;

                case MethodChoice.Search:
                    Utils.logger("Using interval search for gap", opts.Verbose);
                    result = adversarialInputGenerator.FindMaximumGapInterval(
                        optimalEncoder, heuristicEncoder,
                        opts.Confidencelvl, opts.StartingGap, opts.DemandUB,
                        demandList: demandList);
                    break;

                case MethodChoice.FindFeas:
                    Utils.logger("Finding feasible solution with target gap", opts.Verbose);
                    result = adversarialInputGenerator.FindOptimalityGapAtLeast(
                        optimalEncoder, heuristicEncoder,
                        opts.StartingGap, opts.DemandUB,
                        demandList: demandList, simplify: opts.Simplify);
                    break;

                case MethodChoice.Random:
                    Utils.logger("Using random search", opts.Verbose);
                    result = adversarialInputGenerator.RandomAdversarialGenerator(
                        optimalEncoder, heuristicEncoder,
                        opts.NumRandom, opts.DemandUB,
                        seed: opts.Seed, verbose: opts.Verbose,
                        storeProgress: opts.StoreProgress, logPath: opts.LogFile,
                        timeout: opts.Timeout);
                    break;

                case MethodChoice.HillClimber:
                    Utils.logger("Using hill climbing", opts.Verbose);
                    result = adversarialInputGenerator.HillClimbingAdversarialGenerator(
                        optimalEncoder, heuristicEncoder,
                        opts.NumRandom, opts.NumNeighbors, opts.DemandUB, opts.StdDev,
                        seed: opts.Seed, verbose: opts.Verbose,
                        storeProgress: opts.StoreProgress, logPath: opts.LogFile,
                        timeout: opts.Timeout);
                    break;

                case MethodChoice.SimulatedAnnealing:
                    Utils.logger("Using simulated annealing", opts.Verbose);
                    result = adversarialInputGenerator.SimulatedAnnealing(
                        optimalEncoder, heuristicEncoder,
                        opts.NumRandom, opts.NumNeighbors, opts.DemandUB, opts.StdDev,
                        opts.InitTmp, opts.TmpDecreaseFactor,
                        seed: opts.Seed, verbose: opts.Verbose,
                        storeProgress: opts.StoreProgress, logPath: opts.LogFile,
                        timeout: opts.Timeout);
                    break;

                default:
                    throw new Exception($"Unknown method: {opts.Method}. " +
                        "Valid options: Direct, Search, FindFeas, Random, HillClimber, SimulatedAnnealing");
            }

            // Post-processing: Full optimization refinement (clustering only)
            if (opts.FullOpt)
            {
                if (!opts.EnableClustering)
                {
                    throw new Exception("FullOpt requires clustering to be enabled");
                }
                if (opts.InnerEncoding != InnerRewriteMethodChoice.PrimalDual)
                {
                    throw new Exception("FullOpt requires PrimalDual inner encoding");
                }

                optimalEncoder.Solver.CleanAll(timeout: opts.FullOptTimer);
                var currDemands = new Dictionary<(string, string), double>(result.Item1.Demands);
                Utils.setEmptyPairsToZero(topology, currDemands);

                result = adversarialInputGenerator.MaximizeOptimalityGap(
                    optimalEncoder, heuristicEncoder, opts.DemandUB,
                    innerEncoding: opts.InnerEncoding, demandList: demandList,
                    simplify: opts.Simplify, verbose: opts.Verbose,
                    demandInits: currDemands);

                optimalEncoder.Solver.CleanAll(focusBstBd: false, timeout: opts.Timeout);
            }

            // Post-processing: Upper bound focus refinement
            if (opts.UBFocus)
            {
                var currDemands = new Dictionary<(string, string), double>(result.Item1.Demands);
                optimalEncoder.Solver.CleanAll(focusBstBd: true, timeout: opts.UBFocusTimer);
                Utils.setEmptyPairsToZero(topology, currDemands);

                result = adversarialInputGenerator.MaximizeOptimalityGap(
                    optimalEncoder, heuristicEncoder, opts.DemandUB,
                    innerEncoding: opts.InnerEncoding, demandList: demandList,
                    simplify: opts.Simplify, verbose: opts.Verbose,
                    demandInits: currDemands);

                optimalEncoder.Solver.CleanAll(focusBstBd: false, timeout: opts.Timeout);
            }

            timer.Stop();

            // Extract results
            var optimal = result.Item1.MaxObjective;
            var heuristic = result.Item2.MaxObjective;
            var demands = new Dictionary<(string, string), double>(result.Item1.Demands);
            Utils.setEmptyPairsToZero(topology, demands);

            // Display results
            Console.WriteLine("##############################################");
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal: {optimal}");
            Console.WriteLine($"Heuristic: {heuristic}");
            Console.WriteLine($"Gap: {optimal - heuristic}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine("##############################################");

            // Special handling for ExpectedPop heuristic
            if (opts.Heuristic == Heuristic.ExpectedPop)
            {
                CliUtils.findGapExpectedPopAdversarialDemandOnIndependentPartitions<GRBVar, GRBModel>(
                    opts, topology, demands, optimal);
            }

            // Validation: verify solution with independent solvers
            ValidateSolution(solver, opts, topology, partitioning, partitionList, optimal, heuristic, demands);
        }

        /// <summary>
        /// Validates the solution using independent Gurobi solver instances.
        /// </summary>
        private static void ValidateSolution<TVar, TSolution>(
            ISolver<TVar, TSolution> solver,
            CliOptions opts,
            Topology topology,
            IDictionary<(string, string), int> partitioning,
            IList<IDictionary<(string, string), int>> partitionList,
            double optimal,
            double heuristic,
            Dictionary<(string, string), double> demands)
        {
            Console.WriteLine("Validating solution...");

            var optimalEncoderG = new TEMaxFlowOptimalEncoder<TVar, TSolution>(
                solver, maxNumPaths: opts.Paths);
            IEncoder<TVar, TSolution> heuristicEncoder;

            switch (opts.Heuristic)
            {
                case Heuristic.Pop:
                    heuristicEncoder = new PopEncoder<TVar, TSolution>(
                        solver, maxNumPaths: opts.Paths,
                        numPartitions: opts.PopSlices,
                        demandPartitions: partitioning);
                    break;

                case Heuristic.DemandPinning:
                    heuristicEncoder = new DirectDemandPinningEncoder<TVar, TSolution>(
                        solver, k: opts.Paths,
                        threshold: opts.DemandPinningThreshold * opts.DownScaleFactor);
                    break;

                case Heuristic.ExpectedPop:
                    heuristicEncoder = new ExpectedPopEncoder<TVar, TSolution>(
                        solver, k: opts.Paths,
                        numSamples: opts.NumRandom,
                        numPartitionsPerSample: opts.PopSlices,
                        demandPartitionsList: partitionList);
                    break;

                case Heuristic.PopDp:
                    throw new Exception("PopDp validation not implemented yet.");

                default:
                    throw new Exception($"Unknown heuristic for validation: {opts.Heuristic}");
            }

            Utils.checkSolution(
                topology, heuristicEncoder, optimalEncoderG,
                heuristic, optimal, demands, "SolverCheck");

            Console.WriteLine("Validation completed.");
        }
    }
}