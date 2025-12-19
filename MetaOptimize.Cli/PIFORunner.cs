// <copyright file="PIFORunner.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System.Diagnostics;

    /// <summary>
    /// Runner for PIFO (Push-In First-Out) packet scheduling adversarial optimization.
    /// Finds packet arrival patterns that maximize scheduling inversions between SP-PIFO and AIFO.
    /// </summary>
    /// <remarks>
    /// Compares two packet scheduling algorithms:
    /// - SP-PIFO with Drop: Strict Priority PIFO with packet dropping under congestion
    /// - AIFO: Approximate Ideal Fair Ordering with window-based admission
    ///
    /// The adversarial generator finds packet rank sequences where AIFO produces
    /// significantly more inversions (out-of-order deliveries) than SP-PIFO.
    ///
    /// An inversion occurs when a lower-priority packet is scheduled before a
    /// higher-priority packet that arrived earlier.
    /// </remarks>
    public sealed class PIFORunner
    {
        /// <summary>
        /// Computes the number of inversions for a single packet.
        /// An inversion occurs when a lower-priority packet precedes this packet in the schedule.
        /// </summary>
        /// <param name="solution">The scheduling solution to analyze.</param>
        /// <param name="orderToRank">Mapping from schedule order to packet rank.</param>
        /// <param name="pid">The packet ID to compute inversions for.</param>
        /// <returns>Number of inversions for the specified packet.</returns>
        /// <remarks>
        /// For admitted packets: counts how many packets scheduled earlier have higher rank values
        /// (lower priority, since lower rank = higher priority).
        /// For dropped packets: counts inversions against all admitted packets.
        /// </remarks>
        private static int ComputeInversionNum(
            PIFOOptimizationSolution solution,
            Dictionary<int, double> orderToRank,
            int pid)
        {
            int numInv = 0;

            // Check if packet was admitted (0.98 threshold handles floating-point)
            if (solution.Admit[pid] >= 0.98)
            {
                int currOrder = solution.Order[pid];
                // Count packets scheduled earlier with worse priority (higher rank)
                for (int prev = 0; prev < currOrder; prev++)
                {
                    if (orderToRank[prev] > solution.Ranks[pid])
                    {
                        numInv += 1;
                    }
                }
            }
            else
            {
                // Dropped packet: count against all admitted packets with worse priority
                foreach (var (order, rank) in orderToRank)
                {
                    if (rank > solution.Ranks[pid])
                    {
                        numInv += 1;
                    }
                }
            }

            return numInv;
        }

        /// <summary>
        /// Computes total inversion counts for both optimal and heuristic solutions.
        /// </summary>
        /// <param name="optimalSol">The optimal (SP-PIFO) scheduling solution.</param>
        /// <param name="heuristicSol">The heuristic (AIFO) scheduling solution.</param>
        /// <param name="numPackets">Total number of packets in the sequence.</param>
        /// <returns>Tuple of (optimal inversions, heuristic inversions).</returns>
        private static (int optimal, int heuristic) ComputeInversions(
            PIFOOptimizationSolution optimalSol,
            PIFOOptimizationSolution heuristicSol,
            int numPackets)
        {
            // Build order-to-rank mappings for admitted packets
            var orderToRankOpt = new Dictionary<int, double>();
            var orderToRankHeu = new Dictionary<int, double>();

            for (int pid = 0; pid < numPackets; pid++)
            {
                if (optimalSol.Admit[pid] == 1)
                {
                    orderToRankOpt[optimalSol.Order[pid]] = optimalSol.Ranks[pid];
                }

                if (heuristicSol.Admit[pid] == 1)
                {
                    orderToRankHeu[heuristicSol.Order[pid]] = heuristicSol.Ranks[pid];
                }
            }

            // Sum inversions across all packets
            int numInvOpt = 0;
            int numInvHeu = 0;

            for (int pid = 0; pid < numPackets; pid++)
            {
                numInvOpt += ComputeInversionNum(optimalSol, orderToRankOpt, pid);
                numInvHeu += ComputeInversionNum(heuristicSol, orderToRankHeu, pid);
            }

            return (numInvOpt, numInvHeu);
        }

        /// <summary>
        /// Creates the appropriate PIFO encoder based on method and packet drop settings.
        /// </summary>
        /// <param name="method">The PIFO scheduling method to use.</param>
        /// <param name="solver">The solver instance.</param>
        /// <param name="opts">CLI options containing encoder parameters.</param>
        /// <returns>Configured encoder implementing IEncoder interface.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when AIFO is used without packet drop, or ModifiedSPPIFO is used with packet drop.
        /// </exception>
        private static IEncoder<TVar, TSolution> CreateEncoder<TVar, TSolution>(
            PIFOMethodChoice method,
            ISolver<TVar, TSolution> solver,
            CliOptions opts)
        {
            return method switch
            {
                PIFOMethodChoice.PIFO => opts.ConsiderPktDrop
                    ? new PIFOWithDropAvgDelayEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.MaxRank, opts.MaxQueueSize)
                    : new PIFOAvgDelayOptimalEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.MaxRank),

                PIFOMethodChoice.SPPIFO => opts.ConsiderPktDrop
                    ? new SPPIFOWithDropAvgDelayEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.NumQueues, opts.MaxRank, opts.MaxQueueSize)
                    : new SPPIFOAvgDelayEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.NumQueues, opts.MaxRank),

                PIFOMethodChoice.AIFO => opts.ConsiderPktDrop
                    ? new AIFOAvgDelayEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.MaxRank, opts.MaxQueueSize, opts.WindowSize, opts.BurstParam)
                    : throw new ArgumentException(
                        "AIFO only works on shallow buffers and decides whether to admit or drop the packet. " +
                        "As a result, it does not apply to cases where we do not want packet drop."),

                PIFOMethodChoice.ModifiedSPPIFO => !opts.ConsiderPktDrop
                    ? new ModifiedSPPIFOAvgDelayEncoder<TVar, TSolution>(solver, opts.NumPackets, opts.SplitQueue, opts.NumQueues, opts.SplitRank, opts.MaxRank)
                    : throw new ArgumentException(
                        "ModifiedSPPIFO does not support packet drop yet."),

                _ => throw new ArgumentException($"Unknown PIFO method: {method}")
            };
        }

        /// <summary>
        /// Runs PIFO packet scheduling adversarial optimization.
        /// </summary>
        /// <param name="opts">CLI options.</param>
        /// <exception cref="ArgumentException">Thrown for unsupported solver.</exception>
        public static void Run(CliOptions opts)
        {
            switch (opts.SolverChoice)
            {
                case SolverChoice.OrTools:
                    RunWithSolver(new ORToolsSolver(), opts);
                    break;
                case SolverChoice.Zen:
                    RunWithSolver(new SolverZen(), opts);
                    break;
                case SolverChoice.Gurobi:
                    RunWithSolver(new GurobiSOS(timeout: opts.Timeout, verbose: Convert.ToInt32(opts.Verbose)), opts);
                    break;
                default:
                    throw new ArgumentException($"Unsupported solver: {opts.SolverChoice}. Valid options: OrTools, Gurobi, Zen");
            }
        }

        /// <summary>
        /// Runs PIFO packet scheduling adversarial optimization with the specified solver.
        /// </summary>
        /// <remarks>
        /// Creates encoders for the selected PIFO methods, then uses adversarial
        /// optimization to find packet rank sequences that maximize the cost gap.
        ///
        /// Key parameters from opts:
        /// - NumPackets: Total packets in sequence
        /// - MaxRank: Maximum priority rank value
        /// - NumQueues: Number of queues for SP-PIFO
        /// - MaxQueueSize: Maximum packets per queue
        /// - WindowSize: AIFO admission window size
        /// - BurstParam: AIFO burst tolerance parameter.
        /// </remarks>
        private static void RunWithSolver<TVar, TSolution>(ISolver<TVar, TSolution> solver, CliOptions opts)
        {
            Console.WriteLine($"Packets: {opts.NumPackets}, Max Rank: {opts.MaxRank}, Queues: {opts.NumQueues}");
            Console.WriteLine($"Max Queue Size: {opts.MaxQueueSize}, Window Size: {opts.WindowSize}");
            Console.WriteLine($"PIFO Method 1: {opts.PIFOMethod1} (ConsiderPktDrop={opts.ConsiderPktDrop})");
            Console.WriteLine($"PIFO Method 2: {opts.PIFOMethod2} (ConsiderPktDrop={opts.ConsiderPktDrop})");

            // Create encoders based on CLI options
            var h1 = CreateEncoder(opts.PIFOMethod1, solver, opts);
            var h2 = CreateEncoder(opts.PIFOMethod2, solver, opts);

            // Create adversarial generator
            var adversarialGenerator = new PIFOAdversarialInputGenerator<TVar, TSolution>(
                opts.NumPackets, opts.MaxRank);

            var timer = Stopwatch.StartNew();

            // Find worst-case packet sequence
            var (optimalSolution, heuristicSolution) = adversarialGenerator.MaximizeOptimalityGap(
                h1, h2, verbose: opts.Verbose);

            timer.Stop();

            // Compute inversion metrics
            var (numInvOpt, numInvHeu) = ComputeInversions(
                optimalSolution, heuristicSolution, opts.NumPackets);

            // Display results
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"{opts.PIFOMethod1} cost: {optimalSolution.Cost}");
            Console.WriteLine($"{opts.PIFOMethod2} cost: {heuristicSolution.Cost}");
            Console.WriteLine($"Gap: {heuristicSolution.Cost - optimalSolution.Cost}");
            Console.WriteLine($"Inversions ({opts.PIFOMethod1}): {numInvOpt}");
            Console.WriteLine($"Inversions ({opts.PIFOMethod2}): {numInvHeu}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            // Verbose output: full solution details
            if (opts.Verbose)
            {
                Console.WriteLine($"\n{opts.PIFOMethod1} Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSolution, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine($"\n{opts.PIFOMethod2} Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    heuristicSolution, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}