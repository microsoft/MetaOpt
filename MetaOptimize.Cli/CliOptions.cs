// <copyright file="CliOptions.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Cli
{
    using CommandLine;

    /// <summary>
    /// Command-line arguments for all MetaOptimize problem types.
    /// </summary>
    /// <remarks>
    /// Supports four problem types:
    /// - TrafficEngineering: Find worst-case demand patterns for routing heuristics
    /// - BinPacking: Find adversarial item sizes that maximize FFD vs optimal gap
    /// - PIFO: Find packet sequences that maximize scheduling inversions
    /// - FailureAnalysis: Analyze network resilience under link failures
    ///
    /// Parameters are organized by:
    /// - Common: Shared across all problem types
    /// - Traffic Engineering: TE-specific options (largest section, most mature)
    /// - Bin Packing: VBP-specific options
    /// - PIFO: Packet scheduling options
    /// - Failure Analysis: Network resilience options.
    /// </remarks>
    public class CliOptions
    {
        /// <summary>
        /// Singleton instance of the parsed command-line arguments.
        /// Set by Program.Main() after parsing.
        /// </summary>
        public static CliOptions Instance { get; set; }

        #region Common Options

        /// <summary>
        /// The problem type to solve.
        /// Determines which runner is invoked and which parameters are relevant.
        /// </summary>
        [Option("problemType", Default = ProblemType.FailureAnalysis, HelpText = "Problem type: TrafficEngineering, BinPacking, PIFO, FailureAnalysis")]
        public ProblemType ProblemType { get; set; }

        /// <summary>
        /// The solver to use for optimization.
        /// Gurobi uses MIP (Mixed Integer Programming), Zen uses SMT (Satisfiability Modulo Theories).
        /// </summary>
        [Option('c', "solver", Default = SolverChoice.OrTools, HelpText = "The solver to use (Gurobi | Zen)")]
        public SolverChoice SolverChoice { get; set; }

        /// <summary>
        /// Timeout for the solver in seconds.
        /// Solver terminates and returns best solution found after this time.
        /// </summary>
        [Option('o', "timeout", Default = double.PositiveInfinity, HelpText = "Solver timeout in seconds (default: no timeout)")]
        public double Timeout { get; set; }

        /// <summary>
        /// Enable verbose output for debugging.
        /// Shows detailed solver progress, intermediate solutions, and timing information.
        /// </summary>
        [Option('v', "verbose", Default = false, HelpText = "Enable verbose output with detailed logs")]
        public bool Verbose { get; set; }

        /// <summary>
        /// Enable debug output.
        /// Prints additional debugging messages to standard output.
        /// </summary>
        [Option('d', "debug", Default = false, HelpText = "Prints debugging messages to standard output")]
        public bool Debug { get; set; }

        #endregion

        #region Traffic Engineering Options

        /// <summary>
        /// The topology file path.
        /// JSON file containing network nodes and links with capacities.
        /// </summary>
        /// <remarks>
        /// Expected format:
        /// {
        ///   "nodes": [{"id": "a"}, {"id": "b"}, ...],
        ///   "links": [{"source": "a", "target": "b", "capacity": 10}, ...]
        /// }.
        /// </remarks>
        [Option('f', "topologyFile", Default = "..\\Topologies\\simple.json", HelpText = "The location of the topology file (JSON format)")]
        public string TopologyFile { get; set; }

        /// <summary>
        /// The heuristic encoder to use.
        /// </summary>
        /// <remarks>
        /// Available heuristics:
        /// - Pop: Partition-based routing with random demand partitioning
        /// - DemandPinning: Threshold-based path selection (pin large demands to shortest path)
        /// - ExpectedPop: Average performance across multiple partition samples
        /// - PopDp: Combined Pop and DemandPinning
        /// - ExpectedPopDp: Average gap of running Pop and DemandPinning in parallel
        /// - ParallelPop: Multiple Pop instances in parallel
        /// - ParallelPopDp: Multiple Pop instances with DemandPinning
        /// - ModifiedDp: DemandPinning with upper bound on pinned path lengths.
        /// </remarks>
        /// TODO: Is this specific to only the TE use-case? If so, is there a way to make a separate
        /// data structure for each heuristic example to make the code cleaner?
        [Option('h', "heuristic", Default = Heuristic.Pop, HelpText = "The heuristic encoder to use (Pop | DemandPinning | ExpectedPop | PopDp | ModifiedDp)")]
        public Heuristic Heuristic { get; set; }

        /// <summary>
        /// Inner encoding method for bilevel optimization.
        /// KKT uses Karush-Kuhn-Tucker conditions, PrimalDual uses primal-dual reformulation.
        /// </summary>
        /// <remarks>
        /// KKT: Converts inner optimization to constraints using optimality conditions.
        /// PrimalDual: Uses strong duality to reformulate inner problem.
        /// PrimalDual requires demand quantization (see DemandList).
        /// </remarks>
        [Option('e', "innerencoding", Default = InnerRewriteMethodChoice.KKT, HelpText = "Method for inner encoding (KKT | PrimalDual)")]
        public InnerRewriteMethodChoice InnerEncoding { get; set; }

        /// <summary>
        /// Adversarial generator method.
        /// </summary>
        /// TODO: The description of this input is not clear, explain it more.
        /// Encoding uses direct bilevel formulation, Benders uses decomposition (currently broken).
        [Option("adversarialgen", Default = AdversarialGenMethodChoice.Encoding, HelpText = "Adversarial generator method (Encoding | Benders). Benders decomposition currently does not work.")]
        public AdversarialGenMethodChoice AdversarialGen { get; set; }

        /// <summary>
        /// Quantized list of demand values for PrimalDual encoding.
        /// Comma-separated values without spaces.
        /// </summary>
        /// <remarks>
        /// Example: "0,5,10,15,20" allows demands to take only these discrete values.
        /// Required when using PrimalDual inner encoding for tractable optimization.
        /// More values = finer granularity but slower optimization.
        /// </remarks>
        /// TODO: This terminology is too TE specific. Can you make it more general to apply to
        /// our other heuristics too? Also would be good to expand the comment.
        [Option("demandlist", Default = "0", HelpText = "Quantized demand values (comma-separated, no spaces). Only applies to PrimalDual encoding.")]
        public string DemandList { get; set; }

        /// <summary>
        /// Whether to simplify the final solution.
        /// </summary>
        /// TODO: This is too vague. Explain what it means to simplify the solution.
        /// Simplification may remove redundant flow allocations or round near-zero values.
        [Option('s', "simplify", Default = false, HelpText = "Whether to simplify the final solution")]
        public bool Simplify { get; set; }

        /// <summary>
        /// Terminate if no improvement in best objective after specified time.
        /// Only applies to MIP (Mixed Integer Programming) problems.
        /// Value of -1 disables this termination condition.
        /// </summary>
        [Option('x', "timetoterminate", Default = -1.0, HelpText = "Terminate if no improvement after specified seconds (MIP only, -1 to disable)")]
        public double TimeToTerminateIfNoImprovement { get; set; }

        /// <summary>
        /// The number of partitions (slices) for Pop heuristic.
        /// Demands are randomly assigned to partitions, each optimized separately.
        /// </summary>
        /// TODO: Again this is specific to a particular heuristic, is there a way to separate
        /// inputs that are heuristic specific from those that are general?
        /// One way may be to take a JSON as input that contains the inputs specific to the particular heuristic.
        [Option("slices", Default = 2, HelpText = "Number of Pop partitions/slices")]
        public int PopSlices { get; set; }

        /// <summary>
        /// The threshold for demand pinning heuristic.
        /// Demands above this threshold are pinned to shortest path.
        /// </summary>
        /// TODO: Same as other comments that are about being heuristic specific.
        [Option('t', "pinthreshold", Default = 0.5, HelpText = "Threshold for demand pinning heuristic")]
        public double DemandPinningThreshold { get; set; }

        /// <summary>
        /// The maximum number of paths to consider for each demand pair.
        /// Higher values allow more routing flexibility but increase problem size.
        /// </summary>
        /// TODO: Same as others.
        [Option('p', "paths", Default = 2, HelpText = "Maximum number of paths per demand pair")]
        public int Paths { get; set; }

        /// <summary>
        /// The maximum shortest path length to pin in modified demand pinning.
        /// Only applied when using ModifiedDp heuristic.
        /// Value of -1 disables this constraint.
        /// </summary>
        /// TODO: Same as others.
        [Option("maxshortestlen", Default = -1, HelpText = "Maximum shortest path length to pin (ModifiedDp only, -1 to disable)")]
        public int MaxShortestPathLen { get; set; }

        /// <summary>
        /// Method for finding the optimality gap.
        /// </summary>
        /// <remarks>
        /// - Direct: Solve bilevel optimization directly for maximum gap
        /// - Search: Binary search within interval [0, startinggap] for maximum gap
        /// - FindFeas: Find any solution with gap >= startinggap
        /// - Random: Random sampling to estimate gap distribution
        /// - HillClimber: Local search with neighborhood exploration
        /// - SimulatedAnnealing: Probabilistic local search with temperature cooling.
        /// </remarks>
        /// TODO: Expand on the comment to describe what each option does.
        [Option('m', "method", Default = MethodChoice.Direct, HelpText = "Gap-finding method [Direct | Search | FindFeas | Random | HillClimber | SimulatedAnnealing]")]
        public MethodChoice Method { get; set; }

        /// <summary>
        /// Confidence level for Search method.
        /// Search terminates when solution is within this fraction of optimal.
        /// </summary>
        [Option("confidence", Default = 0.1, HelpText = "Search terminates when within this fraction of optimal")]
        public double Confidencelvl { get; set; }

        /// <summary>
        /// Starting gap value for Search and FindFeas methods.
        /// Search uses this as upper bound, FindFeas uses as target threshold.
        /// </summary>
        [Option('g', "startinggap", Default = 10.0, HelpText = "Starting gap for Search/FindFeas methods")]
        public double StartingGap { get; set; }

        /// <summary>
        /// Upper bound on all demand values.
        /// Constrains adversarial inputs to realistic ranges.
        /// Value of -1 means no upper bound.
        /// </summary>
        /// TODO: Is this also heuristic specific? If not, change the terminology to be general.
        /// If yes, fix as stated above.
        [Option('u', "demandub", Default = -1.0, HelpText = "Upper bound on demand values (-1 for no bound)")]
        public double DemandUB { get; set; }

        /// <summary>
        /// Maximum difference of total demands between partitions.
        /// Used to balance load across partitions.
        /// Value of -1 disables this constraint.
        /// </summary>
        /// TODO: Same as above.
        [Option("partitionSensitivity", Default = -1.0, HelpText = "Maximum demand difference between partitions (-1 to disable)")]
        public double PartitionSensitivity { get; set; }

        /// <summary>
        /// Number of trials for Random search or HillClimber.
        /// More trials increase chance of finding better solutions.
        /// </summary>
        [Option('n', "num", Default = 1, HelpText = "Number of trials for Random/HillClimber")]
        public int NumRandom { get; set; }

        /// <summary>
        /// Number of neighbors to evaluate before declaring local optimum.
        /// Used by HillClimber and SimulatedAnnealing.
        /// </summary>
        [Option('k', "neighbors", Default = 1, HelpText = "Neighbors to check before local optimum [HillClimber | SimulatedAnnealing]")]
        public int NumNeighbors { get; set; }

        /// <summary>
        /// Initial temperature for simulated annealing.
        /// Higher values allow more exploration early in search.
        /// </summary>
        [Option("inittmp", Default = 1.0, HelpText = "Initial temperature for simulated annealing")]
        public double InitTmp { get; set; }

        /// <summary>
        /// Temperature decrease factor for simulated annealing.
        /// Temperature is multiplied by this factor each iteration.
        /// </summary>
        [Option('l', "lambda", Default = 1.0, HelpText = "Temperature decrease factor for simulated annealing")]
        public double TmpDecreaseFactor { get; set; }

        /// <summary>
        /// Maximum density of the final traffic demand matrix.
        /// Density = (non-zero demands) / (total possible demands).
        /// </summary>
        /// TODO: Same as other comments.
        [Option("maxdensity", Default = 1.0, HelpText = "Maximum density of traffic demand matrix (0.0-1.0)")]
        public double MaxDensity { get; set; }

        /// <summary>
        /// Maximum path distance for large demands.
        /// Restricts large demands to nearby destinations.
        /// Value of -1 disables this constraint.
        /// </summary>
        /// TODO: Same as other comments.
        [Option("maxdistancelarge", Default = -1, HelpText = "Maximum distance for large demands (-1 to disable)")]
        public int maxLargeDistance { get; set; }

        /// <summary>
        /// Maximum path distance for small demands.
        /// Restricts small demands to nearby destinations.
        /// Value of -1 disables this constraint.
        /// </summary>
        /// TODO: Same as other comments.
        [Option("maxdistancesmall", Default = -1, HelpText = "Maximum distance for small demands (-1 to disable)")]
        public int maxSmallDistance { get; set; }

        /// <summary>
        /// Lower bound to distinguish large demands from small demands.
        /// Demands >= this value are considered "large".
        /// Value of -1 disables large/small distinction.
        /// </summary>
        /// TODO: Same as other comments.
        [Option("largedemandlb", Default = -1.0, HelpText = "Threshold to distinguish large vs small demands (-1 to disable)")]
        public double LargeDemandLB { get; set; }

        /// <summary>
        /// Enable hierarchical clustering for scalability.
        /// Clusters the topology and optimizes inter/intra-cluster demands separately.
        /// More scalable but may not find the globally optimal gap.
        /// </summary>
        [Option("enableclustering", Default = false, HelpText = "Enable clustering for scalability (may not find optimal gap)")]
        public bool EnableClustering { get; set; }

        /// <summary>
        /// Directory containing cluster-level topology files.
        /// </summary>
        /// TODO: Not clear what this is doing, need a better user-visible and also private comment.
        /// Should contain JSON files defining the cluster structure and inter-cluster links.
        [Option("clusterdir", Default = null, HelpText = "Directory containing cluster topology files")]
        public string ClusterDir { get; set; }

        /// <summary>
        /// Number of clusters to create.
        /// </summary>
        [Option("numclusters", Default = 2, HelpText = "Number of clusters")]
        public int NumClusters { get; set; }

        /// <summary>
        /// Version of clustering algorithm for inter-cluster demands.
        /// </summary>
        /// TODO: What are the options? What is the difference between the different options?
        /// v1, v2, v3 use different strategies for handling demands crossing cluster boundaries.
        [Option("clusterversion", Default = 1, HelpText = "Clustering algorithm version for inter-cluster demands")]
        public int ClusterVersion { get; set; }

        /// <summary>
        /// Number of inter-cluster demand samples.
        /// </summary>
        [Option("interclustersamples", Default = 0, HelpText = "Number of inter-cluster demand samples")]
        public int NumInterClusterSamples { get; set; }

        /// <summary>
        /// Number of nodes per cluster for inter-cluster edge representation.
        /// </summary>
        [Option("nodespercluster", Default = 0, HelpText = "Nodes per cluster for inter-cluster edges")]
        public int NumNodesPerCluster { get; set; }

        /// <summary>
        /// Number of quantization levels for inter-cluster demands.
        /// Only applies to clustering version 3.
        /// </summary>
        /// TODO: Unclear how one should use this parameter.
        [Option("numinterclusterquantization", Default = -1, HelpText = "Inter-cluster demand quantization levels (v3 only)")]
        public int NumInterClusterQuantizations { get; set; }

        /// <summary>
        /// Run full optimization after initial clustering solution.
        /// Uses clustered solution as starting point for full-scale optimization.
        /// Requires clustering to be enabled and PrimalDual inner encoding.
        /// </summary>
        /// TODO: Not fully clear what this does, needs a better comment both internally and user-visible.
        [Option("fullopt", Default = false, HelpText = "Run full optimization with clustered solution as init point")]
        public bool FullOpt { get; set; }

        /// <summary>
        /// Timeout for full optimization phase.
        /// Value of -1 uses the main timeout.
        /// </summary>
        [Option("fullopttimer", Default = -1.0, HelpText = "Timeout for full optimization phase (-1 uses main timeout)")]
        public double FullOptTimer { get; set; }

        /// <summary>
        /// Focus on improving upper bound after initial solution.
        /// Runs additional optimization phase targeting the dual bound.
        /// </summary>
        [Option("ubfocus", Default = false, HelpText = "Run additional phase focusing on upper bound improvement")]
        public bool UBFocus { get; set; }

        /// <summary>
        /// Timeout for upper bound focus phase.
        /// Value of -1 uses the main timeout.
        /// </summary>
        [Option("ubfocustimeout", Default = -1.0, HelpText = "Timeout for upper bound focus phase (-1 uses main timeout)")]
        public double UBFocusTimer { get; set; }

        /// <summary>
        /// Number of parallel processes to use.
        /// Value of -1 uses system default.
        /// </summary>
        /// TODO: For what? Parallel optimization? Parallel validation?
        [Option("numProcesses", Default = -1, HelpText = "Number of parallel processes (-1 for system default)")]
        public int NumProcesses { get; set; }

        /// <summary>
        /// Random seed for reproducibility.
        /// Used by Random, HillClimber, SimulatedAnnealing methods.
        /// </summary>
        [Option("seed", Default = 1, HelpText = "Random seed for reproducibility")]
        public int Seed { get; set; }

        /// <summary>
        /// Standard deviation for neighbor generation in HillClimber.
        /// Controls the size of random perturbations when exploring neighbors.
        /// </summary>
        [Option('b', "stddev", Default = 100, HelpText = "Standard deviation for neighbor generation [HillClimber]")]
        public int StdDev { get; set; }

        /// <summary>
        /// Store optimization progress trajectory.
        /// Saves intermediate solutions for analysis.
        /// </summary>
        [Option("storeprogress", Default = false, HelpText = "Store optimization progress trajectory")]
        public bool StoreProgress { get; set; }

        /// <summary>
        /// File containing pre-computed paths to use.
        /// </summary>
        /// TODO: Heuristic specific. It would also be good to specify what format the file has to have.
        /// Expected format: JSON with paths per source-destination pair.
        [Option("pathfile", Default = null, HelpText = "File containing pre-computed paths (JSON format)")]
        public string PathFile { get; set; }

        /// <summary>
        /// Path to log file for storing progress.
        /// </summary>
        [Option("logfile", Default = null, HelpText = "Path to log file for progress storage")]
        public string LogFile { get; set; }

        /// <summary>
        /// Factor to downscale the optimization problem.
        /// Reduces problem size for faster experimentation.
        /// All capacities and demands are multiplied by this factor.
        /// </summary>
        [Option("downscale", Default = 1.0, HelpText = "Factor to downscale problem size (1.0 = no scaling)")]
        public double DownScaleFactor { get; set; }

        /// <summary>
        /// Number of threads for Gurobi solver.
        /// Value of 0 lets Gurobi choose automatically.
        /// Value of 1 ensures deterministic results but slower execution.
        /// </summary>
        [Option("gurobithreads", Default = 1, HelpText = "Gurobi threads (0=auto, 1=deterministic)")]
        public int NumGurobiThreads { get; set; }

        #endregion

        #region Bin Packing Options

        /// <summary>
        /// Number of bins available for packing.
        /// The adversarial generator tries to find items that use many bins with FFD
        /// while requiring fewer bins optimally.
        /// </summary>
        [Option("numBins", Default = 6, HelpText = "Number of bins available (BinPacking)")]
        public int NumBins { get; set; }

        /// <summary>
        /// Number of items/demands to pack.
        /// More items = larger search space for adversarial inputs.
        /// </summary>
        [Option("numDemands", Default = 9, HelpText = "Number of items to pack (BinPacking)")]
        public int NumDemands { get; set; }

        /// <summary>
        /// Number of dimensions for vector bin packing.
        /// Each item has size in each dimension, bins have capacity per dimension.
        /// </summary>
        [Option("numDimensions", Default = 2, HelpText = "Number of dimensions (BinPacking)")]
        public int NumDimensions { get; set; }

        /// <summary>
        /// Target number of bins for optimal solution.
        /// Adversarial generator finds items that pack optimally in this many bins
        /// but require more bins with FFD heuristic.
        /// </summary>
        [Option("optimalBins", Default = 3, HelpText = "Target optimal bin count (BinPacking)")]
        public int OptimalBins { get; set; }

        /// <summary>
        /// First-Fit variant to use as the heuristic.
        /// </summary>
        /// <remarks>
        /// - FF: First Fit (no sorting, place in first bin that fits)
        /// - FFDSum: First Fit Decreasing by sum of dimensions
        /// - FFDProd: First Fit Decreasing by product of dimensions
        /// - FFDDiv: First Fit Decreasing by division of dimensions (2D only).
        /// </remarks>
        [Option("ffMethod", Default = FFDMethodChoice.FFDSum, HelpText = "First-Fit variant: FF, FFDSum, FFDProd, FFDDiv")]
        public FFDMethodChoice FFMethod { get; set; }

        /// <summary>
        /// Enable symmetry breaking constraints.
        /// Reduces search space by eliminating equivalent solutions.
        /// May speed up optimization but could miss some adversarial inputs.
        /// </summary>
        [Option("breakSymmetry", Default = "false", HelpText = "Enable symmetry breaking in BinPacking encoder (true/false)")]
        public string BreakSymmetryStr { get; set; }

        /// <summary>
        /// Enable symmetry breaking constraints.
        /// Reduces search space by eliminating equivalent solutions.
        /// May speed up optimization but could miss some adversarial inputs.
        /// </summary>
        /// <remarks>
        /// If enabled, we leverage symmetry to reduce the optimization size and enhance the scalability of optimal bin packing.
        /// </remarks>
        public bool BreakSymmetry =>
            string.Equals(BreakSymmetryStr, "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Bin capacities per dimension (comma-separated).
        /// Each value is the capacity for one dimension.
        /// Values slightly above 1.0 (e.g., 1.00001) avoid numerical issues.
        /// </summary>
        [Option("binCapacity", Default = "1.00001,1.00001", HelpText = "Comma-separated bin capacities per dimension")]
        public string BinCapacity { get; set; }

        #endregion

        #region PIFO Options

        /// <summary>
        /// First PIFO method (h1 encoder).
        /// </summary>
        [Option("pifoMethod1", Default = PIFOMethodChoice.SPPIFO, HelpText = "First PIFO method: PIFO, SPPIFO, AIFO, ModifiedSPPIFO")]
        public PIFOMethodChoice PIFOMethod1 { get; set; }

        /// <summary>
        /// Second PIFO method (h2 encoder).
        /// </summary>
        [Option("pifoMethod2", Default = PIFOMethodChoice.AIFO, HelpText = "Second PIFO method: PIFO, SPPIFO, AIFO, ModifiedSPPIFO")]
        public PIFOMethodChoice PIFOMethod2 { get; set; }

        /// <summary>
        /// Whether to consider packet drop in PIFO scheduling.
        /// </summary>
        [Option("considerPktDrop", Default = true, HelpText = "Consider packet drop (true/false)")]
        public bool ConsiderPktDrop { get; set; }

        /// <summary>
        /// Split queue parameter for ModifiedSPPIFO.
        /// </summary>
        [Option("splitQueue", Default = 4, HelpText = "Split queue count for ModifiedSPPIFO")]
        public int SplitQueue { get; set; }

        /// <summary>
        /// Split rank parameter for ModifiedSPPIFO.
        /// </summary>
        [Option("splitRank", Default = 100, HelpText = "Split rank threshold for ModifiedSPPIFO")]
        public int SplitRank { get; set; }

        /// <summary>
        /// Number of packets in the sequence.
        /// More packets = larger adversarial search space.
        /// </summary>
        [Option("numPackets", Default = 18, HelpText = "Number of packets (PIFO)")]
        public int NumPackets { get; set; }

        /// <summary>
        /// Maximum rank value for packet priorities.
        /// Lower rank = higher priority.
        /// </summary>
        [Option("maxRank", Default = 8, HelpText = "Maximum rank value (PIFO)")]
        public int MaxRank { get; set; }

        /// <summary>
        /// Number of priority queues for SP-PIFO.
        /// Packets are mapped to queues based on rank.
        /// </summary>
        [Option("numQueues", Default = 4, HelpText = "Number of queues for SP-PIFO")]
        public int NumQueues { get; set; }

        /// <summary>
        /// Maximum size of each queue.
        /// Packets are dropped if queue is full.
        /// </summary>
        [Option("maxQueueSize", Default = 12, HelpText = "Maximum queue size (PIFO)")]
        public int MaxQueueSize { get; set; }

        /// <summary>
        /// Window size for AIFO admission control.
        /// AIFO admits packets based on rank relative to recent window.
        /// </summary>
        [Option("windowSize", Default = 12, HelpText = "AIFO window size (PIFO)")]
        public int WindowSize { get; set; }

        /// <summary>
        /// Burst tolerance parameter for AIFO.
        /// Controls how much burst traffic is allowed.
        /// </summary>
        [Option("burstParam", Default = 0.1, HelpText = "AIFO burst parameter (PIFO)")]
        public double BurstParam { get; set; }

        #endregion

        #region Failure Analysis Options

        /// <summary>
        /// Use built-in default topology instead of loading from file.
        /// Default topology is a simple 4-node diamond for testing.
        /// </summary>
        [Option("useDefaultTopology", Default = "true", HelpText = "Use built-in default topology for Failure Analysis. Requires value: true or false")]
        public string UseDefaultTopologyStr { get; set; }

        /// <summary>
        /// Use built-in default topology instead of loading from file.
        /// Default topology is a simple 4-node diamond for testing.
        /// </summary>
        public bool UseDefaultTopology =>
            string.Equals(UseDefaultTopologyStr, "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Maximum number of simultaneous link failures to consider.
        /// Higher values explore more failure scenarios but increase problem size.
        /// </summary>
        [Option("maxNumFailures", Default = 1, HelpText = "Maximum simultaneous link failures")]
        public int MaxNumFailures { get; set; }

        /// <summary>
        /// Number of extra paths for rerouting under failures.
        /// More paths = more rerouting options but larger problem.
        /// </summary>
        [Option("numExtraPaths", Default = 1, HelpText = "Extra paths for failure rerouting")]
        public int NumExtraPaths { get; set; }

        /// <summary>
        /// Minimum failure probability for considering a link.
        /// Links with probability below this threshold are assumed not to fail.
        /// </summary>
        [Option("failureProbThreshold", Default = 0.25, HelpText = "Minimum failure probability to consider")]
        public double FailureProbThreshold { get; set; }

        /// <summary>
        /// Minimum scenario probability threshold.
        /// Failure scenarios with combined probability below this are ignored.
        /// </summary>
        [Option("scenarioProbThreshold", Default = 0.0, HelpText = "Minimum failure scenario probability")]
        public double ScenarioProbThreshold { get; set; }

        #endregion

    }

    #region Enums

    /// <summary>
    /// Problem type to solve.
    /// </summary>
    public enum ProblemType
    {
        /// <summary>
        /// Traffic engineering: Find worst-case demand patterns for routing heuristics.
        /// </summary>
        TrafficEngineering,

        /// <summary>
        /// Bin packing: Find item sizes that maximize FFD vs optimal gap.
        /// </summary>
        BinPacking,

        /// <summary>
        /// PIFO: Find packet sequences that maximize scheduling inversions.
        /// </summary>
        PIFO,

        /// <summary>
        /// Failure analysis: Find failure scenarios that maximize throughput degradation.
        /// </summary>
        FailureAnalysis,
    }

    /// <summary>
    /// The encoding heuristic for traffic engineering.
    /// </summary>
    public enum Heuristic
    {
        /// <summary>
        /// Partition-based routing with random demand partitioning.
        /// </summary>
        Pop,

        /// <summary>
        /// Average Pop performance over multiple partition samples.
        /// </summary>
        ExpectedPop,

        /// <summary>
        /// Threshold-based path selection (pin large demands to shortest path).
        /// </summary>
        DemandPinning,

        /// <summary>
        /// Combined Pop and DemandPinning.
        /// </summary>
        PopDp,

        /// <summary>
        /// Average gap of running Pop and DemandPinning in parallel.
        /// </summary>
        ExpectedPopDp,

        /// <summary>
        /// Multiple Pop instances running in parallel.
        /// </summary>
        ParallelPop,

        /// <summary>
        /// Multiple Pop instances in parallel with DemandPinning.
        /// </summary>
        ParallelPopDp,

        /// <summary>
        /// DemandPinning with upper bound on pinned path lengths.
        /// </summary>
        ModifiedDp,
    }

    /// <summary>
    /// The solver to use for optimization.
    /// </summary>
    public enum SolverChoice
    {
        /// <summary>
        /// Gurobi solver (MIP - Mixed Integer Programming).
        /// Commercial solver, fast, requires license.
        /// </summary>
        Gurobi,

        /// <summary>
        /// Zen solver (SMT - Satisfiability Modulo Theories).
        /// Based on Z3, open source, good for constraint satisfaction.
        /// </summary>
        Zen,

        /// <summary>
        /// OR-Tools solver (CP-SAT - Constraint Programming with SAT).
        /// Google's open-source optimization suite, good for combinatorial optimization.
        /// </summary>
        OrTools,
    }

    /// <summary>
    /// Method for finding the optimality gap.
    /// </summary>
    public enum MethodChoice
    {
        /// <summary>
        /// Directly solve bilevel optimization for maximum gap.
        /// Most accurate but may be slow for large problems.
        /// </summary>
        Direct,

        /// <summary>
        /// Binary search within interval for maximum gap.
        /// Faster than Direct for some problems.
        /// </summary>
        Search,

        /// <summary>
        /// Find any feasible solution with gap >= startinggap.
        /// Fastest when you only need to prove a gap exists.
        /// </summary>
        FindFeas,

        /// <summary>
        /// Random sampling to estimate gap distribution.
        /// Good for understanding gap landscape.
        /// </summary>
        Random,

        /// <summary>
        /// Local search with neighborhood exploration.
        /// May find good solutions faster than Direct.
        /// </summary>
        HillClimber,

        /// <summary>
        /// Probabilistic local search with temperature cooling.
        /// Can escape local optima better than HillClimber.
        /// </summary>
        SimulatedAnnealing,
    }

    /// <summary>
    /// PIFO method choices for scheduling algorithms.
    /// </summary>
    public enum PIFOMethodChoice
    {
        /// <summary>
        /// Push-In First-Out: Packets are inserted based on rank and dequeued from the head.
        /// </summary>
        PIFO,

        /// <summary>
        /// Strict Priority PIFO: Uses multiple priority queues to approximate PIFO behavior.
        /// </summary>
        SPPIFO,

        /// <summary>
        /// Approximate In-Order First-Out: Uses shallow buffers with admission control.
        /// Only valid when ConsiderPktDrop is true.
        /// </summary>
        AIFO,

        /// <summary>
        /// Modified SP-PIFO: Variant with configurable queue splitting via splitQueue and splitRank parameters.
        /// Only valid when ConsiderPktDrop is false.
        /// </summary>
        ModifiedSPPIFO,
    }

    #endregion
}