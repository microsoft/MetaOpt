// <copyright file="Program.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    /// <summary>
    /// Entry point for MetaOptimize CLI.
    /// Routes to different problem solvers based on command-line arguments.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Main entry point for the program.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Main(string[] args)
        {
            var cliArgs = new CliArgs(args);

            if (cliArgs.ShowHelp)
            {
                cliArgs.ShowHelpMessage();
                return;
            }

            if (!cliArgs.Validate())
            {
                Environment.Exit(1);
            }

            try
            {
                var problemType = cliArgs.ProblemType;
                Console.WriteLine($"Starting MetaOptimize - Problem Type: {problemType}");
                Console.WriteLine(new string('=', 60));

                switch (problemType)
                {
                    case "TrafficEngineering":
                        var mode = cliArgs.Get("--teMode", "advanced");
                        if (mode.ToLower() == "advanced")
                        {
                            TERunner.RunAdvanced(args);
                        }
                        else
                        {
                            TERunner.RunSimple(cliArgs);
                        }
                        break;

                    case "BinPacking":
                        BPRunner.Run(cliArgs);
                        break;

                    case "PIFO":
                        PIFORunner.Run(cliArgs);
                        break;

                    case "FailureAnalysis":
                        FailureAnalysisRunner.Run(cliArgs);
                        break;

                    default:
                        Console.WriteLine($"ERROR: Unknown problem type '{problemType}'");
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
