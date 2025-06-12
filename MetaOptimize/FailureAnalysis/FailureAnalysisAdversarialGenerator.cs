namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using Gurobi;
    using Microsoft.Z3;
    using NLog;
    using QuikGraph.Algorithms.ShortestPath;
    using ZenLib;
    /// <summary>
    /// A class that generates adversarial scenarios for network failure analysis by finding the worst-case failure combinations.
    /// This generator helps identify critical points of failure in a network by:
    /// 1. Finding combinations of link/LAG failures that maximize the gap between the original network performance (without failures) and the network under failures.
    /// 2. Supporting different types of failures (link failures, LAG failures, or SRLG failures).
    /// 3. Allowing constraints on failure probabilities and maximum number of simultaneous failures.
    /// 4. Enabling analysis of both primary and backup paths.
    ///
    /// The class can work in two modes:
    /// - Equal paths mode: Assumes same number of primary and backup paths between all node pairs.
    /// - Custom paths mode: Allows specifying different numbers of primary and backup paths for each node pair.
    /// </summary>
    public class FailureAnalysisAdversarialGenerator<TVar, TSolution> : TEAdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// The solver object.
        /// </summary>
        protected ISolver<TVar, TSolution> Solver = null;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Encode the results of whether the LAG is up or not.
        /// </summary>
        protected List<bool> LagUpResults;

        /// <summary>
        /// These are constraints that enforce the capacity levels for primal-dual.
        /// </summary>
        protected Dictionary<(string, string), Polynomial<TVar>> CapacityEnforcers { get; set; }
        /// <summary>
        /// These are the constraints that enforce the path extension capacity levels.
        /// </summary>
        protected Dictionary<string[], Polynomial<TVar>> PathExtensionCapacityEnforcers { get; set; }
        /// <summary>
        /// The set of paths between a source and a destination.
        /// </summary>
        protected Dictionary<(string, string), string[][]> Paths { get; set; }
        /// <summary>
        /// Whether a particular path is up or not. If the path is up the value of this variable is 0.
        /// </summary>
        protected Dictionary<string[], TVar> PathUpVariables { get; set; }
        /// <summary>
        /// These are variables that we use to compute the maximum in the extra link capacity constraints.
        /// </summary>
        protected List<TVar> AuxiliaryVariables { get; set; }
        /// <summary>
        /// Specifies the upper bound on the path capacity.
        /// </summary>
        protected double CapacityUpperBound = 1e6;
        /// <summary>
        /// These are the variables u_l in the documentation we wrote internally. They are binary variables where
        /// the value 1 is if and only if the LAG is down. To add support for SRLGs we need to add another variable that defines when the SRLG is down.
        /// We have added that in the unequal paths extension of this class.
        /// </summary>
        protected Dictionary<(string, string), TVar> LagUpVariables { get; set; }
        /// <summary>
        /// Link up variables are the equivalent of LAG up variables but for links.
        /// </summary>
        public Dictionary<(string, string, string), TVar> LinkUpVariables { get; set; }
        /// <summary>
        /// Maps an SRLG number to all Links that it belongs to.
        /// </summary>
        public Dictionary<int, HashSet<(string, string, string)>> SRLGsToLinks { get; set; }
        /// <summary>
        /// Indicate whether an SRLG is up or not: 0 for up and 1 for down.
        /// </summary>
        public Dictionary<int, TVar> SRLGUpVariables { get; set; }
        /// <summary>
        /// Ensures we do not fail metro links.
        /// </summary>
        protected bool DoNotFailMetro = false;
        /// <summary>
        /// Initializer for the failure analysis adversarial input generator.
        /// </summary>
        public FailureAnalysisAdversarialGenerator(Topology topology, int maxNumPaths, int numProcesses = -1)
        : base(topology, maxNumPaths, numProcesses)
        {
            this.AuxiliaryVariables = new List<TVar>();
            this.CapacityUpperBound = -1;
            foreach (var edge in topology.GetAllEdges())
            {
                if (edge.Capacity > this.CapacityUpperBound)
                {
                    this.CapacityUpperBound = edge.Capacity;
                }
            }
            this.LinkUpVariables = null;
        }
        /// <summary>
        /// Sets the path for the failure analysis use-case.
        /// </summary>
        /// <param name="numExtraPaths">Number of backup paths to use.</param>
        /// <param name="pathType">Whether to use k-shortest paths.</param>
        /// <param name="selectedPaths">The set of pre-defined paths if the user provided them.</param>
        /// <param name="relayFilter">The set of nodes that are not allowed to act as transit nodes.</param>
        public virtual void SetPath(int numExtraPaths, PathType pathType = PathType.KSP, Dictionary<(string, string), string[][]> selectedPaths = null, List<string> relayFilter = null)
        {
            if (relayFilter != null)
            {
                throw new Exception("We have not yeti mplemented the path filter in this instance");
            }
            if (selectedPaths == null)
            {
                this.Paths = this.Topology.ComputePaths(pathType, selectedPaths, this.maxNumPath + numExtraPaths, this.NumProcesses, false);
            }
        }
        /// <summary>
        /// Finds the worst-case failure scenario that maximizes the performance gap between the original network and the network under failures.
        /// This function:
        /// 1. Takes a network topology and finds combinations of link/LAG failures that cause the largest performance degradation.
        /// 2. Supports various failure types (link, LAG, or SRLG failures).
        /// 3. Can enforce constraints on:
        ///    - Maximum number of simultaneous failures
        ///    - Failure probabilities for individual components
        ///    - Overall scenario probability
        ///    - Network connectivity (ensuring at least one path remains between node pairs).
        /// 4. Returns both the original network performance and the degraded performance under failures.
        ///
        /// Key parameters:
        /// - optimalEncoder: Encoder for the original network without failures.
        /// - failureAnalysisEncoder: Encoder for the network under failure scenarios.
        /// - demandUB: Upper bound on network demands.
        /// - maxNumFailures: Maximum number of simultaneous failures allowed.
        /// - failureProbThreshold: Probability threshold for individual failures.
        /// - scenarioProbThreshold: Probability threshold for overall failure scenarios.
        /// - useLinkFailures: Whether to consider individual link failures instead of LAG failures.
        /// - ensureConnectedGraph: Whether to ensure at least one path remains between each node pair.
        /// </summary>
        /// <param name="optimalEncoder">Encoder for the original network without failures.</param>
        /// <param name="failureAnalysisEncoder">Encoder for the network under failure scenarios.</param>
        /// <param name="demandUB">Upper bound on network demands.</param>
        /// <param name="innerEncoding">The encoding method to use (KKT or primal-dual).</param>
        /// <param name="demandList">List of demands to use for quantization.</param>
        /// <param name="constrainedDemands">Pre-specified demands that must be satisfied.</param>
        /// <param name="cleanUpSolver">Whether to clean the solver before running.</param>
        /// <param name="perDemandUB">Upper bounds for individual demands.</param>
        /// <param name="demandInits">Initial values for demands.</param>
        /// <param name="capacityInits">Initial values for capacities.</param>
        /// <param name="pathExtensionCapacityInits">Initial values for path extension capacities.</param>
        /// <param name="density">Density constraint for the demand matrix.</param>
        /// <param name="largeDemandLB">Lower bound for large demands.</param>
        /// <param name="largeMaxDistance">Maximum distance between large demands.</param>
        /// <param name="smallMaxDistance">Maximum distance between small demands.</param>
        /// <param name="pathType">Type of path computation to use.</param>
        /// <param name="selectedPaths">Pre-computed paths to use.</param>
        /// <param name="historicDemandConstraints">Constraints based on historical demands.</param>
        /// <param name="numExtraPaths">Number of backup paths to use.</param>
        /// <param name="maxNumFailures">Maximum number of simultaneous failures allowed.</param>
        /// <param name="exactNumFailures">Exact number of failures to find.</param>
        /// <param name="lagFailureProbabilities">Failure probabilities for LAGs.</param>
        /// <param name="failureProbThreshold">Probability threshold for individual failures.</param>
        /// <param name="scenarioProbThreshold">Probability threshold for overall failure scenarios.</param>
        /// <param name="linkFailureProbabilities">Failure probabilities for individual links.</param>
        /// <param name="useLinkFailures">Whether to consider individual link failures instead of LAG failures.</param>
        /// <param name="ensureConnectedGraph">Whether to ensure at least one path remains between each node pair.</param>
        /// <param name="lagStatusConstraints">Pre-specified LAG status constraints.</param>
        /// <param name="relayFilter">Nodes that cannot be used as intermediate nodes.</param>
        /// <param name="optimalIsConstant">Whether the optimal solution is constant.</param>
        /// <param name="QARCComparison">Whether to run a comparison with QARC.</param>
        /// <param name="SRLGFailureProbabilities">Failure probabilities for SRLGs.</param>
        /// <param name="SRLGsToLinks">Mapping from SRLGs to their constituent links.</param>
        /// <param name="useSRLGFailures">Whether to consider SRLG failures.</param>
        /// <param name="doNotFailMetro">Whether we want to allow metro links to fail.</param>
        /// <returns>A tuple containing the original network performance and the degraded performance under failures.</returns>
        public virtual (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> failureAnalysisEncoder,
            double demandUB = -1,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            IList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            IDictionary<(string, string), double> capacityInits = null,
            IDictionary<string[], double> pathExtensionCapacityInits = null,
            double density = 1.0,
            double largeDemandLB = -1,
            int largeMaxDistance = -1,
            int smallMaxDistance = -1,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            int numExtraPaths = 0,
            int maxNumFailures = -1,
            int exactNumFailures = -1,
            Dictionary<(string, string), double> lagFailureProbabilities = null,
            double failureProbThreshold = -1,
            double scenarioProbThreshold = -1,
            Dictionary<(string, string, string), double> linkFailureProbabilities = null,
            bool useLinkFailures = false,
            bool ensureConnectedGraph = false,
            Dictionary<(string, string), bool> lagStatusConstraints = null,
            List<string> relayFilter = null,
            bool optimalIsConstant = false,
            bool QARCComparison = false,
            Dictionary<int, double> SRLGFailureProbabilities = null,
            Dictionary<int, HashSet<(string, string, string)>> SRLGsToLinks = null,
            bool useSRLGFailures = false,
            bool doNotFailMetro = false)
            {
                this.DoNotFailMetro = doNotFailMetro;
                Logger.Info("Starting MaximizeOptimalityGap with parameters: useLinkFailures={0}, useSRLGFailures={1}, ensureConnectedGraph={2}",
                    useLinkFailures, useSRLGFailures, ensureConnectedGraph);

                if (linkFailureProbabilities != null && lagFailureProbabilities != null)
                {
                    Logger.Error("Cannot have both link and lag failure probabilities that are not null");
                    throw new Exception("You cannot have both link and lag failure probabilities that are not null");
                }
                if (linkFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    Logger.Error("Cannot use both link and SRLG failure probabilities");
                    throw new Exception("You cannot use both link and SRLG failure probabilities.");
                }
                if (lagFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    Logger.Error("Cannot use both LAG and SRLG failure probabilities");
                    throw new Exception("You cannot use both LAG and SRLG failure probabilities");
                }
                if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
                {
                    Logger.Error("Solver mismatch between optimal and failure analysis encoders");
                    throw new Exception("Solver mismatch between optimal and failure analysis encoders");
                }
                if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
                {
                    Logger.Error("Demand list is required for primal dual encoding");
                    throw new Exception("Demand list is required for primal dual encoding");
                }
                if (demandUB != -1 && perDemandUB != null)
                {
                    Logger.Error("Cannot have both the demand ub and the perdemandUb");
                    throw new Exception("You cannot have both the demand ub and the perdemandUb");
                }
                if (pathType == PathType.Predetermined && selectedPaths == null)
                {
                    Logger.Error("If using predetermined paths, you need to provide those paths");
                    throw new Exception("If you are using predetermined paths you need to provide those paths");
                }
                if (pathType != PathType.Predetermined && selectedPaths != null)
                {
                    Logger.Error("If path is not predetermined then selected paths should be null");
                    throw new Exception("If path is not predetermined then selected paths should be null");
                }

                Logger.Info("Setting up paths with numExtraPaths={0}, pathType={1}", numExtraPaths, pathType);
                if (selectedPaths == null)
                {
                    this.SetPath(numExtraPaths, pathType, selectedPaths, relayFilter);
                }
                else
                {
                    this.Paths = selectedPaths;
                }

                if (SRLGsToLinks != null)
                {
                    Logger.Info("Setting up SRLG to links mapping with {0} SRLGs", SRLGsToLinks.Count);
                    this.SRLGsToLinks = SRLGsToLinks;
                }

                if (useSRLGFailures && !useLinkFailures)
                {
                    Logger.Error("Need link failures to be true to use SRLG support");
                    throw new Exception("We need the link failures to be true if we want to use SRLG support extension possible but not implemented");
                }

                var pathSubset = VerifyPaths(this.Paths, numExtraPaths);
                Debug.Assert(pathSubset != null);
                Logger.Info("Verified paths: {0} path pairs found", pathSubset.Count);

                // check if the inputs to the function make sense.
                CheckDensityAndLocalityInputs(innerEncoding, density, largeDemandLB, largeMaxDistance, smallMaxDistance);
                var solver = optimalEncoder.Solver;
                if (cleanUpSolver)
                {
                    Logger.Info("Cleaning solver before proceeding");
                    solver.CleanAll();
                }
                this.Solver = solver;

                Logger.Info("Creating demand variables with parameters: largeMaxDistance={0}, smallMaxDistance={1}, largeDemandLB={2}",
                    largeMaxDistance, smallMaxDistance, largeDemandLB);
                (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                    CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);

                Logger.Info("Creating capacity variables");
                this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);

                Logger.Info("Creating failure indicator variables");
                this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
                if (useLinkFailures)
                {
                    Logger.Info("Creating link up variables");
                    this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
                }
                if (useSRLGFailures)
                {
                    Logger.Info("Creating SRLG up variables");
                    this.SRLGUpVariables = CreateSRLGUpVariables(solver);
                }
                this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);

                Logger.Info("Initializing encodings");
                TEMaxFlowOptimizationSolution optimalParsedSolution = null;
                OptimizationEncoding<TVar, TSolution> optimalEncoding = null;
                if (!QARCComparison)
                {
                    Logger.Info("Creating optimal encoding");
                    optimalEncoding = optimalEncoder.Encoding(this.Topology,
                        preInputVariables: this.DemandEnforcers,
                        inputEqualityConstraints: this.LocalityConstrainedDemands,
                        noAdditionalConstraints: true,
                        innerEncoding: innerEncoding,
                        pathType: pathType,
                        selectedPaths: pathSubset,
                        historicInputConstraints: historicDemandConstraints,
                        numProcesses: this.NumProcesses);
                }

                Logger.Info("Adding equality constraints");
                EnsureDemandEquality(solver, constrainedDemands);

                Logger.Info("Adding density constraint: max density={0}", density);
                EnsureDensityConstraint(solver, density);

                if (perDemandUB != null)
                {
                    Logger.Info("Adding per-demand upper bounds");
                    EnsureDemandUB(solver, perDemandUB);
                }
                else
                {
                    Logger.Info("Adding global demand upper bound: {0}", demandUB);
                    EnsureDemandUB(solver, demandUB);
                }

                if (optimalIsConstant & !QARCComparison)
                {
                    Logger.Info("Computing optimal solution for constant case");
                    var optimalSolution = solver.Maximize(optimalEncoding.GlobalObjective);
                    optimalParsedSolution = (TEMaxFlowOptimizationSolution)optimalEncoder.GetSolution(optimalSolution);
                    solver.CleanAll();

                    Logger.Info("Recreating variables for constant case");
                    (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                    this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                    this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);
                    this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
                    if (useLinkFailures)
                    {
                        this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
                    }
                    if (useSRLGFailures)
                    {
                        this.SRLGUpVariables = CreateSRLGUpVariables(solver);
                    }
                    this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);

                    Logger.Info("Re-adding constraints for constant case");
                    EnsureDemandEquality(solver, constrainedDemands);
                    EnsureDensityConstraint(solver, density);
                    failureAnalysisEncoder.Solver = solver;
                    if (perDemandUB != null)
                    {
                        EnsureDemandUB(solver, perDemandUB);
                    }
                    else
                    {
                        EnsureDemandUB(solver, demandUB);
                    }
                }

                Logger.Info("Creating failure analysis encoding");
                var failureEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                    preDemandVariables: this.DemandEnforcers,
                    preCapVariables: this.CapacityEnforcers,
                    prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                    demandEqualityConstraints: this.LocalityConstrainedDemands,
                    noAdditionalConstraints: false,
                    innerRewriteMethod: innerEncoding,
                    pathType: pathType,
                    selectedPaths: this.Paths,
                    historicDemandConstraints: historicDemandConstraints);

                Logger.Info("Adding failure impact constraints");
                if (!useLinkFailures)
                {
                    AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
                else
                {
                    AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }

                Logger.Info("Adding failure number constraints: maxNumFailures={0}, exactNumFailures={1}", maxNumFailures, exactNumFailures);
                if (maxNumFailures >= 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraint(solver, maxNumFailures);
                }
                if (maxNumFailures >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraintsOnLinks(solver, maxNumFailures);
                }
                if (maxNumFailures >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddMaxNumFailureConstraintOnSRLG(solver, maxNumFailures);
                }
                if (exactNumFailures >= 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraint(solver, exactNumFailures, exact: true);
                }
                if (exactNumFailures >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraintsOnLinks(solver, exactNumFailures, exact: true);
                }
                if (exactNumFailures >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddMaxNumFailureConstraintOnSRLG(solver, exactNumFailures, exact: true);
                }

                Logger.Info("Adding probability constraints: failureProbThreshold={0}, scenarioProbThreshold={1}",
                    failureProbThreshold, scenarioProbThreshold);
                if (scenarioProbThreshold != -1 && !useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraints(solver, lagFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraintOnLinks(solver, linkFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && useSRLGFailures)
                {
                    AddScenarioProbabilityConstraintsOnSRLG(solver, SRLGFailureProbabilities, scenarioProbThreshold);
                }
                if (failureProbThreshold > 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddFailureProbabilityConstraints(solver, lagFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (failureProbThreshold >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddFailureProbabilityConstraintOnLinks(solver, linkFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (failureProbThreshold >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddFailureProbabilityConstraintOnSRLG(solver, SRLGFailureProbabilities, failureProbThreshold);
                }

                if (useSRLGFailures)
                {
                    Logger.Info("Adding SRLG constraints");
                    AddSRLGConstraints(solver, lagStatusConstraints);
                }
                if (lagStatusConstraints != null)
                {
                    Logger.Info("Adding LAG status constraints");
                    AddLagStatusConstraints(solver, lagStatusConstraints);
                }

                Logger.Info("Constructing and maximizing objective function");
                var objective = new Polynomial<TVar>(
                    QARCComparison ? new Term<TVar>(0) : new Term<TVar>(1, optimalEncoding.GlobalObjective),
                    new Term<TVar>(-1, failureEncoding.GlobalObjective));
                if (optimalIsConstant)
                {
                    objective = new Polynomial<TVar>(
                        new Term<TVar>(QARCComparison ? 0 : optimalParsedSolution.MaxObjective),
                        new Term<TVar>(-1, failureEncoding.GlobalObjective));
                }

                Logger.Info("Solving optimization problem");
                var solution = solver.Maximize(objective, reset: true);
                this.LagUpResults = this.GetLagDownEvents(solution);

                var optSol = (optimalIsConstant || QARCComparison) ? optimalParsedSolution : (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                var failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);

                Logger.Info("Completed MaximizeOptimalityGap");
                return (optSol, failureSol);
            }
            // TODO-engineering: this can be simiplified significantly if I can find a way to use maximizationObjective in the above instead of globalobjective.
            // TODO-engineering: add support for SRLGs to this function.
            /// <summary>
            /// Computes the scenario with the worst impact for MLU, without clustering.
            /// </summary>
        public virtual (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeMLUOptimalityGap(
                IEncoder<TVar, TSolution> optimalEncoder,
                IEncoder<TVar, TSolution> failureAnalysisEncoder,
                double demandUB = -1,
                InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
                IList demandList = null,
                IDictionary<(string, string), double> constrainedDemands = null,
                bool cleanUpSolver = true,
                IDictionary<(string, string), double> perDemandUB = null,
                IDictionary<(string, string), double> demandInits = null,
                IDictionary<(string, string), double> capacityInits = null,
                IDictionary<string[], double> pathExtensionCapacityInits = null,
                double density = 1.0,
                double largeDemandLB = -1,
                int largeMaxDistance = -1,
                int smallMaxDistance = -1,
                PathType pathType = PathType.KSP,
                Dictionary<(string, string), string[][]> selectedPaths = null,
                Dictionary<(int, string, string), double> historicDemandConstraints = null,
                int numExtraPaths = 0,
                int maxNumFailures = -1,
                int exactNumFailures = -1,
                Dictionary<(string, string), double> lagFailureProbabilities = null,
                double failureProbThreshold = -1,
                double scenarioProbThreshold = -1,
                Dictionary<(string, string, string), double> linkFailureProbabilities = null,
                bool useLinkFailures = false,
                bool ensureConnectedGraph = false,
                Dictionary<(string, string), bool> lagStatusConstraints = null,
                List<string> relayFilter = null,
                bool optimalIsConstant = false,
                bool QARCComparison = false,
                HashSet<string> excludeEdges = null)
                {
                    if (linkFailureProbabilities != null && lagFailureProbabilities != null)
                    {
                        throw new Exception("you cannot have both link and lag failure probabilities that are not null");
                    }
                    if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
                    {
                        throw new Exception("Solver mismatch between optimal and the failure anlaysis scenario");
                    }
                    if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
                    {
                        throw new Exception("Demand list is required for primal dual encoding.");
                    }
                    if (demandUB != -1 && perDemandUB != null)
                    {
                        throw new Exception("if global demand ub is enabled then perDemandUB should not be");
                    }
                    if (pathType == PathType.Predetermined && selectedPaths == null)
                    {
                        throw new Exception("if path type is predetermined, then selectedPaths should not be null.");
                    }
                    if (pathType != PathType.Predetermined && selectedPaths != null)
                    {
                        throw new Exception("if path type is not predetermined then selectedPaths should be null.");
                    }
                    if (selectedPaths == null)
                    {
                        this.SetPath(numExtraPaths, pathType, selectedPaths, relayFilter);
                    }
                    else
                    {
                        this.Paths = selectedPaths;
                    }
                    var pathSubset = VerifyPaths(this.Paths, numExtraPaths);
                    Debug.Assert(pathSubset != null);
                    CheckDensityAndLocalityInputs(innerEncoding, density, largeDemandLB, largeMaxDistance, smallMaxDistance);
                    var solver = optimalEncoder.Solver;
                    if (cleanUpSolver)
                    {
                        solver.CleanAll();
                    }
                    this.Solver = solver;
                    Logger.Info("creating demand variables.");
                    Logger.Info("max large demand distance: " + largeMaxDistance);
                    Logger.Info("max small demand distnace: " + smallMaxDistance);
                    Logger.Info("large demand lb: " + largeDemandLB);
                    (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                    // So far we have setup the demand variables.
                    // We next have to do a similar setup for the main link capacities and the extra link capacities.
                    this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                    this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);
                    // new we need to create variables that encode whether each individual lag/link is up.
                    this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
                    if (useLinkFailures)
                    {
                        this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
                    }
                    this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);
                    // The encodings specify the problem formulation for the two inner problems.
                    // we next need to initialize those.
                    Logger.Info("Initializing the encoding");
                    TEOptimizationSolution optimalParsedSolution = null;
                    OptimizationEncoding<TVar, TSolution> optimalEncoding = null;
                    if (!QARCComparison)
                    {
                        optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                                  preInputVariables: this.DemandEnforcers,
                                                                  inputEqualityConstraints: this.LocalityConstrainedDemands,
                                                                  innerEncoding: innerEncoding,
                                                                  pathType: pathType,
                                                                  selectedPaths: pathSubset,
                                                                  numProcesses: this.NumProcesses,
                                                                  noAdditionalConstraints: true,
                                                                  historicInputConstraints: historicDemandConstraints);
                    }
                    Logger.Info("Adding equality constraints");
                    EnsureDemandEquality(solver, constrainedDemands);
                    Logger.Info("Adding density constraint: max density is " + density);
                    EnsureDensityConstraint(solver, density);
                    if (perDemandUB != null)
                    {
                        EnsureDemandUB(solver, perDemandUB);
                    }
                    else
                    {
                        EnsureDemandUB(solver, demandUB);
                    }
                    if (optimalIsConstant && !QARCComparison)
                    {
                        var optimalSolution = solver.Maximize(optimalEncoding.MaximizationObjective);
                        optimalParsedSolution = (TEOptimizationSolution)optimalEncoder.GetSolution(optimalSolution);
                        solver.CleanAll();
                        (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                                CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                        this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                        this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);

                        // New I need to create the variables that encode whether each individual link/lag is up.
                        this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
                        if (useLinkFailures)
                        {
                            this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
                        }
                        this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);
                        Logger.Info("Adding equality constraints");
                        EnsureDemandEquality(solver, constrainedDemands);
                        Logger.Info("Adding density constraint and max density: " + density);
                        EnsureDensityConstraint(solver, density);
                        failureAnalysisEncoder.Solver = solver;
                        if (perDemandUB != null)
                        {
                            EnsureDemandUB(solver, perDemandUB);
                        }
                        else
                        {
                            EnsureDemandUB(solver, demandUB);
                        }
                    }
                    var failureEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                          preDemandVariables: this.DemandEnforcers,
                                                                          preCapVariables: this.CapacityEnforcers,
                                                                          prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                                                                          demandEqualityConstraints: this.LocalityConstrainedDemands,
                                                                          noAdditionalConstraints: false,
                                                                          innerRewriteMethod: innerEncoding,
                                                                          pathType: pathType,
                                                                          selectedPaths: this.Paths,
                                                                          historicDemandConstraints: historicDemandConstraints,
                                                                          excludeEdges: excludeEdges);
                    Logger.Info("adding the individual constraints to model the failure scenarios");
                    if (!useLinkFailures)
                    {
                        AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                    }
                    else
                    {
                        AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                    }
                    Logger.Info("Now adding constraint for max number of failures.");
                    if (maxNumFailures >= 0 && !useLinkFailures)
                    {
                        AddMaxNumFailureConstraint(solver, maxNumFailures);
                    }
                    if (maxNumFailures >= 0 && useLinkFailures)
                    {
                        AddMaxNumFailureConstraintsOnLinks(solver, maxNumFailures);
                    }
                    if (exactNumFailures >= 0 && !useLinkFailures)
                    {
                        AddMaxNumFailureConstraint(solver, exactNumFailures, exact: true);
                    }
                    if (exactNumFailures >= 0 && useLinkFailures)
                    {
                        AddMaxNumFailureConstraintsOnLinks(solver, exactNumFailures, exact: true);
                    }
                    Logger.Info("Adding the failure probability constraints.");
                    if (scenarioProbThreshold != -1 && !useLinkFailures)
                    {
                        AddScenarioProbabilityConstraints(solver, lagFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                    }
                    if (scenarioProbThreshold != -1 && useLinkFailures)
                    {
                        AddScenarioProbabilityConstraintOnLinks(solver, linkFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                    }
                    if (failureProbThreshold >= 0 && !useLinkFailures)
                    {
                        AddFailureProbabilityConstraints(solver, lagFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                    }
                    if (failureProbThreshold >= 0 && useLinkFailures)
                    {
                        AddFailureProbabilityConstraintOnLinks(solver, linkFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                    }
                    if (lagStatusConstraints != null)
                    {
                        AddLagStatusConstraints(solver, lagStatusConstraints);
                    }
                    Logger.Info("Now we construct the objective function");
                    var objective = new Polynomial<TVar>(
                                    QARCComparison ? new Term<TVar>(0) : new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                                    new Term<TVar>(1, failureEncoding.GlobalObjective));
                    if (optimalIsConstant)
                    {
                        Debug.Assert(false);
                        objective = new Polynomial<TVar>(
                            new Term<TVar>(QARCComparison ? 0 : -1 * optimalParsedSolution.MaxObjective),
                            new Term<TVar>(1, failureEncoding.GlobalObjective));
                    }
                    var solution = solver.Maximize(objective, reset: true);
                    this.LagUpResults = this.GetLagDownEvents(solution);

                    var optSol = (optimalIsConstant || QARCComparison) ? (TEOptimizationSolution)optimalParsedSolution : (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    var failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                    return (optSol, failureSol);
                }
                /// <summary>
                /// Generates a random capacity matrix so that we can use it to create a
                /// warmstart solution for the clustering approach.
                /// </summary>
        public Dictionary<(string, string), double> getRandomCapacities(
                int maxNumFailures = -1,
                int exactNumFailures = -1,
                Dictionary<(string, string), double> lagFailureProbabilities = null,
                double failureProbThreshold = -1,
                double scenarioProbThreshold = -1)
            {
                var rng = new Random();
                int numFailed = 0;
                double failureProb = 1;
                double scenarioProb = 1;
                var rndCapacities = new Dictionary<(string, string), double>();
                if (lagFailureProbabilities == null)
                {
                    throw new Exception("have not yet implemented for the scenario where we don't have lag failure probs.");
                }
                foreach (var edge in this.Topology.GetAllEdges())
                {
                    var failureValue = rng.NextDouble();
                    // if the probability that a link is failed is p then if we generate a number uniformly
                    // at random between 0 and 1 and it is below p that has the same probability.
                    var linkUp = failureValue <= lagFailureProbabilities[(edge.Source, edge.Target)] ? 0 : 1;
                    numFailed += (1 - linkUp);
                    if (linkUp == 0)
                    {
                        failureProb *= lagFailureProbabilities[(edge.Source, edge.Target)];
                        scenarioProb *= lagFailureProbabilities[(edge.Source, edge.Target)];
                    }
                    else
                    {
                        scenarioProb *= (1 - lagFailureProbabilities[(edge.Source, edge.Target)]);
                    }
                    rndCapacities[(edge.Source, edge.Target)] = edge.Capacity * linkUp;
                }
                if (maxNumFailures >= 0 && maxNumFailures < numFailed)
                {
                    return null;
                }
                if (exactNumFailures >= 0 && exactNumFailures != numFailed)
                {
                    return null;
                }
                if (failureProbThreshold >= 0 && failureProb < failureProbThreshold)
                {
                    return null;
                }
                if (scenarioProbThreshold >= 0 && scenarioProb < scenarioProbThreshold)
                {
                    return null;
                }
                return rndCapacities;
            }
        /// <summary>
        /// Returns the gap on an instance of the failure analysis use-case.
        /// </summary>
        /// <param name="optimalEncoder">the encoder for the optimal problem.</param>
        /// <param name="failureAnalysisEncoder">the encoder for the network under failure.</param>
        /// <param name="demands">the set of pre-specified demands.</param>
        /// <param name="capacities">the link capacities.</param>
        /// <param name="innerEncoding">the inner encoding we want to use.</param>
        /// <returns></returns>
        public (double, (OptimizationSolution, OptimizationSolution)) GetGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> failureAnalysisEncoder,
            Dictionary<(string, string), double> demands,
            Dictionary<(string, string), double> capacities,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT)
            {
                failureAnalysisEncoder.Solver.CleanAll();
                var failureEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                       demandEqualityConstraints: demands,
                                                                       capacityEqualityConstraints: capacities,
                                                                       noAdditionalConstraints: true,
                                                                       innerRewriteMethod: innerEncoding,
                                                                       selectedPaths: this.Paths);
                var failureAnalysisSolution = failureAnalysisEncoder.Solver.Maximize(failureEncoding.MaximizationObjective);
                var failureOptimizationSolution = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(failureAnalysisSolution);
                // solving the optimal form of the problem.
                optimalEncoder.Solver.CleanAll();
                var optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                              inputEqualityConstraints: demands,
                                                              noAdditionalConstraints: true,
                                                              numProcesses: this.NumProcesses);
                var optimalSolution = optimalEncoder.Solver.Maximize(optimalEncoding.MaximizationObjective);
                var optimizationSolutionOptimal = (TEOptimizationSolution)optimalEncoder.GetSolution(optimalSolution);
                double currGap = optimizationSolutionOptimal.MaxObjective - failureOptimizationSolution.MaxObjective;
                return (currGap, (optimizationSolutionOptimal, failureOptimizationSolution));
            }
        /// <summary>
        /// Finds a feasible solution to the optimal and the heuristic through a random search.
        /// The heuristic direct encoder is just an encoder that executes the heuristic (it can be either in optimization form or any other form. Its up to us).
        /// </summary>
        /// <returns></returns>
        private (Dictionary<(string, string), double>, Dictionary<(string, string), double>, double) RandomlyInitializedDemandsAndCapacities(
            double demandUB,
            GenericList demandList,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> HeuristicDirectEncoder,
            int maxExtraPaths,
            int maxNumFailures,
            int exactNumFailures,
            Dictionary<(string, string), double> lagFailureProbabilities,
            double failureProbThreshold,
            double scenarioProbThreshold)
            {
                var rng = new Random(Seed: 0);
                var rndDemand = new Dictionary<(string, string), double>();
                var rndCapacities = new Dictionary<(string, string), double>();
                double currGap = 0;
                int numTrials = 30;
                for (int i = 0;  i < numTrials; i++)
                {
                    bool feasible = true;
                    var currRndDemand = getRandomDemand(rng, demandUB, demandList);
                    var currRndCapacities = getRandomCapacities(maxNumFailures,
                                                                exactNumFailures,
                                                                lagFailureProbabilities,
                                                                failureProbThreshold,
                                                                scenarioProbThreshold);
                    if (currRndCapacities == null)
                    {
                        continue;
                    }
                    do
                    {
                        feasible = true;
                        try
                        {
                            var (currRndGap, _) = GetGap(optimalEncoder,
                                                         HeuristicDirectEncoder,
                                                         currRndDemand,
                                                         currRndCapacities,
                                                         InnerRewriteMethodChoice.PrimalDual);
                            if (currRndGap > currGap)
                            {
                                currGap = currRndGap;
                                rndDemand = currRndDemand;
                                rndCapacities = currRndCapacities;
                            }
                        }
                        catch
                        {
                            feasible = false;
                            throw new Exception("Got an infeasible input but there should not be a scenario where that happens.");
                        }
                    } while (!feasible);
                }
                return (rndDemand, rndCapacities, currGap);
            }
        /// <summary>
        /// Adds constraints on the link statuses so that users can specify
        /// a set of lags that are down from the get-go.
        /// </summary>
        /// <param name="solver">The solver.</param>
        /// <param name="lagStatusConstraints">The lags that are down from the get-go.</param>
        protected void AddLagStatusConstraints(ISolver<TVar, TSolution> solver, Dictionary<(string, string), bool> lagStatusConstraints)
        {
            foreach (var pair in lagStatusConstraints.Keys)
            {
                var constraint = new Polynomial<TVar>(new Term<TVar>(1, this.LagUpVariables[pair]));
                if (lagStatusConstraints[pair])
                {
                    constraint.Add(new Term<TVar>(-1));
                }
                solver.AddEqZeroConstraint(constraint);
            }
        }
        /// <summary>
        /// Uses clustering to more scalably solve the MetaOpt problem. However, to get the worst case gap you should use the non-clustered approach.
        /// RandomInitialization should only be true if we use primal-dual.
        /// </summary>
        public (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeOptimalityGapWithClustering(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> failureAnalysisEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            IDictionary<(string, string), double> capacityInits = null,
            IDictionary<string[], double> pathExtensionCapacityInits = null,
            double density = 1.0,
            double largeDemandLB = -1,
            int largeMaxDistance = -1,
            int smallMaxDistance = -1,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            int numExtraPaths = 0,
            int maxNumFailures = -1,
            int exactNumFailures = -1,
            Dictionary<(string, string), double> lagFailureProbabilities = null,
            double failureProbThreshold = -1,
            double scenarioProbThreshold = -1,
            bool randomInitialization = false,
            IEncoder<TVar, TSolution> heuristicDirectEncoder = null,
            Dictionary<(string, string, string), double> linkFailureProbabilities = null,
            bool useLinkFailures = false,
            bool ensureConnectedGraph = false,
            Dictionary<(string, string), bool> lagStatusConstraints = null,
            List<string> relayFilter = null,
            Dictionary<int, double> SRLGFailureProbabilities = null,
            Dictionary<int, HashSet<(string, string, string)>> SRLGToLinks = null,
            bool useSRLGFailures = false,
            bool doNotFailMetro = false)
            {
                this.Solver = failureAnalysisEncoder.Solver;
                if (linkFailureProbabilities != null && lagFailureProbabilities != null)
                {
                    throw new Exception("you cannot have both link and lag failure probabilities are not null");
                }
                if (linkFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("you cannot have both link and SRLG failure probabilities");
                }
                if (lagFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("you cannot have boht lag and SRLG failure probabilities");
                }
                if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
                {
                    throw new Exception("Solver mismatch between optimal and the failure scenario.");
                }
                if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
                {
                    throw new Exception("Demand list is required for primal dual encoding");
                }
                if (demandUB != -1 && perDemandUB != null)
                {
                    throw new Exception("If global demand ub is enabled, then perDemandUB should be null");
                }
                if (pathType == PathType.Predetermined && selectedPaths == null)
                {
                    throw new Exception("If path type is predetermined, then selectedPaths should be null");
                }
                if (pathType != PathType.Predetermined && selectedPaths != null)
                {
                    throw new Exception("If path type is not predetermined, then selected paths should be null");
                }
                if (useLinkFailures)
                {
                    Debug.Assert(this.Topology.GetAllEdges().Count() == this.Topology.edgeLinks.Count(),
                    "We need these numbers to match, each edge needs to have at least one link specified if your using this setting.");
                }
                if (SRLGsToLinks != null)
                {
                    this.SRLGsToLinks = SRLGsToLinks;
                }
                if (useSRLGFailures && !useLinkFailures)
                {
                    throw new Exception("We need useLinkFailures to be true if we want to use SRLGFailures");
                }
                CheckDensityAndLocalityInputs(innerEncoding, density, largeDemandLB, largeMaxDistance, smallMaxDistance, randomInitialization);
                if (density < 1.0)
                {
                    throw new Exception("Density constraint is not implemented completely for the clustering approach.");
                }
                CheckClusteringIsValid(clusters);
                if (constrainedDemands == null)
                {
                    constrainedDemands = new Dictionary<(string, string), double>();
                }
                if (selectedPaths == null)
                {
                    this.SetPath(numExtraPaths, pathType, selectedPaths, relayFilter);
                }
                else
                {
                    throw new Exception("For clustering I don't have pre-specified paths enabled yet.");
                }
                this.DoNotFailMetro = doNotFailMetro;
                var pathSubset = VerifyPaths(this.Paths, numExtraPaths);
                Dictionary<(string, string), double> rndDemand = null;
                var timer = Stopwatch.StartNew();
                double currGap = 0;
                Dictionary<(string, string), double> rndCapacities = null;
                if (randomInitialization)
                {
                    Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
                    (rndDemand, rndCapacities, currGap) = RandomlyInitializedDemandsAndCapacities(demandUB,
                                                                                                  demandList,
                                                                                                  optimalEncoder,
                                                                                                  heuristicDirectEncoder,
                                                                                                  numExtraPaths,
                                                                                                  maxNumFailures,
                                                                                                  exactNumFailures,
                                                                                                  lagFailureProbabilities,
                                                                                                  failureProbThreshold,
                                                                                                  scenarioProbThreshold);
                }
                timer.Stop();
                var solver = optimalEncoder.Solver;
                var timeout = solver.GetTimeout();
                solver.CleanAll(timeout: timeout / clusters.Count());
                if (randomInitialization)
                {
                    solver.AppendToStoreProgressFile(timer.ElapsedMilliseconds, currGap, reset: false);
                }
                // Next: need to create all the necessary variables for the two encodings.
                Logger.Info("creating demand variables");
                (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                Logger.Info("creating capacity enforcers");
                this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);
                this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
                if (useLinkFailures)
                {
                    this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
                }
                if (useSRLGFailures)
                {
                    this.SRLGUpVariables = CreateSRLGUpVariables(solver);
                }
                this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);
                if (lagStatusConstraints != null)
                {
                    AddLagStatusConstraints(solver, lagStatusConstraints);
                }
                // The approach I am going to take is to allow the demands to determined cluster by cluster BUT the capacity variables are assigned each time.
                // This means the size of the clusters are not fully getting reduced because the capacity variables are still global. Its just the demand,
                // that is getting clustered. The reason I am doing this is because we have global constraints on the number/probability of failures.
                // Clustering would require us to convert these constraints into within cluster constraints which is difficult. What I am suggesting here
                // is a compromise.
                Logger.Info("Creating optimal encoding");
                var optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                               preInputVariables: this.DemandEnforcers,
                                                               innerEncoding: innerEncoding,
                                                               numProcesses: this.NumProcesses,
                                                               inputEqualityConstraints: this.LocalityConstrainedDemands,
                                                               noAdditionalConstraints: true,
                                                               pathType: pathType,
                                                               selectedPaths: pathSubset,
                                                               historicInputConstraints: historicDemandConstraints);
                Logger.Info("Creating failure encoding");
                var failureEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                      preDemandVariables: this.DemandEnforcers,
                                                                      preCapVariables: this.CapacityEnforcers,
                                                                      prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                                                                      demandEqualityConstraints: this.LocalityConstrainedDemands,
                                                                      noAdditionalConstraints: false,
                                                                      innerRewriteMethod: innerEncoding,
                                                                      pathType: pathType,
                                                                      selectedPaths: this.Paths,
                                                                      historicDemandConstraints: historicDemandConstraints);
                FailureAnalysisOptimizationSolution failureSol = null;
                TEOptimizationSolution optSol = null;
                if (perDemandUB != null)
                {
                    EnsureDemandUB(solver, perDemandUB);
                }
                else
                {
                    EnsureDemandUB(solver, demandUB);
                }
                Logger.Info("adding equality constraints");
                EnsureDemandEquality(solver, constrainedDemands);
                Logger.Info("Adding density constraint: max density " + density);
                EnsureDensityConstraint(solver, density);

                Logger.Info("adding the individual constraints to model the failure scenarios");
                if (!useLinkFailures)
                {
                    AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
                else
                {
                    AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
                Logger.Info("Now adding constraint for max number of failures");
                if (maxNumFailures >= 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraint(solver, maxNumFailures);
                }
                if (maxNumFailures >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraintsOnLinks(solver, maxNumFailures);
                }
                if (maxNumFailures >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddMaxNumFailureConstraintOnSRLG(solver, maxNumFailures);
                }
                if (exactNumFailures >= 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraint(solver, exactNumFailures, exact: true);
                }
                if (exactNumFailures >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddMaxNumFailureConstraintsOnLinks(solver, exactNumFailures, exact: true);
                }
                if (exactNumFailures >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddMaxNumFailureConstraintOnSRLG(solver, exactNumFailures, exact: true);
                }
                Logger.Info("adding the failure probability constraint");
                if (scenarioProbThreshold != -1 && !useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraints(solver, lagFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraintOnLinks(solver, linkFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && useSRLGFailures)
                {
                    AddScenarioProbabilityConstraintsOnSRLG(solver, SRLGFailureProbabilities, scenarioProbThreshold);
                }
                if (failureProbThreshold >= 0 && !useLinkFailures && !useSRLGFailures)
                {
                    AddFailureProbabilityConstraints(solver, lagFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (failureProbThreshold >= 0 && useLinkFailures && !useSRLGFailures)
                {
                    AddFailureProbabilityConstraintOnLinks(solver, linkFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (useSRLGFailures)
                {
                    AddSRLGConstraints(solver, lagStatusConstraints);
                }
                if (lagStatusConstraints != null)
                {
                    AddLagStatusConstraints(solver, lagStatusConstraints);
                }
                if (demandUB < 0)
                {
                    demandUB = (this.maxNumPath + numExtraPaths) * this.Topology.MaxCapacity();
                }
                var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
                pairNameToConstraintMapping = AddViableDemandConstraints(randomInitialization, solver, constrainedDemands, rndDemand);
                var demandMatrix = new Dictionary<(string, string), double>();
                foreach (var cluster in clusters)
                {
                    var consideredPairs = new HashSet<(string, string)>();
                    Logger.Info("finding adversarial demand for cluster with {0} nodes and {1} edges", cluster.GetAllNodes().Count(), cluster.GetAllEdges().Count());
                    foreach (var pair in cluster.GetNodePairs())
                    {
                        if (checkIfPairIsConstrained(constrainedDemands, pair) || !this.Paths.ContainsKey(pair))
                        {
                            continue;
                        }
                        if (!randomInitialization)
                        {
                            if (perDemandUB != null && !perDemandUB.ContainsKey(pair))
                            {
                                perDemandUB[pair] = 0;
                            }
                            solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], perDemandUB != null && perDemandUB.ContainsKey(pair) ? perDemandUB[pair] : demandUB);
                        }
                        else
                        {
                            solver.RemoveConstraint(pairNameToConstraintMapping[pair]);
                        }
                        consideredPairs.Add(pair);
                    }
                    Logger.Info("setting the objective");
                    var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, failureEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                    foreach (var pair in consideredPairs)
                    {
                        var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                        demandMatrix[pair] = demandlvl;
                        AddSingleDemandEquality(solver, pair, demandlvl);
                    }
                }
                for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
                {
                    for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                    {
                        var consideredPairs = new HashSet<(string, string)>();
                        Logger.Info(string.Format("inter-cluster adversarial demand between cluster {0} and cluster {1}", cid1, cid2));
                        var cluster1Nodes = clusters[cid1].GetAllNodes().ToList();
                        var cluster2Nodes = clusters[cid2].GetAllNodes().ToList();
                        bool neighbor = false;
                        foreach (var node1 in cluster1Nodes)
                        {
                            foreach (var node2 in cluster2Nodes)
                            {
                                if (this.Topology.ContaintsEdge(node1, node2))
                                {
                                    neighbor = true;
                                    break;
                                }
                            }
                            if (neighbor)
                            {
                                break;
                            }
                        }
                        if (!neighbor)
                        {
                            Logger.Debug("skipping the cluster pairs since they are not neighbors");
                            continue;
                        }
                        foreach (var node1 in cluster1Nodes)
                        {
                            foreach (var node2 in cluster2Nodes)
                            {
                                if (!this.Paths.ContainsKey((node1, node2)) || checkIfPairIsConstrained(constrainedDemands, (node1, node2)))
                                {
                                    continue;
                                }
                                if (!randomInitialization)
                                {
                                    if (perDemandUB != null && !perDemandUB.ContainsKey((node1, node2)))
                                    {
                                        perDemandUB[(node1, node2)] = 0;
                                    }
                                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], perDemandUB != null && perDemandUB.ContainsKey((node1, node2)) ? perDemandUB[(node1, node2)] : demandUB);
                                }
                                else
                                {
                                    solver.RemoveConstraint(pairNameToConstraintMapping[(node1, node2)]);
                                }
                                consideredPairs.Add((node1, node2));
                            }
                        }
                        Logger.Info("setting the objective");
                        var objective = new Polynomial<TVar>(
                            new Term<TVar>(1, optimalEncoding.GlobalObjective),
                            new Term<TVar>(-1, failureEncoding.GlobalObjective));
                        var solution = solver.Maximize(objective, reset: true);
                        optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                        failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                        foreach (var pair in consideredPairs)
                        {
                            var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                            demandMatrix[pair] = Math.Round(demandlvl);
                            AddSingleDemandEquality(solver, pair, demandlvl);
                        }
                    }
                }
                foreach (var pair in this.Paths.Keys)
                {
                    if (!demandMatrix.ContainsKey(pair) && !constrainedDemands.ContainsKey(pair))
                    {
                        demandMatrix[pair] = 0;
                    }
                    else
                    {
                        if (demandMatrix.ContainsKey(pair))
                        {
                            Logger.Info("Check this, it may be a bug.");
                        }
                        else
                        {
                            demandMatrix[pair] = Math.Round(constrainedDemands[pair]);
                        }
                    }
                }
                optimalEncoder.Solver.CleanAll(timeout: timeout);
                (optSol, failureSol) = MaximizeOptimalityGap(optimalEncoder,
                                                             failureAnalysisEncoder,
                                                             innerEncoding: innerEncoding,
                                                             demandList: demandList,
                                                             constrainedDemands: demandMatrix,
                                                             demandInits: demandInits,
                                                             capacityInits: capacityInits,
                                                             pathExtensionCapacityInits: pathExtensionCapacityInits,
                                                             density: density,
                                                             largeDemandLB: largeDemandLB,
                                                             largeMaxDistance: largeMaxDistance,
                                                             smallMaxDistance: smallMaxDistance,
                                                             selectedPaths: this.Paths,
                                                             pathType: PathType.Predetermined,
                                                             historicDemandConstraints: historicDemandConstraints,
                                                             numExtraPaths: numExtraPaths,
                                                             maxNumFailures: maxNumFailures,
                                                             exactNumFailures: exactNumFailures,
                                                             lagFailureProbabilities: lagFailureProbabilities,
                                                             failureProbThreshold: failureProbThreshold,
                                                             scenarioProbThreshold: scenarioProbThreshold,
                                                             linkFailureProbabilities: linkFailureProbabilities,
                                                             useLinkFailures: useLinkFailures,
                                                             ensureConnectedGraph: ensureConnectedGraph,
                                                             lagStatusConstraints: lagStatusConstraints,
                                                             optimalIsConstant: true,
                                                             SRLGFailureProbabilities: SRLGFailureProbabilities,
                                                             SRLGsToLinks: SRLGsToLinks,
                                                             useSRLGFailures: useSRLGFailures,
                                                             doNotFailMetro: doNotFailMetro);
                return (optSol, failureSol);
            }
        /// <summary>
        /// Uses clustering to more scalably solve the MetaOpt problem. However, to get the worst-case gap you should use the non-clustered approach.
        /// </summary>
        /// TODO-engineering: this implementation has not been tested properly.
        public (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeMLUOptimalityGapWithClustering(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> failureAnalysisEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            IDictionary<(string, string), double> capacityInits = null,
            IDictionary<string[], double> pathExtensionCapacityInits = null,
            double density = 1.0,
            double largeDemandLB = -1,
            int largeMaxDistance = -1,
            int smallMaxDistance = -1,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            int numExtraPaths = 0,
            int maxNumFailures = -1,
            int exactNumFailures = -1,
            Dictionary<(string, string), double> lagFailureProbabilities = null,
            double failureProbThreshold = -1,
            double scenarioProbThreshold = -1,
            IEncoder<TVar, TSolution> HueristicDirectEncoder = null,
            Dictionary<(string, string, string), double> linkFailureProbabilities = null,
            bool useLinkFailures = false,
            bool ensureConnectedGraph = false,
            Dictionary<(string, string), bool> lagStatusConstraints = null,
            List<string> relayFilter = null)
        {
            this.Solver = failureAnalysisEncoder.Solver;
            if (lagFailureProbabilities != null && linkFailureProbabilities != null)
            {
                throw new Exception("you cannot have both link and lag failure probabilities that are not null");
            }
            // Start with all necessary checks.
            if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and the failure scenario.");
            }
            if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
            {
                throw new Exception("Demand list is required for primal dual encoding.");
            }
            if (demandUB != -1 && perDemandUB != null)
            {
                throw new Exception("if global ub is enabled, then perdemandUB should be null");
            }
            if (pathType == PathType.Predetermined && selectedPaths == null)
            {
                throw new Exception("if paht type is predetermined, then selected paths should not be null");
            }
            if (pathType != PathType.Predetermined && selectedPaths != null)
            {
                throw new Exception("if path type is not predetermined, then selected paths should be null");
            }
            if (useLinkFailures)
            {
                Debug.Assert(this.Topology.GetAllEdges().Count() == this.Topology.edgeLinks.Count(),
                "We need these numbers to match, each edge needs to have at least one lag specified if your using this setting.");
            }
            CheckDensityAndLocalityInputs(innerEncoding, density, largeDemandLB, largeMaxDistance, smallMaxDistance, false);
            if (density < 1.0)
            {
                throw new Exception("Density constraint is not implemented completely for the clustering approach");
            }
            CheckClusteringIsValid(clusters);
            if (constrainedDemands == null)
            {
                constrainedDemands = new Dictionary<(string, string), double>();
            }
            if (selectedPaths == null)
            {
                this.SetPath(numExtraPaths, pathType, selectedPaths, relayFilter);
            }
            else
            {
                throw new Exception("For clustering I don't have pre-specified paths enabled yet.");
            }
            var pathSubset = VerifyPaths(this.Paths, numExtraPaths);
            Dictionary<(string, string), double> rndDemand = null;
            var timer = Stopwatch.StartNew();
            timer.Stop();
            var solver = optimalEncoder.Solver;
            var timeout = solver.GetTimeout();
            solver.CleanAll(timeout: timeout / clusters.Count());
            // Next: need to create all the necessary variables for the two encodings.
            Logger.Info("creatng demand variables");
            (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
            Logger.Info("Creating capacity enforcers");
            this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
            this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits, numExtraPaths);
            // Now I need to create the variables that encode whether each individual link is up.
            this.LagUpVariables = CreateLagUpVariables(solver, this.Topology);
            if (useLinkFailures)
            {
                this.LinkUpVariables = CreateLinkUpVariables(solver, this.Topology);
            }
            this.PathUpVariables = CreatePathUpVariables(solver, this.Topology);
            if (lagStatusConstraints != null)
            {
                AddLagStatusConstraints(solver, lagStatusConstraints);
            }
            // The approach I am going to take is to allow the demands to be determined cluster by cluster BUT the capacity variables are assigned each time.
            // This means the size of the clusters are not fully getting reduced because the capacity variables are still global. Its just the demand,
            // that is getting clustered. The reason I am doing this is because we have global constraints on the number/probability of failures.
            // Clustering would require us to convert these constraints into wihin cluster constraints which is difficult. What I am suggesting here is a compromise.
            //  We can revisit it if needed.
            Logger.Info("Creating optimal encoding.");
            var optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                          preInputVariables: this.DemandEnforcers,
                                                          innerEncoding: innerEncoding,
                                                          numProcesses: this.NumProcesses,
                                                          inputEqualityConstraints: this.LocalityConstrainedDemands,
                                                          noAdditionalConstraints: true,
                                                          pathType: pathType,
                                                          selectedPaths: pathSubset,
                                                          historicInputConstraints: historicDemandConstraints);
            Logger.Info("Creating failure encoding");
            var failureEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                  preDemandVariables: this.DemandEnforcers,
                                                                  preCapVariables: this.CapacityEnforcers,
                                                                  prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                                                                  demandEqualityConstraints: this.LocalityConstrainedDemands,
                                                                  noAdditionalConstraints: false,
                                                                  innerRewriteMethod: innerEncoding,
                                                                  pathType: pathType,
                                                                  selectedPaths: this.Paths,
                                                                  historicDemandConstraints: historicDemandConstraints);
            FailureAnalysisOptimizationSolution failureSol = null;
            TEOptimizationSolution optSol = null;
            Logger.Info("adding constraints for upper bound on demands");
            if (perDemandUB != null)
            {
                EnsureDemandUB(solver, perDemandUB);
            }
            else
            {
                EnsureDemandUB(solver, demandUB);
            }
            Logger.Info("adding equality constraints");
            EnsureDemandEquality(solver, constrainedDemands);
            Logger.Info("Adding density constraints: max density " + density);
            EnsureDensityConstraint(solver, density);
            Logger.Info("adding the individual constraints to model the failure scenarios");
            if (!useLinkFailures)
            {
                AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
            }
            else
            {
                AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
            }
            Logger.Info("Now adding constraint for max number of failures");
            if (maxNumFailures >= 0 && !useLinkFailures)
            {
                AddMaxNumFailureConstraint(solver, maxNumFailures);
            }
            if (maxNumFailures >= 0 && useLinkFailures)
            {
                AddMaxNumFailureConstraintsOnLinks(solver, maxNumFailures);
            }
            if (exactNumFailures >= 0 && !useLinkFailures)
            {
                AddMaxNumFailureConstraint(solver, exactNumFailures, exact: true);
            }
            if (exactNumFailures >= 0 && useLinkFailures)
            {
                AddMaxNumFailureConstraintsOnLinks(solver, exactNumFailures, exact: true);
            }
            Logger.Info("adding the failure probability constraint");
            if (scenarioProbThreshold != -1 && !useLinkFailures)
            {
                AddScenarioProbabilityConstraints(solver, lagFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
            }
            if (scenarioProbThreshold != -1 && useLinkFailures)
            {
                AddScenarioProbabilityConstraintOnLinks(solver, linkFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
            }
            if (failureProbThreshold >= 0 && !useLinkFailures)
            {
                AddFailureProbabilityConstraints(solver, lagFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
            }
            if (failureProbThreshold >= 0 && useLinkFailures)
            {
                AddFailureProbabilityConstraintOnLinks(solver, linkFailureProbabilities, failureProbThreshold, lagStatusConstraints: lagStatusConstraints);
            }
            if (demandUB < 0)
            {
                demandUB = (this.maxNumPath + numExtraPaths) * this.Topology.MaxCapacity();
            }
            var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
            pairNameToConstraintMapping = AddViableDemandConstraints(false, solver, constrainedDemands, rndDemand);
            var demandMatrix = new Dictionary<(string, string), double>();
            foreach (var cluster in clusters)
            {
                var consideredPairs = new HashSet<(string, string)>();
                Logger.Info("finding adversarial demand for cluster with {0} nodes and {1} edges", cluster.GetAllNodes().Count(), cluster.GetAllEdges().Count());
                foreach (var pair in cluster.GetNodePairs())
                {
                    if (checkIfPairIsConstrained(constrainedDemands, pair) || this.Paths.ContainsKey(pair))
                    {
                        continue;
                    }
                    if (perDemandUB != null && !perDemandUB.ContainsKey(pair))
                    {
                        perDemandUB[pair] = 0;
                    }
                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], perDemandUB != null && perDemandUB.ContainsKey(pair) ? perDemandUB[pair] : demandUB);
                    consideredPairs.Add(pair);
                }
                Logger.Info("setting the objective");
                var objective = new Polynomial<TVar>(
                    new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                    new Term<TVar>(1, failureEncoding.GlobalObjective));
                var solution = solver.Maximize(objective, reset: true);
                optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                foreach (var pair in consideredPairs)
                {
                    var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                    demandMatrix[pair] = demandlvl;
                    AddSingleDemandEquality(solver, pair, demandlvl);
                }
            }
            for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
            {
                for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                {
                    var consideredPairs = new HashSet<(string, string)>();
                    Logger.Info(string.Format("inter-cluster adversarial demand between cluster {0} and cluster {1}", cid1, cid2));
                    var cluster1Nodes = clusters[cid1].GetAllNodes().ToList();
                    var cluster2Nodes = clusters[cid2].GetAllNodes().ToList();
                    bool neighbor = false;
                    foreach (var node1 in cluster1Nodes)
                    {
                        foreach (var node2 in cluster2Nodes)
                        {
                            if (this.Topology.ContaintsEdge(node1, node2))
                            {
                                neighbor = true;
                                break;
                            }
                        }
                        if (neighbor)
                        {
                            break;
                        }
                    }
                    if (!neighbor)
                    {
                        Logger.Debug("skipping the cluster pairs since they are not neighbors");
                        continue;
                    }
                    foreach (var node1 in cluster1Nodes)
                    {
                        foreach (var node2 in cluster2Nodes)
                        {
                            if (!this.Paths.ContainsKey((node1, node2)) || checkIfPairIsConstrained(constrainedDemands, (node1, node2)))
                            {
                                continue;
                            }
                            if (perDemandUB != null && !perDemandUB.ContainsKey((node1, node2)))
                            {
                                perDemandUB[(node1, node2)] = 0;
                            }
                            solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], perDemandUB != null && perDemandUB.ContainsKey((node1, node2)) ? perDemandUB[(node1, node2)] : demandUB);
                            consideredPairs.Add((node1, node2));
                        }
                    }
                    Logger.Info("setting the objective");
                    var objective = new Polynomial<TVar>(
                        new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(1, failureEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                    foreach (var pair in consideredPairs)
                    {
                        var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                        demandMatrix[pair] = Math.Round(demandlvl);
                        AddSingleDemandEquality(solver, pair, demandlvl);
                    }
                }
            }
            foreach (var pair in this.Paths.Keys)
            {
                if (!demandMatrix.ContainsKey(pair) && !constrainedDemands.ContainsKey(pair))
                {
                    demandMatrix[pair] = 0;
                }
                else
                {
                    if (demandMatrix.ContainsKey(pair))
                    {
                        Logger.Info("check this, it may be a bug.");
                    }
                    else
                    {
                        demandMatrix[pair] = Math.Round(constrainedDemands[pair]);
                    }
                }
            }
            optimalEncoder.Solver.CleanAll(timeout: timeout);
            (optSol, failureSol) = MaximizeMLUOptimalityGap(optimalEncoder,
                                                            failureAnalysisEncoder,
                                                            innerEncoding: innerEncoding,
                                                            demandList: demandList,
                                                            constrainedDemands: demandMatrix,
                                                            demandInits: demandInits,
                                                            capacityInits: capacityInits,
                                                            pathExtensionCapacityInits: pathExtensionCapacityInits,
                                                            density: density,
                                                            largeDemandLB: largeDemandLB,
                                                            largeMaxDistance: largeMaxDistance,
                                                            smallMaxDistance: smallMaxDistance,
                                                            selectedPaths: this.Paths,
                                                            pathType: PathType.Predetermined,
                                                            historicDemandConstraints: historicDemandConstraints,
                                                            numExtraPaths: numExtraPaths,
                                                            maxNumFailures: maxNumFailures,
                                                            exactNumFailures: exactNumFailures,
                                                            lagFailureProbabilities: lagFailureProbabilities,
                                                            failureProbThreshold: failureProbThreshold,
                                                            scenarioProbThreshold: scenarioProbThreshold,
                                                            linkFailureProbabilities: linkFailureProbabilities,
                                                            useLinkFailures: useLinkFailures,
                                                            ensureConnectedGraph: ensureConnectedGraph,
                                                            lagStatusConstraints: lagStatusConstraints,
                                                            optimalIsConstant: true);
            return (optSol, failureSol);
        }
        /// <summary>
        /// models the impact of failures.
        /// </summary>
        /// <param name="solver">the solver object.</param>
        /// <param name="ensureAtLeastOnePathUp">to check whether at least one path is up.</param>
        protected void AddImpactOfLinkFailuresOnCapacity(ISolver<TVar, TSolution> solver, bool ensureAtLeastOnePathUp = false)
        {
            Logger.Info("Adding the capacity bounds with the link up variables.");
            foreach (var lag in this.Topology.GetAllEdges())
            {
                var capacity = this.Topology.GetEdge(lag.Source, lag.Target).Capacity;
                var trueCap = this.CapacityEnforcers[(lag.Source, lag.Target)].Copy().Negate();
                trueCap.Add(new Term<TVar>(capacity));
                foreach (var (index, link) in this.Topology.edgeLinks[(lag.Source, lag.Target)])
                {
                    trueCap.Add(new Term<TVar>(-capacity * 1.0 / this.Topology.edgeLinks[(lag.Source, lag.Target)].Count(), this.LinkUpVariables[(lag.Source, lag.Target, link.Item1)]));
                }
                solver.AddEqZeroConstraint(trueCap);
            }
            Logger.Info("Now I have to add constraints that ensure the lagup varaibles are correct");
            foreach (var (lag, indicator) in this.LagUpVariables)
            {
                var auxVariable = this.Solver.CreateVariable("aux_lag_" + lag.Item1 + "_" + lag.Item2, lb: 0, ub: this.Topology.edgeLinks[lag].Count() - 1, type: GRB.INTEGER);
                var overallConstraint = new Polynomial<TVar>(new Term<TVar>(-1 * this.Topology.edgeLinks[lag].Count(), indicator));
                overallConstraint.Add(new Term<TVar>(-1, auxVariable));
                foreach (var (index, link) in this.Topology.edgeLinks[lag])
                {
                    overallConstraint.Add(new Term<TVar>(1, this.LinkUpVariables[(lag.Item1, lag.Item2, link.Item1)]));
                    var individualConstraint = new Polynomial<TVar>(new Term<TVar>(-1, this.LinkUpVariables[(lag.Item1, lag.Item2, link.Item1)]));
                    individualConstraint.Add(new Term<TVar>(1, indicator));
                    solver.AddLeqZeroConstraint(individualConstraint);
                }
                solver.AddEqZeroConstraint(overallConstraint);
            }
            Logger.Info("Now I am adding the constraint that ensures path constraints are met");
            this.SetPathConstraintsForFailures(solver, ensureAtLeastOnePathUp);
        }
        /// <summary>
        /// Adds the path constraints that bring the path down and switch to
        /// backup paths if a link along that path fails.
        /// for the backup path to be activated the link has to come down entirely, it is not enough for a lag to fail.
        /// </summary>
        /// <param name="solver">the solver object.</param>
        /// <param name="ensureAtLeastOneUp">whether we want to ensure there is at least on path up between each node-pair.</param>
        protected virtual void SetPathConstraintsForFailures(ISolver<TVar, TSolution> solver, bool ensureAtLeastOneUp = false)
        {
            foreach (var (pair, paths) in this.Paths)
            {
                Logger.Info("Computing the sum of all paths + setting lag up constraints");
                var sumOfPathsUpVariables = new Polynomial<TVar>();
                var atLeastOneUpConstraint = new Polynomial<TVar>();
                for (var i = 0; i < paths.Length; i++)
                {
                    // TODO: check that path i is not longer or equal to i + 1.
                    var path = paths[i];
                    if (ensureAtLeastOneUp)
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                    }
                    var firstPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(-1, this.PathUpVariables[path]));
                    var secondPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(-1 * this.Topology.GetAllEdges().Count(), this.PathUpVariables[path]));
                    for (int k = 0; k < path.Length - 1; k++)
                    {
                        var source = path[k];
                        var dest = path[k + 1];
                        firstPathUpConstraint.Add(new Term<TVar>(1, this.LagUpVariables[(source, dest)]));
                        secondPathUpConstraint.Add(new Term<TVar>(1, this.LagUpVariables[(source, dest)]));
                    }
                    solver.AddLeqZeroConstraint(secondPathUpConstraint);
                    solver.AddLeqZeroConstraint(firstPathUpConstraint);
                    sumOfPathsUpVariables.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                    var sumDisabledPathSoFar = sumOfPathsUpVariables.Copy();
                    sumDisabledPathSoFar.Add(new Term<TVar>(this.maxNumPath - i));
                    var aux = EncodingUtils<TVar, TSolution>.IsLeq(this.Solver,
                                                                   new Polynomial<TVar>(new Term<TVar>(0)),
                                                                   sumDisabledPathSoFar,
                                                                   this.maxNumPath * 10, 0.1);
                    this.AuxiliaryVariables.Append(aux);
                    Debug.Assert(this.PathExtensionCapacityEnforcers.ContainsKey(paths[i]));
                    var lowerBound = this.PathExtensionCapacityEnforcers[paths[i]].Copy().Negate();
                    lowerBound.Add(new Term<TVar>(this.CapacityUpperBound, aux));
                    solver.AddEqZeroConstraint(lowerBound);
                }
                if (ensureAtLeastOneUp)
                {
                    if (paths.Length < 1)
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * (paths.Length)));
                    }
                    else
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * (paths.Length - 1)));
                    }
                    solver.AddLeqZeroConstraint(atLeastOneUpConstraint);
                }
            }
        }
        /// <summary>
        /// Adds the impact of the lag failure on the total capacity available on the lag.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="ensureAtLeastOnePathUp"></param>
        protected void AddImpactOfFailuresOnCapacity(ISolver<TVar, TSolution> solver, bool ensureAtLeastOnePathUp = false)
        {
            Logger.Info("Adding the capacity bounds using lag up variables");
            foreach (var (lag, indicator) in this.LagUpVariables)
            {
                var capacity = this.Topology.GetEdge(lag.Item1, lag.Item2).Capacity;
                var trueCap = this.CapacityEnforcers[lag].Copy().Negate();
                trueCap.Add(new Term<TVar>(capacity));
                trueCap.Add(new Term<TVar>(-capacity, indicator));
                solver.AddEqZeroConstraint(trueCap);
            }
            Logger.Info("new adding the path extension capacity constraints based on the pathUpVariables");
            this.SetPathConstraintsForFailures(solver, ensureAtLeastOnePathUp);
        }
        /// <summary>
        /// Returns the lag down events after solving the problem.
        /// </summary>
        public List<bool> GetLinkDownEvents()
        {
            return this.LagUpResults;
        }
        /// <summary>
        /// Returns the pathset we used in this computation.
        /// </summary>
        public Dictionary<(string, string), string[][]> ReturnPaths()
        {
            return this.Paths;
        }
        /// <summary>
        /// Returns the lag down events after we solve the problem.
        /// </summary>
        public List<bool> GetLagDownEvents(TSolution solution)
        {
            var res = new List<bool>();
            foreach (var (key, variable) in this.LagUpVariables)
            {
                res.Add(this.Solver.GetVariable(solution, variable) < 0.001 ? false : true);
            }
            this.LagUpResults = res;
            return res;
        }
        /// <summary>
        /// Adds constraints on the SRLGs so that all links belonging to the SRLG go down when it goes down.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="lagStatusConstraints"></param>
        protected void AddSRLGConstraints(ISolver<TVar, TSolution> solver, Dictionary<(string, string), bool> lagStatusConstraints = null)
        {
            foreach (var srlg in this.SRLGsToLinks.Keys)
            {
                foreach (var link in this.SRLGsToLinks[srlg])
                {
                    if (lagStatusConstraints.ContainsKey((link.Item1, link.Item2)))
                    {
                        continue;
                    }
                    if (this.DoNotFailMetro && IsLagMetro((link.Item1, link.Item2)))
                    {
                        continue;
                    }
                    if (!this.LinkUpVariables.ContainsKey(link))
                    {
                        continue;
                    }
                    var constraint = new Polynomial<TVar>(new Term<TVar>(1, this.LinkUpVariables[link]));
                    constraint.Add(new Term<TVar>(-1, this.SRLGUpVariables[srlg]));
                    solver.AddEqZeroConstraint(constraint);
                }
            }
        }
        /// <summary>
        /// Adds failure probability constraints on SRLGs.
        /// </summary>
        protected void AddFailureProbabilityConstraintOnSRLG(ISolver<TVar, TSolution> solver, Dictionary<int, double> SRLGFailureProbabilities, double failureProbThreshold)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (srlg, probability) in SRLGFailureProbabilities)
            {
                var srlgUp = this.SRLGUpVariables[srlg];
                var srlgDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-15)), srlgUp);
                constraint.Add(srlgDownTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        /// <summary>
        /// This function adds constraints on probabilities for links.
        /// </summary>
        protected void AddFailureProbabilityConstraintOnLinks(ISolver<TVar, TSolution> solver, Dictionary<(string, string, string), double> linkFailureProbabilities, double failureProbThreshold, Dictionary<(string, string), bool> lagStatusConstraints = null)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (link, probability) in linkFailureProbabilities)
            {
                if (lagStatusConstraints != null & lagStatusConstraints.ContainsKey((link.Item1, link.Item2)) && lagStatusConstraints.ContainsKey((link.Item1, link.Item2)))
                {
                    continue;
                }
                if (this.DoNotFailMetro && IsLagMetro((link.Item1, link.Item2)))
                {
                    continue;
                }
                var linkUp = this.LinkUpVariables[link];
                var linkDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-10)), linkUp);
                constraint.Add(linkDownTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        /// <summary>
        /// Adds probability constraints on lags.
        /// </summary>
        protected void AddFailureProbabilityConstraints(ISolver<TVar, TSolution> solver, Dictionary<(string, string), double> lagFailureProbabilities, double failureProbThreshold, Dictionary<(string, string), bool> lagStatusConstraints = null)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (lag, probability) in lagFailureProbabilities)
            {
                if (lagStatusConstraints != null && lagStatusConstraints.ContainsKey((lag.Item1, lag.Item2)) && lagStatusConstraints.ContainsKey((lag.Item1, lag.Item2)))
                {
                    continue;
                }
                if (this.DoNotFailMetro && IsLagMetro(lag))
                {
                    continue;
                }
                var lagUp = this.LagUpVariables[lag];
                var lagDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-10)), lagUp);
                constraint.Add(lagDownTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        /// <summary>
        /// Adds scenario probability constraints.
        /// </summary>
        protected void AddScenarioProbabilityConstraintsOnSRLG(ISolver<TVar, TSolution> solver, Dictionary<int, double> scenarioProbabilities, double failureProbThreshold)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (srlg, probability) in scenarioProbabilities)
            {
                var srlgUp = this.SRLGUpVariables[srlg];
                var srlgDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-10)), srlgUp);
                constraint.Add(srlgDownTerm);
                constraint.Add(new Term<TVar>(-1 * Math.Log(Math.Max(1 - probability, 1e-10))));
                var srlgUpTerm = new Term<TVar>(Math.Log(Math.Max(1 - probability, 1e-10)), srlgUp);
                constraint.Add(srlgUpTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        private bool IsLagMetro((string, string) lag)
        {
            throw new Exception("you need to implement this function yourself based on your own topology naming convention");
        }
        /// <summary>
        /// This looks at the probability of specific scenarios:
        /// so it not only enforces that the link down probabilities hold but also enforces that link up probabilities hold.
        /// </summary>
        protected void AddScenarioProbabilityConstraintOnLinks(ISolver<TVar, TSolution> solver, Dictionary<(string, string, string), double> scenarioProbabilities, double failureProbThreshold, Dictionary<(string, string), bool> lagStatusConstraints)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (link, probability) in scenarioProbabilities)
            {
                if (lagStatusConstraints != null && lagStatusConstraints.ContainsKey((link.Item1, link.Item2)) && lagStatusConstraints.ContainsKey((link.Item1, link.Item2)))
                {
                    continue;
                }
                if (this.DoNotFailMetro && IsLagMetro((link.Item1, link.Item2)))
                {
                    continue;
                }
                var linkUp = this.LinkUpVariables[link];
                var linkDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-10)), linkUp);
                constraint.Add(linkDownTerm);
                constraint.Add(new Term<TVar>(-1 * Math.Log(Math.Max(1 - probability, 1e-10))));
                var linkUpTerm = new Term<TVar>(Math.Log(Math.Max(1 - probability, 1e-10)), linkUp);
                constraint.Add(linkUpTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        /// <summary>
        /// Adds the scenario probability constraints.
        /// </summary>
        protected void AddScenarioProbabilityConstraints(ISolver<TVar, TSolution> solver, Dictionary<(string, string), double> scenarioProbabilities, double failureProbThreshold, Dictionary<(string, string), bool> lagStatusConstraints)
        {
            var constraint = new Polynomial<TVar>(new Term<TVar>(Math.Log(failureProbThreshold)));
            foreach (var (lag, probability) in scenarioProbabilities)
            {
                if (lagStatusConstraints != null && lagStatusConstraints.ContainsKey((lag.Item1, lag.Item2)) && lagStatusConstraints.ContainsKey((lag.Item1, lag.Item2)))
                {
                    continue;
                }
                if (this.DoNotFailMetro && IsLagMetro(lag))
                {
                    continue;
                }
                var lagUp = this.LagUpVariables[lag];
                var lagDownTerm = new Term<TVar>(-1 * Math.Log(Math.Max(probability, 1e-10)), lagUp);
                constraint.Add(lagDownTerm);
                constraint.Add(new Term<TVar>(-1 * Math.Log(Math.Max(1 - probability, 1e-10))));
                var lagUpTerm = new Term<TVar>(Math.Log(Math.Max(1 - probability, 1e-10)), lagUp);
                constraint.Add(lagUpTerm);
            }
            solver.AddLeqZeroConstraint(constraint);
        }
        /// <summary>
        /// Ensures that there are no more SRLG failures than what we allow.
        /// </summary>
        protected void AddMaxNumFailureConstraintOnSRLG(ISolver<TVar, TSolution> solver, int maxNumFailures, bool exact = false)
        {
            var totalNumFailures = new Polynomial<TVar>(new Term<TVar>(-1 * maxNumFailures));
            foreach (var srlg in this.SRLGUpVariables.Values)
            {
                totalNumFailures.Add(new Term<TVar>(1, srlg));
            }
            if (!exact)
            {
                solver.AddLeqZeroConstraint(totalNumFailures);
            }
            else
            {
                solver.AddEqZeroConstraint(totalNumFailures);
            }
        }
        /// <summary>
        /// Add max number of failure constraints.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="numMaxFailures"></param>
        /// <param name="exact"></param>
        protected void AddMaxNumFailureConstraintsOnLinks(ISolver<TVar, TSolution> solver, int numMaxFailures, bool exact = false)
        {
            var totalNumFailures = new Polynomial<TVar>(new Term<TVar>(-1 * numMaxFailures));
            foreach (var link in this.LinkUpVariables.Values)
            {
                totalNumFailures.Add(new Term<TVar>(1, link));
            }
            if (!exact)
            {
                solver.AddLeqZeroConstraint(totalNumFailures);
            }
            else
            {
                solver.AddEqZeroConstraint(totalNumFailures);
            }
        }
        /// <summary>
        /// Constraints the maximum number of link failures that are allowed.
        /// </summary>
        protected void AddMaxNumFailureConstraint(ISolver<TVar, TSolution> solver, int maxNumFailures, bool exact = false)
        {
            var totalNumFailures = new Polynomial<TVar>(new Term<TVar>(-1 * maxNumFailures));
            foreach (var lag in this.LagUpVariables.Values)
            {
                totalNumFailures.Add(new Term<TVar>(1, lag));
            }
            if (!exact)
            {
                solver.AddLeqZeroConstraint(totalNumFailures);
            }
            else
            {
                solver.AddEqZeroConstraint(totalNumFailures);
            }
        }
        /// <summary>
        /// Makes ssure that we have enough paths in the path selection and also returns the primary paths so that we can use them for the optimal formulation.
        /// </summary>
        protected virtual Dictionary<(string, string), string[][]> VerifyPaths(Dictionary<(string, string), string[][]> paths, int numExtraPaths)
        {
            var pathSubset = new Dictionary<(string, string), string[][]>();
            foreach (var pair in paths.Keys)
            {
                pathSubset[pair] = paths[pair].Take(Math.Min(this.maxNumPath, paths[pair].Length)).ToArray();
            }
            return pathSubset;
        }
        /// <summary>
        /// Create binary indicator variables that show if a path is up or not.
        /// </summary>
        protected virtual Dictionary<string[], TVar> CreatePathUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var pathUpVariables = new Dictionary<string[], TVar>(new PathComparer());
            var index = 0;
            foreach (var pair in this.Paths.Keys)
            {
                foreach (var path in this.Paths[pair])
                {
                    index += 1;
                    pathUpVariables[path] = solver.CreateVariable($"path_up_{string.Join("_", path)}", type: GRB.BINARY);
                }
            }
            return pathUpVariables;
        }
        /// <summary>
        /// Create the indicator variables for links of a given lag being up.
        /// 0 is lag is up and 1 is that it is down.
        /// </summary>
        protected virtual Dictionary<(string, string, string), TVar> CreateLinkUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var linkUpVariables = new Dictionary<(string, string, string), TVar>();
            foreach (var lag in topology.GetAllEdges())
            {
                var source = lag.Source;
                var dest = lag.Target;
                foreach (var eachLink in topology.edgeLinks[(source, dest)].Keys)
                {
                    linkUpVariables[(source, dest, topology.edgeLinks[(source, dest)][eachLink].Item1)] = solver.CreateVariable("link_up_" + source + "_" + dest + "_" + eachLink, type: GRB.BINARY);
                }
            }
            return linkUpVariables;
        }
        /// <summary>
        /// Create the SRLG up variables.
        /// </summary>
        /// <param name="solver"></param>
        /// <returns></returns>
        protected Dictionary<int, TVar> CreateSRLGUpVariables(ISolver<TVar, TSolution> solver)
        {
            var SRLGUpVariables = new Dictionary<int, TVar>();
            foreach (var srlg in this.SRLGsToLinks.Keys)
            {
                SRLGUpVariables[srlg] = solver.CreateVariable("SRLG_" + srlg, type: GRB.BINARY);
            }
            return SRLGUpVariables;
        }
        /// <summary>
        /// Creates the indicator variables for lags being up.
        /// </summary>
        protected virtual Dictionary<(string, string), TVar> CreateLagUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var lagUpVariables = new Dictionary<(string, string), TVar>();
            foreach (var lag in topology.GetAllEdges())
            {
                var source = lag.Source;
                var dest = lag.Target;
                lagUpVariables[(source, dest)] = solver.CreateVariable("link_up_" + source + "_" + dest, type: GRB.BINARY);
            }
            return lagUpVariables;
        }
        /// <summary>
        /// Does the same thing as the CreateDemandVariables and the CreateCapacityVariables function but for the path extension capacities.
        /// </summary>
        protected Dictionary<string[], Polynomial<TVar>> CreatePathExtensionCapacityVariables(
            ISolver<TVar, TSolution> solver,
            InnerRewriteMethodChoice innerEncoding,
            IDictionary<string[], double> pathExtensionCapacityInits,
            int numExtraPaths)
            {
                var capacityEnforcers = new Dictionary<string[], Polynomial<TVar>>(new PathComparer());
                Logger.Debug("[INFO] In total " + this.Topology.GetNodePairs().Count() + " pairs");
                foreach (var pair in this.Paths.Keys)
                {
                    Logger.Debug("There are " + this.Paths[pair].Length + " paths between " + pair.Item1 + " and " + pair.Item1);
                    if (!this.DemandEnforcers.ContainsKey(pair))
                    {
                        continue;
                    }
                    var index = 0;
                    foreach (var path in this.Paths[pair])
                    {
                        switch (innerEncoding)
                        {
                            case InnerRewriteMethodChoice.KKT:
                                capacityEnforcers[path] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("extensionCapacity_" + string.Join("_", path))));
                                break;
                            case InnerRewriteMethodChoice.PrimalDual:
                                 // get capacity lvls
                                var capacities = new HashSet<double> { this.CapacityUpperBound };
                                var axVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                                var capacityLvlEnforcer = new Polynomial<TVar>();
                                bool found = false;
                                foreach (var capacityLvl in capacities)
                                {
                                    index += 1;
                                    var capacitybinaryAuxVar = solver.CreateVariable($"extendCapacity_{index}", type: GRB.BINARY);
                                    capacityLvlEnforcer.Add(new Term<TVar>(capacityLvl, capacitybinaryAuxVar));
                                    axVariableConstraint.Add(new Term<TVar>(1, capacitybinaryAuxVar));
                                    if (pathExtensionCapacityInits != null)
                                    {
                                        if (Math.Abs(pathExtensionCapacityInits[path] - capacityLvl) < 0.0001)
                                        {
                                            found = true;
                                            solver.InitializeVariables(capacitybinaryAuxVar, 1);
                                        }
                                        else
                                        {
                                            solver.InitializeVariables(capacitybinaryAuxVar, 0);
                                        }
                                    }
                                }
                                if (pathExtensionCapacityInits != null)
                                {
                                    Debug.Assert(found == true || Math.Abs(pathExtensionCapacityInits[path]) < 0.0001);
                                }
                                solver.AddLeqZeroConstraint(axVariableConstraint);
                                capacityEnforcers[path] = capacityLvlEnforcer;
                                break;
                            default:
                                throw new Exception("Unkown inner encoding method");
                        }
                    }
                }
                return capacityEnforcers;
            }
        /// <summary>
        /// This is similar to the demand levels in the demand pinning example.
        /// We are quantizing the capacity and using it for the quantized primal dual implementation.
        /// </summary>
        protected virtual Dictionary<(string, string), Polynomial<TVar>> CreateCapacityVariables(ISolver<TVar, TSolution> solver,
                                                                                                     InnerRewriteMethodChoice innerEncoding,
                                                                                                     IDictionary<(string, string), double> capacityInits,
                                                                                                     bool modelLags)
            {
                var capacityEnforcers = new Dictionary<(string, string), Polynomial<TVar>>();
                Logger.Debug("[Info] In total " + this.Topology.GetNodePairs().Count() + " pairs");
                foreach (var edge in this.Topology.GetAllEdges())
                {
                    var pair = (edge.Source, edge.Target);
                    switch (innerEncoding)
                    {
                        case InnerRewriteMethodChoice.KKT:
                            capacityEnforcers[pair] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("capacity_" + pair.Item1 + "_" + pair.Item2)));
                            break;
                        case InnerRewriteMethodChoice.PrimalDual:
                            // get capacitylvls
                            HashSet<double> capacities = new HashSet<double> { this.Topology.GetEdge(pair.Item1, pair.Item2).Capacity };
                            if (modelLags)
                            {
                                // If we model lags then we also need to have capacities that are less than the full capacity as part of our set.
                                // Specifically, we adjust the link capacity based on the number of lags that have failed.
                                for (int i = 1; i < this.Topology.edgeLinks[pair].Count(); i++)
                                {
                                    capacities.Add(this.Topology.GetEdge(pair.Item1, pair.Item2).Capacity * (i * 1.0 / this.Topology.edgeLinks[pair].Count()));
                                }
                            }
                            capacities.Remove(0);
                            var axVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                            var capacityLvlEnforcer = new Polynomial<TVar>();
                            bool found = false;
                            foreach (var capacitylvl in capacities)
                            {
                                var capacitybinaryAuxVar = solver.CreateVariable("aux_capacity_" + pair.Item1 + "_" + pair.Item2, type: GRB.BINARY);
                                capacityLvlEnforcer.Add(new Term<TVar>(capacitylvl, capacitybinaryAuxVar));
                                axVariableConstraint.Add(new Term<TVar>(1, capacitybinaryAuxVar));
                                if (capacityInits != null)
                                {
                                    if (Math.Abs(capacityInits[pair] - capacitylvl) < 0.0001)
                                    {
                                        found = true;
                                        solver.InitializeVariables(capacitybinaryAuxVar, 1);
                                    }
                                    else
                                    {
                                        solver.InitializeVariables(capacitybinaryAuxVar, 0);
                                    }
                                }
                            }
                            if (capacityInits != null)
                            {
                                Debug.Assert(found == true || Math.Abs(capacityInits[pair]) < 0.0001);
                            }
                            solver.AddLeqZeroConstraint(axVariableConstraint);
                            capacityEnforcers[pair] = capacityLvlEnforcer;
                            break;
                        default:
                            throw new Exception("Unkonwn inner encoding method");
                    }
                }
                return capacityEnforcers;
            }
    }
}