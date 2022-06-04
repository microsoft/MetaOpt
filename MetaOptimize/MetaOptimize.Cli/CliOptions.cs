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
        /// The solver we want to use.
        /// </summary>
        [Option('c', "solver choice", Required = true, HelpText = "The solver that we want to use (Gurobi | Zen | GurobiSearch)")]
        public SolverChoice SolverChoice { get; set; }

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
        /// method for finding gap [search or direct].
        /// </summary>
        [Option('m', "method", Default = MethodChoice.Direct, HelpText = "the method for finding the desirable gap [Direct (default) | Search]")]
        public MethodChoice Method { get; set; }

        /// <summary>
        /// if using search, shows how much close to optimal is ok.
        /// </summary>
        [Option('o', "confidence", Default = 0.1, HelpText = "if using search, will find a solution as close as this value to optimal. [Default=0.1]")]
        public double Confidencelvl { get; set; }

        /// <summary>
        /// if using search, this values is used as the starting gap.
        /// </summary>
        [Option('g', "startinggap", Default = 10, HelpText = "if using search, will start the search from this number. [Default=10]")]
        public double StartingGap { get; set; }

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
    /// <summary>
    /// The solver we want to use.
    /// </summary>
    public enum SolverChoice
    {
        /// <summary>
        /// The Gurobi solver.
        /// </summary>
        Gurobi,

        /// <summary>
        /// The Zen solver.
        /// </summary>
        Zen,

        /// <summary>
        /// GurobiSearch.
        /// </summary>
        GurobiSearch,
    }
    /// <summary>
    /// The method we want to use.
    /// </summary>
    public enum MethodChoice
    {
        /// <summary>
        /// directly find the max gap
        /// </summary>
        Direct,
        /// <summary>
        /// search for the max gap with some interval
        /// </summary>
        Search,
    }
}
