// <copyright file="CliOptions.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using CommandLine;

    /// <summary>
    /// The CLI command line arguments.
    /// </summary>
    public class CliOptions
    {
        /// <summary>
        /// The instance of the command line arguments.
        /// </summary>
        public static CliOptions Instance { get; set; }

        /// <summary>
        /// The topology file path.
        /// </summary>
        [Option('f', "file", Required = true, HelpText = "Topology input file to be processed.")]
        public string TopologyFile { get; set; }

        /// <summary>
        /// The heuristic encoder to use.
        /// </summary>
        [Option('h', "heuristic", Required = true, HelpText = "The heuristic encoder to use (Pop | Threshold).")]
        public Heuristic Heuristic { get; set; }

        /// <summary>
        /// The number of pop slices to use.
        /// </summary>
        [Option('s', "slices", Default = 2, HelpText = "The number of pop slices to use.")]
        public int PopSlices { get; set; }

        /// <summary>
        /// The maximum number of paths to use for a demand.
        /// </summary>
        [Option('p', "paths", Default = 2, HelpText = "The maximum number of paths to use for any demand.")]
        public int Paths { get; set; }

        /// <summary>
        /// Whether to print debugging information.
        /// </summary>
        [Option('d', "debug", Default = false, HelpText = "Prints debugging messages to standard output.")]
        public bool Debug { get; set; }
    }

    /// <summary>
    /// The encoding heuristic.
    /// </summary>
    public enum Heuristic
    {
        /// <summary>
        /// The pop heuristic.
        /// </summary>
        Pop,

        /// <summary>
        /// The threshold heuristic.
        /// </summary>
        Threshold,
    }
}
