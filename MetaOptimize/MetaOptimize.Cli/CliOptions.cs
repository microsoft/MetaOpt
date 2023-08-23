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
        [Option('f', "file", Required = true, HelpText = "The location of the topology file.")]
        public string TopologyFile { get; set; }

        /// <summary>
        /// The heuristic encoder to use.
        /// </summary>
        /// TODO: Is this specific to only the TE use-case? if so, is there a way to make a separate data structure for each heuristic example to make the code cleaner?
        [Option('h', "heuristic", Required = true, HelpText = "The heuristic encoder to use (Pop | DemandPinning | ExpectedPop).")]
        public Heuristic Heuristic { get; set; }

        /// <summary>
        /// The solver we want to use.
        /// </summary>
        [Option('c', "solver", Required = true, HelpText = "The solver that we want to use (Gurobi | Zen)")]
        public SolverChoice SolverChoice { get; set; }

        /// <summary>
        /// inner encoding (KKT or PrimalDual).
        /// </summary>
        [Option('e', "innerencoding", Default = InnerEncodingMethodChoice.KKT, HelpText = "Method to use for inner encoding.")]
        public InnerEncodingMethodChoice InnerEncoding { get; set; }

        /// <summary>
        /// adversarial generator (Encoding or Benders).
        /// </summary>
        /// TODO: the discription of this input is not clear, explain it more.
        [Option('e', "adversarialgen", Default = AdversarialGenMethodChoice.Encoding, HelpText = "Method to use for adversarial generator. The benders decomposition approach currently does not work.")]
        public AdversarialGenMethodChoice AdversarialGen { get; set; }

        /// <summary>
        /// demand list (only applies for PrimalDual).
        /// </summary>
        /// TODO: this terminology is too TE specific. Can you make it more general to apply to our other heuristics too? Also would be good to expand the comment.
        [Option('d', "demandlist", Default = "0", HelpText = "quantized list of demands (only applies to PrimalDual -- should separate value with ',' no space).")]
        public String DemandList { get; set; }

        /// <summary>
        /// whether to simplify the final solution or not.
        /// </summary>
        /// TODO: this is too vague. Explain what it means to simplify the solution.
        [Option('s', "simplify", Default = false, HelpText = "Whether to simplify the final solution or not")]
        public bool Simplify { get; set; }

        /// <summary>
        /// Timeout for gurobi solver.
        /// </summary>
        [Option('o', "timeout", Default = double.PositiveInfinity, HelpText = "gurobi solver terminates after the specified time")]
        public double Timeout { get; set; }

        /// <summary>
        /// terminates if no improvement after specified time.
        /// </summary>
        [Option('x', "timetoterminate", Default = -1, HelpText = "gurobi solver terminates if no improvement in best objective after the specified time (only applies to MIP)")]
        public double TimeToTerminateIfNoImprovement { get; set; }

        /// <summary>
        /// The number of pop slices to use.
        /// </summary>
        /// TODO: again this is specific to a particular heuristic, is there a way to separate inputs that are heuristic specific from those that are general?
        /// One way may be to take a json as input that contains the inputs that are specific to the particular heuristic.
        [Option('s', "slices", Default = 2, HelpText = "The number of pop slices to use.")]
        public int PopSlices { get; set; }

        /// <summary>
        /// The threshold for demand pinning.
        /// </summary>
        /// TODO: same as other comments that are about being heuristic specific.
        [Option('t', "pinthreshold", Default = 5, HelpText = "The threshold for the demand pinning heuristic.")]
        public double DemandPinningThreshold { get; set; }

        /// <summary>
        /// The maximum number of paths to use for a demand.
        /// </summary>
        /// TODO: same as others.
        [Option('p', "paths", Default = 2, HelpText = "The maximum number of paths to use for any demand.")]
        public int Paths { get; set; }

        /// <summary>
        /// The maximum shortest path length to pin in modified demandpinning.
        /// </summary>
        /// TODO: same as others.
        [Option('p', "maxshortestlen", Default = -1, HelpText = "The maximum shortest path length to pin (only applied to ModifiedDp).")]
        public int MaxShortestPathLen { get; set; }

        /// <summary>
        /// method for finding gap [search or direct].
        /// </summary>
        /// TODO: expand on the comment to describe what each option does.
        [Option('m', "method", Default = MethodChoice.Direct, HelpText = "the method for finding the desirable gap [Direct | Search | FindFeas | Random | HillClimber | SimulatedAnnealing]")]
        public MethodChoice Method { get; set; }

        /// <summary>
        /// if using search, shows how much close to optimal is ok.
        /// </summary>
        [Option('d', "confidence", Default = 0.1, HelpText = "if using search, will find a solution as close as this value to optimal.")]
        public double Confidencelvl { get; set; }

        /// <summary>
        /// if using search, this values is used as the starting gap.
        /// </summary>
        [Option('g', "startinggap", Default = 10, HelpText = "if using search, will start the search from this number.")]
        public double StartingGap { get; set; }

        /// <summary>
        /// an upper bound on all the demands to find more useful advers inputs.
        /// </summary>
        /// TODO: is this also heuristic specific? if not, change the terminology to be general if yes, fix as stated above.
        [Option('u', "demandub", Default = -1, HelpText = "an upper bound on all the demands.")]
        public double DemandUB { get; set; }

        /// <summary>
        /// an upper bound on all the demands to find more useful advers inputs.
        /// </summary>
        /// TODO: same as above.
        [Option('x', "partitionSensitivity", Default = -1, HelpText = "the difference of total demands in each partition.")]
        public double PartitionSensitivity { get; set; }

        /// <summary>
        /// number of trails for random search.
        /// </summary>
        [Option('n', "num", Default = 1, HelpText = "number of trials for random search or hill climber.")]
        public int NumRandom { get; set; }

        /// <summary>
        /// number of neighbors to look.
        /// </summary>
        [Option('k', "neighbors", Default = 1, HelpText = "number of neighbors to search before marking as local optimum [for hill climber | simulated annealing].")]
        public int NumNeighbors { get; set; }

        /// <summary>
        /// initial temperature for simulated annealing.
        /// </summary>
        [Option('t', "inittmp", Default = 1, HelpText = "initial temperature for simulated annealing.")]
        public double InitTmp { get; set; }

        /// <summary>
        /// initial temperature for simulated annealing.
        /// </summary>
        [Option('l', "lambda", Default = 1, HelpText = "temperature decrease factor for simulated annealing.")]
        public double TmpDecreaseFactor { get; set; }

        /// <summary>
        /// max density of final traffic matrix.
        /// </summary>
        /// TODO: same as other comments.
        [Option('d', "maxdensity", Default = 1.0, HelpText = "maximum density of the final traffic demand.")]
        public double MaxDensity { get; set; }

        /// <summary>
        /// max distance for large demands.
        /// </summary>
        /// TODO: same as other comments.
        [Option('m', "maxdistancelarge", Default = -1, HelpText = "maximum distance for large demands.")]
        public int maxLargeDistance { get; set; }

        /// <summary>
        /// max distance for small demands.
        /// </summary>
        /// TODO: same as other comments.
        [Option('m', "maxdistancesmall", Default = -1, HelpText = "maximum distance for small demands.")]
        public int maxSmallDistance { get; set; }

        /// <summary>
        /// Lower bound for large demands.
        /// </summary>
        /// TODO: same as other comments.
        [Option('m', "largedemandlb", Default = -1, HelpText = "to distinguish large demands from small demands.")]
        public double LargeDemandLB { get; set; }

        /// <summary>
        /// enable clustering breakdown.
        /// </summary>
        [Option('c', "enableclustering", Default = false, HelpText = "Use this input to enable clustering. Clustering is more scalable but does not find the best possible gap.")]
        public bool EnableClustering { get; set; }

        /// <summary>
        /// cluster directory.
        /// </summary>
        /// TODO: not clear what this is doing, need a better user-visible and also private commment.
        [Option('j', "clusterdir", Default = null, HelpText = "cluster lvl topo directory")]
        public string ClusterDir { get; set; }

        /// <summary>
        /// num clusters.
        /// </summary>
        [Option('j', "numclusters", Default = null, HelpText = "number of clusters")]
        public int NumClusters { get; set; }

        /// <summary>
        /// inter-cluster method version.
        /// </summary>
        /// TODO: what are the options? what is the difference between the different options?
        [Option('j', "clusterversion", Default = 1, HelpText = "version of clustering for inter-cluster demands")]
        public int ClusterVersion { get; set; }

        /// <summary>
        /// num inter-cluster samples.
        /// </summary>
        [Option('j', "interclustersamples", Default = 0, HelpText = "number of inter cluster samples")]
        public int NumInterClusterSamples { get; set; }

        /// <summary>
        /// num nodes per cluster for inter-cluster edges.
        /// </summary>
        [Option('j', "nodespercluster", Default = 0, HelpText = "number of nodes per cluster for inter-cluster edges")]
        public int NumNodesPerCluster { get; set; }

        /// <summary>
        /// inter-cluster quantization lvls.
        /// </summary>
        /// TODO: unclear how one should use this parameter.
        [Option('j', "numinterclusterquantization", Default = -1, HelpText = "inter-cluster demands number of quantizations [only works for v3].")]
        public int NumInterClusterQuantizations { get; set; }

        /// <summary>
        /// error analysis.
        /// </summary>
        /// TODO: not fully clear what this does, needs a better comment both internally and user-visible.
        [Option('j', "fullopt", Default = false, HelpText = "after finding the demand, will run the full optimization with demands as init point.")]
        public bool FullOpt { get; set; }

        /// <summary>
        /// error analysis timer.
        /// </summary>
        [Option('j', "fullopttimer", Default = -1, HelpText = "the duration to run the full optimization for error analysis.")]
        public double FullOptTimer { get; set; }

        /// <summary>
        /// gurobi improve upper bound instead of bestObj.
        /// </summary>
        [Option('j', "ubfocus", Default = false, HelpText = "if enabled after finding demand, will solve another optimization focusing on improving upper bound.")]
        public bool UBFocus { get; set; }

        /// <summary>
        /// gurobi upper bound timer.
        /// </summary>
        [Option('j', "ubfocustimeout", Default = -1, HelpText = "the timer of solver when focusing on ub.")]
        public double UBFocusTimer { get; set; }

        /// <summary>
        /// num processes.
        /// </summary>
        /// TODO: for what?
        [Option('s', "numProcesses", Default = -1, HelpText = "num processes to use for.")]
        public int NumProcesses { get; set; }

        /// <summary>
        /// seed.
        /// </summary>
        [Option('s', "seed", Default = 1, HelpText = "seed for random generator.")]
        public int Seed { get; set; }

        /// <summary>
        /// seed.
        /// </summary>
        [Option('b', "stddev", Default = 100, HelpText = "standard deviation for generating neighbor for hill climber.")]
        public int StdDev { get; set; }

        /// <summary>
        /// store trajectory.
        /// </summary>
        [Option('m', "storeprogress", Default = false, HelpText = "store the progress for the specified approach.")]
        public bool StoreProgress { get; set; }

        /// <summary>
        /// file to read paths.
        /// </summary>
        /// TODO: heuristic specific. It would also be good to specify what format the file has to have.
        [Option('m', "pathfile", Default = null, HelpText = "file to read the paths from.")]
        public string PathFile { get; set; }

        /// <summary>
        /// log file.
        /// </summary>
        [Option('t', "logfile", Default = null, HelpText = "path to the log file to store the progress.")]
        public string LogFile { get; set; }

        /// <summary>
        /// Whether to print debugging information.
        /// </summary>
        [Option('d', "debug", Default = false, HelpText = "Prints debugging messages to standard output.")]
        public bool Debug { get; set; }

        /// <summary>
        /// To downscale the solver.
        /// </summary>
        [Option('p', "downscale", Default = 1.0, HelpText = "Factor to downscale MetaOpt.")]
        public double DownScaleFactor { get; set; }

        /// <summary>
        /// number of threads to use in gurobi.
        /// </summary>
        [Option('t', "gurobithreads", Default = 0, HelpText = "number of threads to use for Gurobi.")]
        public int NumGurobiThreads { get; set; }

        /// <summary>
        /// to show more detailed logs.
        /// </summary>
        [Option('v', "verbose", Default = false, HelpText = "more detailed logs")]
        public bool Verbose { get; set; }
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
        /// The average pop heuristic over multiple sample
        /// </summary>
        ExpectedPop,

        /// <summary>
        /// The threshold heuristic.
        /// </summary>
        DemandPinning,

        /// <summary>
        /// Combine POP and DP.
        /// </summary>
        PopDp,

        /// <summary>
        /// The average gap of running POP and DP in parallel.
        /// </summary>
        ExpectedPopDp,

        /// <summary>
        /// Parallel POP. Running multiple instances of POP in parallel.
        /// </summary>
        ParallelPop,

        /// <summary>
        /// Running multiple instances of POP in parallel with DP.
        /// </summary>
        ParallelPopDp,

        /// <summary>
        /// Modified Demand Pinning with upper bound of pinned path lengths.
        /// </summary>
        ModifiedDp,
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
        /// <summary>
        /// find a solution with gap at least equal to startinggap.
        /// </summary>
        FindFeas,
        /// <summary>
        /// find a solution with random search.
        /// </summary>
        Random,
        /// <summary>
        /// find a solution with hill climber.
        /// </summary>
        HillClimber,
        /// <summary>
        /// find a solution with simulated annealing.
        /// </summary>
        SimulatedAnnealing,
    }
}
