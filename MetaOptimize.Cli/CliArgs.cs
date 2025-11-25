// <copyright file="CliArgs.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Command-line argument parser and help system for MetaOptimize.
    /// Provides a unified interface for all problem types with sensible defaults.
    /// </summary>
    public class CliArgs
    {
        private readonly Dictionary<string, string> arguments = new Dictionary<string, string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="CliArgs"/> class.
        /// </summary>
        /// <param name="args">Command-line arguments array.</param>
        public CliArgs(string[] args)
        {
            this.ParseArguments(args);
        }

        /// <summary>
        /// Gets the problem type being solved.
        /// </summary>
        public string ProblemType => this.Get("--problemType", "BinPacking");

        /// <summary>
        /// Gets a value indicating whether to show help.
        /// </summary>
        public bool ShowHelp => this.Has("--help") || this.Has("-h");

        /// <summary>
        /// Gets argument value or returns default if not present.
        /// </summary>
        /// <param name="key">Argument name (e.g., "--timeout").</param>
        /// <param name="defaultValue">Default value if not specified.</param>
        /// <returns>Argument value or default.</returns>
        public string Get(string key, string defaultValue = "")
        {
            return this.arguments.ContainsKey(key) ? this.arguments[key] : defaultValue;
        }

        /// <summary>
        /// Checks if argument is present.
        /// </summary>
        /// <param name="key">Argument name.</param>
        /// <returns>True if argument exists.</returns>
        public bool Has(string key)
        {
            return this.arguments.ContainsKey(key);
        }

        /// <summary>
        /// Gets integer argument value.
        /// </summary>
        /// <param name="key">Argument name.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <returns>Integer value.</returns>
        public int GetInt(string key, int defaultValue = 0)
        {
            return int.TryParse(this.Get(key), out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Gets double argument value.
        /// </summary>
        /// <param name="key">Argument name.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <returns>Double value.</returns>
        public double GetDouble(string key, double defaultValue = 0.0)
        {
            return double.TryParse(this.Get(key), out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Gets boolean argument value.
        /// </summary>
        /// <param name="key">Argument name.</param>
        /// <param name="defaultValue">Default value.</param>
        /// <returns>Boolean value.</returns>
        public bool GetBool(string key, bool defaultValue = false)
        {
            if (!this.Has(key))
            {
                return defaultValue;
            }

            var value = this.Get(key).ToLower();
            return value == "true" || value == "1" || value == "yes" || string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Displays comprehensive help information for all problem types.
        /// </summary>
        public void ShowHelpMessage()
        {
            Console.WriteLine("MetaOptimize - Adversarial Input Generation Framework");
            Console.WriteLine("======================================================\n");
            Console.WriteLine("USAGE:");
            Console.WriteLine("  dotnet run --project MetaOptimize.Cli -- [OPTIONS]\n");
            Console.WriteLine("PROBLEM TYPES:");
            Console.WriteLine("  --problemType <type>     Problem to solve (required)");
            Console.WriteLine("                           - TrafficEngineering: Network flow optimization");
            Console.WriteLine("                           - BinPacking: Multi-dimensional bin packing");
            Console.WriteLine("                           - PIFO: Packet scheduling optimization");
            Console.WriteLine("                           - FailureAnalysis: Network failure scenario analysis\n");

            Console.WriteLine("COMMON OPTIONS:");
            Console.WriteLine("  --verbose <0|1>          Enable detailed output (default: 0)");
            Console.WriteLine("  --timeout <seconds>      Solver timeout in seconds (default: 1000)");
            Console.WriteLine("  --solver <type>          Solver choice: Gurobi, Zen (default: Gurobi)");
            Console.WriteLine("  --help, -h               Show this help message\n");

            this.ShowTrafficEngineeringHelp();
            this.ShowBinPackingHelp();
            this.ShowPIFOHelp();
            this.ShowFailureAnalysisHelp();

            Console.WriteLine("\nEXAMPLES:");
            Console.WriteLine("  # Traffic Engineering with demand pinning");
            Console.WriteLine("  dotnet run --project MetaOptimize.Cli -- --problemType TrafficEngineering \\");
            Console.WriteLine("    --topologyFile topology.json --heuristic DemandPinning --paths 2\n");
            Console.WriteLine("  # Bin Packing adversarial generation");
            Console.WriteLine("  dotnet run --project MetaOptimize.Cli -- --problemType BinPacking \\");
            Console.WriteLine("    --numBins 6 --numDemands 9 --numDimensions 2 --optimalBins 3\n");
            Console.WriteLine("  # PIFO packet scheduling");
            Console.WriteLine("  dotnet run --project MetaOptimize.Cli -- --problemType PIFO \\");
            Console.WriteLine("    --numPackets 18 --maxRank 8 --numQueues 4\n");
            Console.WriteLine("  # Failure Analysis");
            Console.WriteLine("  dotnet run --project MetaOptimize.Cli -- --problemType FailureAnalysis \\");
            Console.WriteLine("    --maxNumFailures 2 --numExtraPaths 1 --failureProbThreshold 0.1\n");
        }

        private void ShowTrafficEngineeringHelp()
        {
            Console.WriteLine("TRAFFIC ENGINEERING OPTIONS:");
            Console.WriteLine("  --teMode <mode>          Mode: simple (TEMain), advanced (ssMain) (default: simple)");
            Console.WriteLine("  --topologyFile <path>    Network topology JSON file (default: simple.json)");
            Console.WriteLine("  --pathFile <path>        Path configuration file");
            Console.WriteLine("  --heuristic <type>       Heuristic: DemandPinning, Pop, ExpectedPop, PopDp");
            Console.WriteLine("                           (default: DemandPinning)");
            Console.WriteLine("  --paths <num>            Number of paths per source-dest pair (default: 2)");
            Console.WriteLine("  --method <type>          Optimization method: Direct, Search, FindFeas,");
            Console.WriteLine("                           Random, HillClimber, SimulatedAnnealing (default: Direct)");
            Console.WriteLine("  --demandUB <value>       Upper bound for demands (default: 100.0)");
            Console.WriteLine("  --demandList <csv>       Comma-separated demand quantization levels");
            Console.WriteLine("                           (default: \"0,5,10,15,20\")");
            Console.WriteLine("  --dpThreshold <value>    Demand pinning threshold (default: 0.5)");
            Console.WriteLine("  --innerEncoding <type>   Inner encoding: PrimalDual, KKT (default: PrimalDual)");
            Console.WriteLine("  --downscale <factor>     Topology downscale factor (default: 1.0)");
            Console.WriteLine("  --numThreads <num>       Number of Gurobi threads (default: 1)");
            Console.WriteLine("  --enableClustering       Enable topology clustering");
            Console.WriteLine("  --numClusters <num>      Number of clusters (default: 2)");
            Console.WriteLine("  --clusterDir <path>      Directory for cluster files");
            Console.WriteLine("  --logFile <path>         Path to log file\n");
        }

        private void ShowBinPackingHelp()
        {
            Console.WriteLine("BIN PACKING OPTIONS:");
            Console.WriteLine("  --numBins <num>          Number of bins (default: 6)");
            Console.WriteLine("  --numDemands <num>       Number of items to pack (default: 9)");
            Console.WriteLine("  --numDimensions <num>    Number of dimensions (default: 2)");
            Console.WriteLine("  --binCapacity <csv>      Comma-separated bin capacities per dimension");
            Console.WriteLine("                           (default: \"1.00001,1.00001\")");
            Console.WriteLine("  --optimalBins <num>      Expected optimal number of bins (default: 3)");
            Console.WriteLine("  --ffdMethod <type>       FFD method: FFDSum, FFDDimension (default: FFDSum)");
            Console.WriteLine("  --breakSymmetry          Enable symmetry breaking (default: false)\n");
        }

        private void ShowPIFOHelp()
        {
            Console.WriteLine("PIFO OPTIONS:");
            Console.WriteLine("  --numPackets <num>       Number of packets (default: 18)");
            Console.WriteLine("  --maxRank <num>          Maximum rank value (default: 8)");
            Console.WriteLine("  --numQueues <num>        Number of queues for SP-PIFO (default: 4)");
            Console.WriteLine("  --maxQueueSize <num>     Maximum queue size (default: 12)");
            Console.WriteLine("  --windowSize <num>       AIFO window size (default: 12)");
            Console.WriteLine("  --burstParam <value>     Burst parameter for AIFO (default: 0.1)\n");
        }

        private void ShowFailureAnalysisHelp()
        {
            Console.WriteLine("FAILURE ANALYSIS OPTIONS:");
            Console.WriteLine("  --topologyFile <path>    Network topology JSON file (required for custom topology)");
            Console.WriteLine("  --useDefaultTopology     Use built-in test topology (default: true)");
            Console.WriteLine("  --maxNumFailures <num>   Maximum number of link failures (default: 1)");
            Console.WriteLine("  --numExtraPaths <num>    Number of extra paths (default: 1)");
            Console.WriteLine("  --demandList <csv>       Comma-separated demand levels (default: \"0,5,10\")");
            Console.WriteLine("  --failureProbThreshold <value>   Failure probability threshold (default: 0.25)");
            Console.WriteLine("  --scenarioProbThreshold <value>  Scenario probability threshold");
            Console.WriteLine("  --innerEncoding <type>   Inner encoding: PrimalDual, KKT (default: PrimalDual)\n");
        }

        private void ParseArguments(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("--") || args[i].StartsWith("-"))
                {
                    string key = args[i];
                    string value = string.Empty;

                    // Check if next argument is a value (doesn't start with --)
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && !args[i + 1].StartsWith("-"))
                    {
                        value = args[i + 1];
                        i++; // Skip next argument since we consumed it
                    }

                    this.arguments[key] = value;
                }
            }
        }

        /// <summary>
        /// Validates that required arguments are present for the selected problem type.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        public bool Validate()
        {
            if (this.ShowHelp)
            {
                return true;
            }

            var problemType = this.ProblemType;
            var validProblemTypes = new[] { "TrafficEngineering", "BinPacking", "PIFO", "FailureAnalysis" };

            if (!validProblemTypes.Contains(problemType))
            {
                Console.WriteLine($"ERROR: Invalid problem type '{problemType}'");
                Console.WriteLine($"Valid types: {string.Join(", ", validProblemTypes)}");
                Console.WriteLine("Use --help for more information.");
                return false;
            }

            // Problem-specific validation
            switch (problemType)
            {
                case "FailureAnalysis":
                    if (!this.GetBool("--useDefaultTopology", true) && !this.Has("--topologyFile"))
                    {
                        Console.WriteLine("ERROR: --topologyFile is required when not using default topology");
                        return false;
                    }
                    break;
            }

            return true;
        }
    }
}
