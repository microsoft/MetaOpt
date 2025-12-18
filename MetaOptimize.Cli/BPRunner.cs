// <copyright file="BPRunner.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System.Diagnostics;
    using Gurobi;
    using ZenLib;
    using ZenLib.ModelChecking;

    /// <summary>
    /// Runner for Vector Bin Packing adversarial optimization.
    /// Finds item sizes that maximize the gap between optimal packing and First-Fit heuristics.
    /// </summary>
    /// <remarks>
    /// Flow: Configure bins → Create encoders → Run adversarial optimization → Display gap.
    ///
    /// Compares optimal bin packing solution against First-Fit variants (FF, FFDSum, FFDProd, FFDDiv).
    /// The adversarial generator finds item sizes where the heuristic uses significantly more bins
    /// than the optimal solution.
    ///
    /// Supports both Gurobi (MIP) and Zen (SMT) solvers via generic implementation.
    /// </remarks>
    public static class BPRunner
    {
        /// <summary>
        /// Runs Bin Packing adversarial optimization.
        /// Dispatches to the appropriate solver-specific implementation.
        /// </summary>
        /// <param name="opts">Command-line options containing bin packing parameters.</param>
        /// <exception cref="Exception">Thrown when an unsupported solver is specified.</exception>
        public static void Run(CliOptions opts)
        {
            switch (opts.SolverChoice)
            {
                case SolverChoice.OrTools:
                    RunBinPacking(new ORToolsSolver(), opts);
                    break;
                case SolverChoice.Zen:
                    RunBinPacking(new SolverZen(), opts);
                    break;
                case SolverChoice.Gurobi:
                    RunBinPacking(
                        new GurobiSOS(timeout: opts.Timeout, verbose: Convert.ToInt32(opts.Verbose)),
                        opts);
                    break;
                default:
                    throw new Exception($"Unsupported solver: {opts.SolverChoice}. Valid options: Gurobi, Zen");
            }
        }

        /// <summary>
        /// Generic implementation of bin packing adversarial optimization.
        /// </summary>
        /// <typeparam name="TVar">Solver variable type (GRBVar or Zen).</typeparam>
        /// <typeparam name="TSolution">Solver solution type (GRBModel or ZenSolution).</typeparam>
        /// <param name="solver">The solver instance to use.</param>
        /// <param name="opts">Command-line options containing bin packing parameters.</param>
        /// <remarks>
        /// Creates three components:
        /// 1. VBPOptimalEncoder: Encodes the optimal bin packing problem
        /// 2. FFDItemCentricEncoder: Encodes the First-Fit heuristic behavior
        /// 3. VBPAdversarialInputGenerator: Finds item sizes maximizing the gap
        ///
        /// The optimization finds item sizes such that:
        /// - Optimal solution uses exactly opts.OptimalBins bins
        /// - FFD heuristic uses as many bins as possible
        /// - Gap = FFD bins - Optimal bins is maximized.
        /// </remarks>
        private static void RunBinPacking<TVar, TSolution>(ISolver<TVar, TSolution> solver, CliOptions opts)
        {
            Console.WriteLine($"Bins: {opts.NumBins}, Items: {opts.NumDemands}, Dimensions: {opts.NumDimensions}");
            Console.WriteLine($"Target optimal bins: {opts.OptimalBins}");
            Console.WriteLine($"FF Method: {opts.FFMethod}");

            // Parse bin capacities from comma-separated string
            var binCapacities = opts.BinCapacity.Split(',').Select(double.Parse).ToList();

            // Pad capacities to match number of dimensions
            while (binCapacities.Count < opts.NumDimensions)
            {
                binCapacities.Add(1.00001);
            }

            // Create bin configuration
            var bins = new Bins(opts.NumBins, binCapacities);

            // Create optimal encoder - finds minimum bins needed
            var optimalEncoder = new VBPOptimalEncoder<TVar, TSolution>(
                solver, opts.NumDemands, opts.NumDimensions, BreakSymmetry: opts.BreakSymmetry);

            // Create FFD encoder - simulates First-Fit heuristic behavior
            var ffdEncoder = new FFDItemCentricEncoder<TVar, TSolution>(
                solver, opts.NumDemands, opts.NumDimensions);

            // Create adversarial generator - finds worst-case item sizes
            var adversarialGenerator = new VBPAdversarialInputGenerator<TVar, TSolution>(
                bins, opts.NumDemands, opts.NumDimensions);

            var timer = Stopwatch.StartNew();

            // Run bilevel optimization to find adversarial inputs
            List<IList<double>> demandList = null;
            var (optimalSolution, ffdSolution) = adversarialGenerator.MaximizeOptimalityGapFFD(
                optimalEncoder, ffdEncoder,
                opts.OptimalBins,
                ffdMethod: opts.FFMethod,
                itemList: demandList,
                verbose: opts.Verbose);

            timer.Stop();

            // Display results
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal bins used: {optimalSolution.TotalNumBinsUsed}");
            Console.WriteLine($"{opts.FFMethod} bins used: {ffdSolution.TotalNumBinsUsed}");
            Console.WriteLine($"Gap: {ffdSolution.TotalNumBinsUsed - optimalSolution.TotalNumBinsUsed}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            // Verbose output: full solution details as JSON
            if (opts.Verbose)
            {
                Console.WriteLine("\nOptimal Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSolution, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine("\nHeuristic Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    ffdSolution, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}