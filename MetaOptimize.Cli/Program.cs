// <copyright file="Program.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using CommandLine;

    /// <summary>
    /// Entry point for MetaOptimize CLI.
    /// Routes to different problem solvers based on command-line arguments.
    /// </summary>
    /// <remarks>
    /// Supports four problem types:
    /// - TrafficEngineering: Find worst-case demand patterns for routing heuristics
    /// - BinPacking: Find adversarial item sizes that maximize FFD vs optimal gap
    /// - PIFO: Find packet sequences that maximize scheduling inversions
    /// - FailureAnalysis: Analyze network resilience under link failures
    ///
    /// Uses CommandLineParser for argument parsing with CliOptions.
    /// </remarks>
    public class Program
    {
        /// <summary>
        /// Main entry point for the program.
        /// Parses command-line arguments and dispatches to the appropriate runner.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            var parseResult = CommandLine.Parser.Default.ParseArguments<CliOptions>(args);

            parseResult.WithParsed(opts =>
            {
                CliOptions.Instance = opts;
                RunWithOptions(opts);
            });

            parseResult.WithNotParsed(errors =>
            {
                // CommandLineParser prints help/errors automatically
                Environment.Exit(1);
            });
        }

        /// <summary>
        /// Executes the appropriate runner based on the problem type.
        /// </summary>
        /// <param name="opts">Parsed command-line options.</param>
        private static void RunWithOptions(CliOptions opts)
        {
            try
            {
                // Debug output
                if (opts.Verbose)
                {
                    Console.WriteLine($"[DEBUG] UseDefaultTopology: {opts.UseDefaultTopology}");
                    Console.WriteLine($"[DEBUG] BreakSymmetry: {opts.BreakSymmetry}");
                    Console.WriteLine($"[DEBUG] EnableClustering: {opts.EnableClustering}");
                    Console.WriteLine($"[DEBUG] Verbose: {opts.Verbose}");
                    Console.WriteLine($"[DEBUG] Debug: {opts.Debug}");
                }

                switch (opts.ProblemType)
                {
                    case ProblemType.TrafficEngineering:
                        TERunner.Run(opts);
                        break;

                    case ProblemType.BinPacking:
                        BPRunner.Run(opts);
                        break;

                    case ProblemType.PIFO:
                        PIFORunner.Run(opts);
                        break;

                    case ProblemType.FailureAnalysis:
                        FailureAnalysisRunner.Run(opts);
                        break;

                    default:
                        Console.WriteLine($"ERROR: Unknown problem type '{opts.ProblemType}'");
                        Environment.Exit(1);
                        break;
                }

                Console.WriteLine(new string('=', 60));
                Console.WriteLine("Execution completed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nERROR: {ex.Message}");
                Console.WriteLine($"Stack Trace:\n{ex.StackTrace}");
                Environment.Exit(1);
            }
        }
    }
}