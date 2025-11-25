using System.Diagnostics;
using Gurobi;

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Main entry point for traffic engineering experiments.
    /// Executes adversarial optimization to find worst-case demand patterns for routing heuristics.
    /// </summary>
    /// <remarks>
    /// Flow: Load topology → Generate demand levels → Run adversarial optimization → Validate results.
    /// Finds demand patterns where heuristic routes significantly less than optimal.
    /// </remarks>
    public sealed class PIFORunner
    {
        /// <summary>
        /// Computes inversion count for a single packet.
        /// </summary>
        private static int ComputeInversionNum(
            PIFOOptimizationSolution solution,
            Dictionary<int, double> orderToRank,
            int pid)
        {
            int numInv = 0;

            if (solution.Admit[pid] >= 0.98)
            {
                int currOrder = solution.Order[pid];
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
        /// Computes inversion count for PIFO solutions.
        /// </summary>
        private static (int optimal, int heuristic) ComputeInversions(
            PIFOOptimizationSolution optimalSol,
            PIFOOptimizationSolution heuristicSol,
            int numPackets)
        {
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
        /// Runs Bin Packing optimization.
        /// Uses MainVBP logic (more complete than vbMain).
        /// </summary>
        public static void Run(CliArgs args)
        {
            var maxRank = args.GetInt("--maxRank", 8);
            var numPackets = args.GetInt("--numPackets", 18);
            var numQueues = args.GetInt("--numQueues", 4);
            var maxQueueSize = args.GetInt("--maxQueueSize", 12);
            var windowSize = args.GetInt("--windowSize", 12);
            var burstParam = args.GetDouble("--burstParam", 0.1);
            var timeout = args.GetDouble("--timeout", 1000);
            var verbose = args.GetBool("--verbose", false);

            Console.WriteLine($"Packets: {numPackets}, Max Rank: {maxRank}, Queues: {numQueues}");
            Console.WriteLine($"Max Queue Size: {maxQueueSize}, Window Size: {windowSize}");

            var solver = new GurobiSOS(verbose: Convert.ToInt32(verbose), timeout: timeout);

            // Create encoders - comparing SP-PIFO with drop vs AIFO
            var h1 = new SPPIFOWithDropAvgDelayEncoder<GRBVar, GRBModel>(
                solver, numPackets, numQueues, maxRank, maxQueueSize);
            var h2 = new AIFOAvgDelayEncoder<GRBVar, GRBModel>(
                solver, numPackets, maxRank, maxQueueSize, windowSize, burstParam);

            var adversarialGenerator = new PIFOAdversarialInputGenerator<GRBVar, GRBModel>(
                numPackets, maxRank);

            var timer = Stopwatch.StartNew();
            var (optimalSolution, heuristicSolution) = adversarialGenerator.MaximizeOptimalityGap(
                h1, h2, verbose: verbose);
            timer.Stop();

            // Compute inversions
            var (numInvOpt, numInvHeu) = ComputeInversions(optimalSolution, heuristicSolution, numPackets);

            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal cost: {optimalSolution.Cost}");
            Console.WriteLine($"Heuristic cost: {heuristicSolution.Cost}");
            Console.WriteLine($"Gap: {heuristicSolution.Cost - optimalSolution.Cost}");
            Console.WriteLine($"Inversions (Optimal): {numInvOpt}");
            Console.WriteLine($"Inversions (Heuristic): {numInvHeu}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            if (verbose)
            {
                Console.WriteLine("\nOptimal Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSolution, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine("\nHeuristic Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    heuristicSolution, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}