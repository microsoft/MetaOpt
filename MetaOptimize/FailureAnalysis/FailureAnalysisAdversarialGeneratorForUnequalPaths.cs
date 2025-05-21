namespace MetaOptimize.FailureAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using Gurobi;
    using Microsoft.Z3;
    using NLog;
    using NLog.LayoutRenderers;
    /// <summary>
    /// This class is similar to FailureAnalysis adversarial generator with MetaNodes but
    /// does not assume we have the same number of primary and backup paths.
    /// </summary>
    public class FailureAnalysisAdversarialGeneratorForUnequalPaths<TVar, TSolution> : FailureAnalysisWithMetaNodeAdversarialGenerator<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// The primary paths for the traffic.
        /// </summary>
        public Dictionary<(string, string), string[][]> primaryPaths { get; set; }
        /// <summary>
        /// The backup paths for the traffic.
        /// </summary>
        public Dictionary<(string, string), string[][]> backupPaths { get; set; }
        /// <summary>
        /// The starting point for this is the same as the base function.
        /// </summary>
        public FailureAnalysisAdversarialGeneratorForUnequalPaths(Topology topology, Dictionary<string, HashSet<string>> metaNodeToActualNode = null)
        : base(topology, 0, -1, metaNodeToActualNodes: metaNodeToActualNode)
        {
        }
        /// <summary>
        /// This function needs to significantly change because of the path categories.
        /// </summary>
        protected override void SetPathConstraintsForFailures(ISolver<TVar, TSolution> solver, bool ensureAtLeastOneUp = false)
        {
            foreach (var pair in this.DemandEnforcers.Keys)
            {
                var atLeastOneUpConstraint = new Polynomial<TVar>();
                foreach (var actualSource in this.MetaNodeToActualNode[pair.Item1])
                {
                    foreach (var actualDest in this.MetaNodeToActualNode[pair.Item2])
                    {
                        // What I have done so far is for each destination, look through all the rest of the sources and destinations.
                        var sumOfPathUpVariables = new Polynomial<TVar>();
                        List<(string[], string)> candidateList = this.primaryPaths.ContainsKey(pair) ? this.primaryPaths[pair].Where(p => p[1] == actualSource && p[p.Length - 2] == actualDest)
                                                                                                                              .Select(p => (p, "Primary"))
                                                                                                                              .ToList() : null;
                        var primaryLength = candidateList != null ? candidateList.Count : 0;
                        if (this.backupPaths.ContainsKey(pair))
                        {
                            candidateList.AddRange(this.backupPaths[pair].Where(p => p[1] == actualSource && p[p.Length - 2] == actualDest).Select(p => (p, "Backup")));
                        }
                        if (candidateList == null)
                        {
                            continue;
                        }
                        for (var i = 0; i < candidateList.Count(); i++)
                        {
                            var path = candidateList[i].Item1;
                            var priority = candidateList[i].Item2;
                            Debug.Assert(priority == "Primary" ? i < primaryLength : i >= primaryLength, "There is a likely bug in the code");
                            if (ensureAtLeastOneUp)
                            {
                                atLeastOneUpConstraint.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                            }
                            var firstPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(1, this.PathUpVariables[path]));
                            for (int k = 0; k < path.Length - 1; k++)
                            {
                                var source = path[k];
                                var dest = path[k + 1];
                                firstPathUpConstraint.Add(new Term<TVar>(-1, this.LagUpVariables[(source, dest)]));
                                var secondPathUpConstraint = new Polynomial<TVar>(new Term<TVar>(1, this.LagUpVariables[(source, dest)]));
                                secondPathUpConstraint.Add(new Term<TVar>(-1, this.PathUpVariables[path]));
                                solver.AddLeqZeroConstraint(secondPathUpConstraint);
                            }
                            solver.AddLeqZeroConstraint(firstPathUpConstraint);
                            sumOfPathUpVariables.Add(new Term<TVar>(1, this.PathUpVariables[path]));
                            var sumDisabledPathSoFar = sumOfPathUpVariables.Copy();
                            sumDisabledPathSoFar.Add(new Term<TVar>(primaryLength - i));
                            var aux = EncodingUtils<TVar, TSolution>.IsLeq(solver, new Polynomial<TVar>(new Term<TVar>(0)), sumDisabledPathSoFar, candidateList.Count() * 10, 0.1);
                            this.AuxiliaryVariables.Add(aux);
                            Debug.Assert(this.PathExtensionCapacityEnforcers.ContainsKey(path) || this.DemandEnforcers.ContainsKey(pair), $"The path does not exist in the enforcer {path}");
                            var lowerBound = this.PathExtensionCapacityEnforcers[path].Copy().Negate();
                            lowerBound.Add(new Term<TVar>(this.CapacityUpperBound, aux));
                            solver.AddEqZeroConstraint(lowerBound);
                        }
                    }
                }
                if (ensureAtLeastOneUp)
                {
                    var numPaths = (this.primaryPaths[pair].Length + this.backupPaths[pair].Length);
                    if (numPaths < 1)
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * numPaths));
                    }
                    else
                    {
                        atLeastOneUpConstraint.Add(new Term<TVar>(-1 * (numPaths - 1)));
                    }
                    solver.AddLeqZeroConstraint(atLeastOneUpConstraint);
                }
            }
        }
        /// <summary>
        /// This is the same as its counter part in the failure analysis adversarial generator.
        /// The main difference is that it allows for a different numbero f primary and backup paths.
        /// </summary>
        /// <returns></returns>
        protected Dictionary<string[], Polynomial<TVar>> CreatePathExtensionCapacityVariables(ISolver<TVar, TSolution> solver,
                                                                                              InnerRewriteMethodChoice innerEncoding,
                                                                                              IDictionary<string[], double> pathExtensionCapacityInits)
        {
            var capacityEnforcers = new Dictionary<string[], Polynomial<TVar>>(new PathComparer());
            foreach (var pair in this.DemandEnforcers.Keys)
            {
                var index = 0;
                var paths = this.primaryPaths.ContainsKey(pair) ? this.primaryPaths[pair] : null;
                paths = paths != null ? (this.backupPaths.ContainsKey(pair) ? paths.Concat(this.backupPaths[pair]).ToArray() : paths) : null;
                if (paths == null)
                {
                    Logger.Warn($"No paths found for pair {pair}!!!");
                    continue;
                }
                foreach (var path in paths)
                {
                    switch (innerEncoding)
                    {
                        case InnerRewriteMethodChoice.KKT:
                            capacityEnforcers[path] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("extensionCapacity_" + string.Join("_", path))));
                            break;
                        case InnerRewriteMethodChoice.PrimalDual:
                            var capacities = new HashSet<double> { this.CapacityUpperBound };
                            var auxVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                            var capacityLvlEnforcer = new Polynomial<TVar>();
                            bool found = false;
                            foreach (var capacitylvl in capacities)
                            {
                                index += 1;
                                var capacitybinaryAuxVar = solver.CreateVariable($"extendCapacity_{string.Join("_", path)}", type: GRB.BINARY);
                                capacityLvlEnforcer.Add(new Term<TVar>(capacitylvl, capacitybinaryAuxVar));
                                auxVariableConstraint.Add(new Term<TVar>(1, capacitybinaryAuxVar));
                                if (pathExtensionCapacityInits != null)
                                {
                                    if (Math.Abs(pathExtensionCapacityInits[path] - capacitylvl) < 0.0001)
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
                            solver.AddLeqZeroConstraint(auxVariableConstraint);
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
        /// This is the same as its counterpart it just adds supports for primary and backup paths
        /// being stored in different data structures.
        /// </summary>
        protected override Dictionary<string[], TVar> CreatePathUpVariables(ISolver<TVar, TSolution> solver, Topology topology)
        {
            var pathUpVariables = new Dictionary<string[], TVar>(new PathComparer());
            var index = 0;
            foreach (var pair in this.DemandEnforcers.Keys)
            {
                if (this.primaryPaths.ContainsKey(pair))
                {
                    foreach (var path in this.primaryPaths[pair])
                    {
                        index += 1;
                        pathUpVariables[path] = solver.CreateVariable($"primary_path_up_{string.Join("_", path)}", type: GRB.BINARY);
                    }
                }
                if (this.backupPaths.ContainsKey(pair))
                {
                    foreach (var path in this.backupPaths[pair])
                    {
                        index += 1;
                        pathUpVariables[path] = solver.CreateVariable($"backup_path_up_{string.Join("_", path)}", type: GRB.BINARY);
                    }
                }
            }
            return pathUpVariables;
        }
        /// <summary>
        /// Maximize the optimality gap.
        /// </summary>
        /// <returns></returns>
        public virtual (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> failureAnalysisEncoder,
            Dictionary<(string, string), string[][]> primaryPaths,
            Dictionary<(string, string), string[][]> backupPaths,
            double demandUB = -1,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool cleanupSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            IDictionary<(string, string), double> capacityInits = null,
            IDictionary<string[], double> pathExtensionCapacityInits = null,
            double density = 1.0,
            double largeDemandLB = -1,
            int largeMaxDistance = -1,
            int smallMaxDistance = -1,
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
            bool optimalIsConstant = false,
            bool cleanUpSolver = true,
            Dictionary<int, double> SRLGFailureProbabilities = null,
            Dictionary<int, HashSet<(string, string, string)>> SRLGsToLinks = null,
            bool useSRLGFailures = false,
            bool doNotFailMetro = true)
            {
                if (linkFailureProbabilities != null && lagFailureProbabilities != null)
                {
                    throw new Exception("you cannot have both link and lag failure probabilities");
                }
                if (linkFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("you cannot use both link and SRLG failure probabilities.");
                }
                if (lagFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("you cannot use both lag and SRLG failure probabilities.");
                }
                if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
                {
                    throw new Exception("Solver mis-match between optimal and failure analysis scenario");
                }
                if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
                {
                    throw new Exception("Demand list is required for primal-dual");
                }
                if (demandUB != -1 && perDemandUB != null)
                {
                    throw new Exception("If global demand UB is enabled, then perDemandUB should be null.");
                }
                this.primaryPaths = primaryPaths;
                this.backupPaths = backupPaths;
                this.doNotFailMetro = doNotFailMetro;
                var solver = optimalEncoder.Solver;
                if (cleanUpSolver)
                {
                    solver.CleanAll();
                }
                if (SRLGsToLinks != null)
                {
                    this.SRLGsToLinks = SRLGsToLinks;
                }
                if (useSRLGFailures && !useLinkFailures)
                {
                    throw new Exception("You cannot use SRLG failures without link failures.");
                }
                this.Solver = solver;
                (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits);
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
                TEMaxFlowOptimizationSolution optimalParsedSolution = null;
                OptimizationEncoding<TVar, TSolution> optimalEncoding = null;
                EnsureDemandEquality(solver, constrainedDemands);
                EnsureDensityConstraint(solver, density);
                optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                          preInputVariables: this.DemandEnforcers,
                                                          inputEqualityConstraints: this.LocalityConstrainedDemands,
                                                          innerEncoding: innerEncoding,
                                                          selectedPaths: this.primaryPaths,
                                                          numProcesses: this.NumProcesses,
                                                          noAdditionalConstraints: true,
                                                          historicInputConstraints: historicDemandConstraints);
                if (perDemandUB != null)
                {
                    EnsureDemandUB(solver, perDemandUB);
                }
                else
                {
                    EnsureDemandUB(solver, demandUB);
                }
                if (optimalIsConstant)
                {
                    var optimalSolution = solver.Maximize(optimalEncoding.MaximizationObjective);
                    optimalParsedSolution = (TEMaxFlowOptimizationSolution)optimalEncoder.GetSolution(optimalSolution);
                    solver.CleanAll();
                    (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                    this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
                    this.PathExtensionCapacityEnforcers = CreatePathExtensionCapacityVariables(solver, innerEncoding, pathExtensionCapacityInits);
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
                var failureAnalysisEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                              primaryPaths: this.primaryPaths,
                                                                              backupPaths: this.backupPaths,
                                                                              preDemandVariables: this.DemandEnforcers,
                                                                              preCapVariables: this.CapacityEnforcers,
                                                                              prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                                                                              demandEqualityConstraints: this.LocalityConstrainedDemands,
                                                                              noAdditionalConstraints: false,
                                                                              innerRewriteMethodChoice: innerEncoding,
                                                                              historicDemandConstraints: historicDemandConstraints);
                if (!useLinkFailures)
                {
                    AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
                else
                {
                    AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
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
                if (failureProbThreshold >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddFailureProbabilityConstraintOnSRLG(solver, SRLGFailureProbabilities, failureProbThreshold);
                }
                if (useSRLGFailures)
                {
                        AddSRLGConstraints(solver, lagStatusConstraints);
                }
                if (lagStatusConstraints != null)
                {
                    AddLagStatusConstraints(solver, lagStatusConstraints);
                }
                var objective = new Polynomial<TVar>(new Term<TVar>(1, optimalEncoding.GlobalObjective), new Term<TVar>(-1, failureAnalysisEncoding.GlobalObjective));
                if (optimalIsConstant)
                {
                    objective = new Polynomial<TVar>(new Term<TVar>(optimalParsedSolution.MaxObjective), new Term<TVar>(-1, failureAnalysisEncoding.GlobalObjective));
                }
                var solution = solver.Maximize(objective, reset: true);
                this.LagUpResults = this.GetLagDownEvents(solution);
                var optSol = optimalIsConstant ? optimalParsedSolution : (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                var failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                return (optSol, failureSol);
            }
            /// <summary>
            /// The clustered version of maximize optimality gap for two path sets.
            /// </summary>
        public (TEOptimizationSolution, FailureAnalysisOptimizationSolution) MaximizeOptimalityGapWithClustering(List<Topology> clusters,
                                                                                                                     IEncoder<TVar, TSolution> optimalEncoder,
                                                                                                                     IEncoder<TVar, TSolution> failureAnalysisEncoder,
                                                                                                                     Dictionary<(string, string), string[][]> primaryPaths,
                                                                                                                     Dictionary<(string, string), string[][]> backupPaths,
                                                                                                                     double demandUB = -1,
                                                                                                                     int numIterClusterSamples = 0,
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
                                                                                                                     Dictionary<(int, string, string), double> historicDemandConstraints = null,
                                                                                                                     int numExtraPaths = 0,
                                                                                                                     int maxNumFailures = -1,
                                                                                                                     int exactNumFailures = -1,
                                                                                                                     Dictionary<(string, string), double> lagFailureProbabilities = null,
                                                                                                                     double failureProbThreshold = -1,
                                                                                                                     double scenarioProbThreshold = -1,
                                                                                                                     IEncoder<TVar, TSolution> heuristicDirectEncoder = null,
                                                                                                                     Dictionary<(string, string, string), double> linkFailureProbabilities = null,
                                                                                                                     bool useLinkFailures = false,
                                                                                                                     bool ensureConnectedGraph = false,
                                                                                                                     Dictionary<(string, string), bool> lagStatusConstraints = null,
                                                                                                                     Dictionary<int, double> SRLGFailureProbabilities = null,
                                                                                                                     Dictionary<int, HashSet<(string, string, string)>> SRLGsToLinks = null,
                                                                                                                     bool useSRLGFailures = false,
                                                                                                                     bool doNotFailMetro = false)
            {
                this.Solver = optimalEncoder.Solver;
                var solver = optimalEncoder.Solver;
                this.primaryPaths = primaryPaths;
                this.backupPaths = backupPaths;
                this.doNotFailMetro = doNotFailMetro;
                if (lagFailureProbabilities != null && linkFailureProbabilities != null)
                {
                    throw new Exception("you cannot have both lag and link failure probabilities");
                }
                if (linkFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("You cannot use both link and SRLG failure probabilities.");
                }
                if (lagFailureProbabilities != null && SRLGFailureProbabilities != null)
                {
                    throw new Exception("You cannot use both lag and SRLG failure probabilities");
                }
                if (optimalEncoder.Solver != failureAnalysisEncoder.Solver)
                {
                    throw new Exception("solver mis-match between optimal and failure analysis scenario");
                }
                if (innerEncoding == InnerRewriteMethodChoice.PrimalDual && demandList == null)
                {
                    throw new Exception("Demand list is required for primal-dual");
                }
                if (demandUB != -1 && perDemandUB != null)
                {
                    throw new Exception("If global demand UB is enabled then preDemandUB should not be enabled");
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
                    throw new Exception("We need useLinkFailures to be true if we want to use SRLG failures");
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
                Dictionary<(string, string), double> rndDemand = null;
                var timer = Stopwatch.StartNew();
                solver.CleanAll();
                (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                    CreateDemandVariables(solver, innerEncoding, demandList, demandInits, largeDemandLB, largeMaxDistance, smallMaxDistance);
                this.CapacityEnforcers = CreateCapacityVariables(solver, innerEncoding, capacityInits, useLinkFailures);
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
                // The approach we take here is to allow the demands to be determined cluster by cluster
                // BUT the capacity variables are assigned each time. This means the size of the clusters are not fully getting reduced
                // because the capacity variables are still global. Its just the demand that is getting clustered. The reason I am doing this is because I have
                // global constraints on the number/probability of failures. Clustering would require us to convert these constraints into within cluster constraints which is difficult.
                // What I am suggesting here is a compromise we can revisit if it is needed.
                var optimalEncoding = optimalEncoder.Encoding(this.Topology,
                                                              preInputVariables: this.DemandEnforcers,
                                                              innerEncoding: innerEncoding,
                                                              numProcesses: this.NumProcesses,
                                                              inputEqualityConstraints: this.LocalityConstrainedDemands,
                                                              noAdditionalConstraints: true,
                                                              selectedPaths: this.primaryPaths,
                                                              historicInputConstraints: historicDemandConstraints);
                var failureAnalysisEncoding = failureAnalysisEncoder.Encoding(this.Topology,
                                                                              primaryPaths: this.primaryPaths,
                                                                              backupPaths: this.backupPaths,
                                                                              preDemandVariables: this.DemandEnforcers,
                                                                              preCapVariables: this.CapacityEnforcers,
                                                                              prePathExtensionCapacities: this.PathExtensionCapacityEnforcers,
                                                                              demandEqualityConstraints: this.LocalityConstrainedDemands,
                                                                              noAdditionalConstraints: false,
                                                                              innerRewriteMethodChoice: innerEncoding,
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
                EnsureDemandEquality(solver, constrainedDemands);
                EnsureDensityConstraint(solver, density);
                if (!useLinkFailures)
                {
                    AddImpactOfFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
                else
                {
                    AddImpactOfLinkFailuresOnCapacity(solver, ensureAtLeastOnePathUp: ensureConnectedGraph);
                }
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
                if (scenarioProbThreshold != -1 && !useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraints(solver, lagFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && !useSRLGFailures)
                {
                    AddScenarioProbabilityConstraintOnLinks(solver, linkFailureProbabilities, scenarioProbThreshold, lagStatusConstraints: lagStatusConstraints);
                }
                if (scenarioProbThreshold != -1 && useLinkFailures && useLinkFailures)
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
                if (failureProbThreshold >= 0 && useLinkFailures && useSRLGFailures)
                {
                    AddFailureProbabilityConstraintOnSRLG(solver, SRLGFailureProbabilities, failureProbThreshold);
                }
                if (useSRLGFailures)
                {
                    AddSRLGConstraints(solver, lagStatusConstraints);
                }
                if (demandUB < 0)
                {
                    demandUB = 30 * this.Topology.MaxCapacity();
                }
                var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
                pairNameToConstraintMapping = AddViableDemandConstraints(false, solver, constrainedDemands, rndDemand);
                var demandMatrix = new Dictionary<(string, string), double>();
                foreach (var cluster in clusters)
                {
                    var consideredPairs = new HashSet<(string, string)>();
                    foreach (var pair in cluster.GetNodePairs())
                    {
                        if (checkIfPairIsConstrained(constrainedDemands, pair) || !this.DemandEnforcers.ContainsKey(pair))
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
                    var objective = new Polynomial<TVar>(
                    new Term<TVar>(1, optimalEncoding.GlobalObjective),
                    new Term<TVar>(-1, failureAnalysisEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                    foreach (var pair in consideredPairs)
                    {
                        var demandLvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                        demandMatrix[pair] = demandLvl;
                        AddSingleDemandEquality(solver, pair, demandLvl);
                    }
                }
                for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
                {
                    for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                    {
                        var consideredPairs = new HashSet<(string, string)>();
                        var cluster1Nodes = clusters[cid1].GetAllNodes().ToList();
                        var cluster2Nodes = clusters[cid2].GetAllNodes().ToList();
                        foreach (var node1 in cluster1Nodes)
                        {
                            foreach (var node2 in cluster2Nodes)
                            {
                                if (!this.primaryPaths.ContainsKey((node1, node2)) || checkIfPairIsConstrained(constrainedDemands, (node1, node2)) || !this.DemandEnforcers.ContainsKey((node1, node2)))
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
                        var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, failureAnalysisEncoding.GlobalObjective));
                        var solution = solver.Maximize(objective, reset: true);
                        optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                        failureSol = (FailureAnalysisOptimizationSolution)failureAnalysisEncoder.GetSolution(solution);
                        foreach (var pair in consideredPairs)
                        {
                            var demandLvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optSol.Demands[pair]);
                            demandMatrix[pair] = Math.Round(demandLvl);
                            AddSingleDemandEquality(solver, pair, demandLvl);
                        }
                    }
                }
                foreach (var pair in this.DemandEnforcers.Keys)
                {
                    if (!demandMatrix.ContainsKey(pair) && !constrainedDemands.ContainsKey(pair))
                    {
                        demandMatrix[pair] = 0;
                    }
                    else
                    {
                        if (demandMatrix.ContainsKey(pair))
                        {
                            Logger.Warn("Check this, it may be a bug!!!!");
                        }
                        else
                        {
                            demandMatrix[pair] = Math.Round(constrainedDemands[pair]);
                        }
                    }
                }
                (optSol, failureSol) = MaximizeOptimalityGap(optimalEncoder,
                                                             failureAnalysisEncoder,
                                                             primaryPaths: primaryPaths,
                                                             backupPaths: backupPaths,
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
                                                             historicDemandConstraints: historicDemandConstraints,
                                                             numExtraPaths: numExtraPaths,
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
    }
}