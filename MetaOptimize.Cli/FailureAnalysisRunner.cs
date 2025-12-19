// <copyright file="FailureAnalysisRunner.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System.Diagnostics;
    using Google.OrTools.LinearSolver;
    using Gurobi;
    using MetaOptimize.FailureAnalysis;
    using ZenLib;
    using ZenLib.ModelChecking;

    /// <summary>
    /// Runner for network failure analysis adversarial optimization.
    /// Finds failure scenarios that maximize the gap between normal and degraded network performance.
    /// </summary>
    /// <remarks>
    /// Analyzes network resilience by finding worst-case link failure combinations.
    /// Compares optimal routing under normal conditions against routing under failure scenarios,
    /// identifying vulnerabilities where failures cause significant throughput degradation.
    ///
    /// Supports configurable failure probability thresholds and multiple simultaneous failures.
    /// </remarks>
    public static class FailureAnalysisRunner
    {
        /// <summary>
        /// Runs failure analysis adversarial optimization.
        /// Dispatches to the appropriate solver-specific implementation.
        /// </summary>
        /// <param name="opts">Command-line options containing failure analysis parameters.</param>
        /// <exception cref="Exception">Thrown when an unsupported solver is specified.</exception>
        public static void Run(CliOptions opts)
        {
            switch (opts.SolverChoice)
            {
                case SolverChoice.OrTools:
                    FailureAnalysisRunnerImpl<Variable, Solver>.CreateSolver = () => new ORToolsSolver();
                    FailureAnalysisRunnerImpl<Variable, Solver>.Run(opts);
                    break;
                case SolverChoice.Gurobi:
                    FailureAnalysisRunnerImpl<GRBVar, GRBModel>.CreateSolver =
                        () => new GurobiSOS(verbose: Convert.ToInt32(opts.Verbose), timeout: opts.Timeout);
                    FailureAnalysisRunnerImpl<GRBVar, GRBModel>.Run(opts);
                    break;
                case SolverChoice.Zen:
                    FailureAnalysisRunnerImpl<Zen<Real>, ZenSolution>.CreateSolver =
                        () => new SolverZen();
                    FailureAnalysisRunnerImpl<Zen<Real>, ZenSolution>.Run(opts);
                    break;
                default:
                    throw new Exception($"Unsupported solver: {opts.SolverChoice}. Valid options: OrTools, Gurobi, Zen");
            }
        }
    }

    /// <summary>
    /// Generic implementation of failure analysis adversarial optimization.
    /// </summary>
    /// <typeparam name="TVar">Solver variable type (GRBVar or Zen).</typeparam>
    /// <typeparam name="TSolution">Solver solution type (GRBModel or ZenSolution).</typeparam>
    /// <remarks>
    /// Uses bilevel optimization to find:
    /// 1. Outer level: Failure scenario (which links fail)
    /// 2. Inner level: Optimal routing under that failure scenario
    ///
    /// The gap represents how much throughput is lost due to the failure.
    /// </remarks>
    internal sealed class FailureAnalysisRunnerImpl<TVar, TSolution>
    {
        /// <summary>
        /// Factory function to create solver instances.
        /// Set by the dispatcher before calling Run().
        /// </summary>
        internal static Func<ISolver<TVar, TSolution>> CreateSolver = null;

        /// <summary>
        /// Creates a default 4-node diamond topology for testing.
        /// </summary>
        /// <returns>A simple test topology with nodes a, b, c, d.</returns>
        /// <remarks>
        /// Topology structure:
        ///      a
        ///     /|\
        ///    / | \
        ///   b--+--c
        ///    \ | /
        ///     \|/
        ///      d
        ///
        /// Link capacities vary to create interesting failure scenarios.
        /// </remarks>
        private static Topology CreateDefaultFailureTopology()
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
            topology.AddEdge("a", "d", capacity: 5);  // Direct path with lower capacity
            topology.AddEdge("b", "c", capacity: 3);  // Cross-link with lowest capacity
            return topology;
        }

        /// <summary>
        /// Runs failure analysis adversarial optimization.
        /// </summary>
        /// <param name="opts">Command-line options containing failure analysis parameters.</param>
        /// <remarks>
        /// Key parameters from opts:
        /// - UseDefaultTopology: Use built-in test topology or load from file
        /// - MaxNumFailures: Maximum simultaneous link failures to consider
        /// - NumExtraPaths: Additional paths for rerouting under failures
        /// - FailureProbThreshold: Minimum probability for considering a failure
        /// - InnerEncoding: KKT or PrimalDual for inner optimization
        /// - DemandList: Quantized demand levels for optimization.
        /// </remarks>
        internal static void Run(CliOptions opts)
        {
            Console.WriteLine($"Max Failures: {opts.MaxNumFailures}, Extra Paths: {opts.NumExtraPaths}");
            Console.WriteLine($"Failure Prob Threshold: {opts.FailureProbThreshold}");

            // Load or create topology
            Topology topology;
            List<Topology> clusters = null;
            if (opts.UseDefaultTopology)
            {
                topology = CreateDefaultFailureTopology();
                Console.WriteLine("Using default test topology");
            }
            else
            {
                (topology, clusters) = CliUtils.getTopology(
                opts.TopologyFile,
                opts.PathFile,
                opts.DownScaleFactor,
                opts.EnableClustering,
                opts.NumClusters,
                opts.ClusterDir,
                opts.Verbose);
                Console.WriteLine($"Loaded topology from: {opts.TopologyFile}");
            }

            // Default demand matrix for test topology
            var demands = new Dictionary<(string, string), double>
            {
                { ("a", "d"), 10 },
                { ("b", "d"), 5 },
                { ("a", "c"), 5 },
                { ("c", "d"), 0 },
                { ("a", "b"), 0 },
                { ("b", "c"), 0 },
            };

            // Parse demand quantization levels
            var demandSet = new HashSet<double>(opts.DemandList.Split(',').Select(double.Parse));
            var demandList = new GenericList(demandSet);

            // Link failure probabilities for test topology
            var probs = new Dictionary<(string, string), double>
            {
                { ("a", "d"), 0.3 },  // Direct link has highest failure probability
                { ("b", "d"), 0.2 },
                { ("a", "c"), 0 },
                { ("a", "b"), 0 },
                { ("c", "d"), 0 },
                { ("b", "c"), 0 },
            };

            var solver = CreateSolver();

            var timer = Stopwatch.StartNew();

            // Create encoders for normal and failure scenarios
            var optimalEncoder = new TEMaxFlowOptimalEncoder<TVar, TSolution>(solver, maxNumPaths: 2);
            var failureEncoder = new FailureAnalysisEncoder<TVar, TSolution>(solver, maxNumPathTotal: 2);
            var adversarialGenerator = new FailureAnalysisAdversarialGenerator<TVar, TSolution>(
                topology, maxNumPaths: 2);

            // Find worst-case failure scenario
            var (optimalSol, failureSol) = adversarialGenerator.MaximizeOptimalityGap(
                optimalEncoder, failureEncoder,
                innerEncoding: opts.InnerEncoding,
                constrainedDemands: demands,
                maxNumFailures: opts.MaxNumFailures,
                demandList: demandList,
                numExtraPaths: opts.NumExtraPaths,
                lagFailureProbabilities: probs,
                failureProbThreshold: opts.FailureProbThreshold);

            timer.Stop();

            // Display results
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("RESULTS:");
            Console.WriteLine($"Optimal objective (no failures): {optimalSol.MaxObjective}");
            Console.WriteLine($"Failure scenario objective: {failureSol.MaxObjective}");
            Console.WriteLine($"Gap (throughput loss): {optimalSol.MaxObjective - failureSol.MaxObjective}");
            Console.WriteLine($"Time: {timer.ElapsedMilliseconds}ms");
            Console.WriteLine(new string('=', 60));

            // Verbose output: full solution details
            if (opts.Verbose)
            {
                Console.WriteLine("\nOptimal Solution (no failures):");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    optimalSol, Newtonsoft.Json.Formatting.Indented));
                Console.WriteLine("\nFailure Scenario Solution:");
                Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(
                    failureSol, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}