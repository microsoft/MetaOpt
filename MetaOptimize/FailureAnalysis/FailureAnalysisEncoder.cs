namespace MetaOptimize.FailureAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using Microsoft.Z3;
    using NLog;
    /// <summary>
    /// This function is a variant of the failureAnalysisEncoder which allows us to
    /// use an adversarialGenerator compatible with MetaOpt.
    /// </summary>
    /// <typeparam name="TVar"></typeparam>
    /// <typeparam name="TSolution"></typeparam>
    public class FailureAnalysisEncoder<TVar, TSolution> : TEMaxFlowOptimalEncoder<TVar, TSolution>, IEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// These are the variables c_l that we have in the failure analysis doc.
        /// </summary>
        public Dictionary<(string, string), Polynomial<TVar>> CapacityVariables { get; set; }
        /// <summary>
        /// These are the capacities for link l'_{pk} defined in the failure analysis doc.
        /// </summary>
        /// <value></value>
        public Dictionary<string[], Polynomial<TVar>> PathExtensionCapacities { get; set; }
        /// <summary>
        /// Capacity constraints in terms of constant values.
        /// </summary>
        public Dictionary<(string, string), double> CapacityConstraints { get; set; }
        /// <summary>
        /// PathExtension capacity constraints in terms of constant values.
        /// </summary>
        /// <value></value>
        public Dictionary<string[], double> PathExtensionCapacityConstraints { get; set; }
        /// <summary>
        /// Initializer for the class.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="maxNumPathTotal"></param>
        public FailureAnalysisEncoder(ISolver<TVar, TSolution> solver, int maxNumPathTotal)
            : base(solver, maxNumPathTotal)
        {
        }
        /// <summary>
        /// Use this to initialize the failure analysis variables.
        /// </summary>
        protected virtual void InitializeFailureAnalysisVariables(
            Dictionary<(string, string), Polynomial<TVar>> preCapVariables,
            Dictionary<(string, string), double> capacityEqualityConstraints,
            Dictionary<string[], Polynomial<TVar>> prePathExtensionCapacities,
            Dictionary<string[], double> pathExtensionCapacityConstraints,
            int numProcesses)
            {
                this.CapacityConstraints = capacityEqualityConstraints ?? new Dictionary<(string, string), double>();
                this.PathExtensionCapacityConstraints = pathExtensionCapacityConstraints ?? new Dictionary<string[], double>(new PathComparer());
                this.CapacityVariables = new Dictionary<(string, string), Polynomial<TVar>>();
                if (preCapVariables == null)
                {
                    foreach (var edge in this.Topology.GetAllEdges())
                    {
                        var source = edge.Source;
                        var target = edge.Target;
                        var variable = this.Solver.CreateVariable("edgeCapacity_" + source + "_" + target);
                        this.CapacityVariables[(source, target)] = new Polynomial<TVar>(new Term<TVar>(1, variable));
                        this.variables.Add(variable);
                        this.innerProblemEncoder.AddConstantVar(variable);
                    }
                }
                else
                {
                    foreach (var (pair, variable) in preCapVariables)
                    {
                        this.CapacityVariables[pair] = variable;
                        foreach (var term in variable.GetTerms())
                        {
                            this.variables.Add(term.Variable.Value);
                            this.innerProblemEncoder.AddConstantVar(term.Variable.Value);
                        }
                    }
                }
                if (prePathExtensionCapacities == null)
                {
                    this.PathExtensionCapacities = new Dictionary<string[], Polynomial<TVar>>(new PathComparer());
                    foreach (var (pair, paths) in this.Paths)
                    {
                        foreach (var simplePath in this.Paths[pair])
                        {
                            var variable = this.Solver.CreateVariable("extensionCapacity_" + string.Join("_", simplePath));
                            this.PathExtensionCapacities[simplePath] = new Polynomial<TVar>(new Term<TVar>(1, variable));
                            this.variables.Add(variable);
                            this.innerProblemEncoder.AddConstantVar(variable);
                        }
                    }
                }
                else
                {
                    this.PathExtensionCapacities = new Dictionary<string[], Polynomial<TVar>>(new PathComparer());
                    foreach (var (pair, variable) in prePathExtensionCapacities)
                    {
                        this.PathExtensionCapacities[pair] = variable;
                        foreach (var term in variable.GetTerms())
                        {
                            this.variables.Add(term.Variable.Value);
                            this.innerProblemEncoder.AddConstantVar(term.Variable.Value);
                        }
                    }
                }
            }
            /// <summary>
            /// If there are capacities that are preset this function ensures we have constraints that capture them.
            /// </summary>
        public void MeetPreSetCapacities()
            {
                Logger.Info("Ensures that pre-set capacities are respected.");
                foreach (var (pair, constant) in this.CapacityConstraints)
                {
                    if (constant < 0)
                    {
                        continue;
                    }
                    var capacityConstraint = this.CapacityVariables[pair].Copy();
                    capacityConstraint.Add(new Term<TVar>(-1 * constant));
                    this.Solver.AddEqZeroConstraint(capacityConstraint);
                }
            }
        /// <summary>
        /// Sets the pre-specified values for the capacity of auxiliary lags l_{pk}.
        /// </summary>
        public void MeetPreSetExtensionCapacities()
        {
            Logger.Info("Ensure pre-set capacities are respected");
            foreach (var (path, constant) in this.PathExtensionCapacityConstraints)
            {
                if (constant < 0)
                {
                    continue;
                }
                if (!PathExtensionCapacities.ContainsKey(path))
                {
                    continue;
                }
                var capacityExtensionConstraint = this.PathExtensionCapacities[path].Copy();
                capacityExtensionConstraint.Add(new Term<TVar>(-1 * constant));
                this.Solver.AddEqZeroConstraint(capacityExtensionConstraint);
            }
        }
        /// <summary>
        /// Sets the capacity constraints for the paths and the extensions of them.
        /// </summary>
        public virtual void AddCapCapacityConstraints()
        {
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            foreach (var (pair, paths) in this.Paths)
            {
                if (!this.IsDemandValid(pair))
                {
                    continue;
                }
                foreach (var path in paths)
                {
                    // for a path p_k ensure f_pk <= c_l'_pk. Where l_pk is an auxiliary link to capture
                    // the constraint that only paths are active that are part of the k-shortest paths.
                    if (this.PathExtensionCapacities.ContainsKey(path))
                    {
                        if (!this.FlowPathVariables.ContainsKey(path))
                        {
                            continue;
                        }
                        var extensionCapacityConstraint = new Polynomial<TVar>(new Term<TVar>(0));
                        extensionCapacityConstraint.Add(this.PathExtensionCapacities[path].Copy().Negate());
                        extensionCapacityConstraint.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                        this.innerProblemEncoder.AddLeqZeroConstraint(extensionCapacityConstraint);
                    }
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.Topology.GetEdge(source, target);
                        var term = new Term<TVar>(1, this.FlowPathVariables[path]);
                        if (!sumPerEdge.ContainsKey(edge))
                        {
                            sumPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
                        }
                        sumPerEdge[edge].Add(term);
                    }
                }
            }
            foreach (var (edge, total) in sumPerEdge)
            {
                total.Add(this.CapacityVariables[(edge.Source, edge.Target)].Copy().Negate());
                this.innerProblemEncoder.AddLeqZeroConstraint(total);
            }
        }
        /// <summary>
        /// Makes sure that all required elements from each of these are set.
        /// </summary>
        /// <param name="demandEqualityConstraints"></param>
        /// <param name="capacityEqualityConstraints"></param>
        /// <param name="pathExtensionCapacityConstraints"></param>
        public void CheckPreSetConstraints(Dictionary<(string, string), double> demandEqualityConstraints,
                                           Dictionary<(string, string), double> capacityEqualityConstraints,
                                           Dictionary<string[], double> pathExtensionCapacityConstraints)
        {
            if (capacityEqualityConstraints != null)
            {
                if (capacityEqualityConstraints.Keys.Count() != this.Topology.GetAllEdges().Count())
                {
                    throw new ArgumentException("The numbero f capacity constraints does not match the number of edges in the graph");
                }
            }
        }
        /// <summary>
        /// Encodes the capacity planner cut scenario. See the overleaf doc for detail of the formulation.
        /// </summary>
        /// <returns></returns>
        public virtual OptimizationEncoding<TVar, TSolution> Encoding(
            Topology topology,
            bool modelFailures = true,
            Dictionary<(string, string), Polynomial<TVar>> preDemandVariables = null,
            Dictionary<(string, string), Polynomial<TVar>> preCapVariables = null,
            Dictionary<string[], Polynomial<TVar>> prePathExtensionCapacities = null,
            Dictionary<(string, string), double> demandEqualityConstraints = null,
            Dictionary<(string, string), double> capacityEqualityConstraints = null,
            Dictionary<string[], double> pathExtensionCapacityConstraints = null,
            bool noAdditionalConstraints = false,
            InnerRewriteMethodChoice innerRewriteMethod = InnerRewriteMethodChoice.KKT,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            HashSet<string> excludeNodeEdges = null,
            int numProcesses = -1)
        {
            this.Topology = topology;
            Logger.Info("Initialize the base optimizer variables.");
            this.InitializeVariables(preDemandVariables, demandEqualityConstraints, innerRewriteMethod, pathType, selectedPaths, numProcesses);
            if (selectedPaths != null)
            {
                if (selectedPaths.Count != this.Topology.GetNodePairs().Count() && selectedPaths.Count != preDemandVariables.Count)
                {
                    throw new ArgumentException("The number of selected paths does not match the number of node pairs in the graph. Note the selectedPaths with the links as edges combination is not enabled yet.");
                }
                this.Paths = selectedPaths;
            }
            Logger.Info("Initialize the failure analysis scenario variables");
            this.InitializeFailureAnalysisVariables(preCapVariables, capacityEqualityConstraints, prePathExtensionCapacities, pathExtensionCapacityConstraints, numProcesses);
            this.CheckPreSetConstraints(demandEqualityConstraints, capacityEqualityConstraints, pathExtensionCapacityConstraints);
            this.EncodeTotalDemandConstraint(topology);
            this.MeetPreSetDemands();
            this.MeetPreSetCapacities();
            this.MeetPreSetExtensionCapacities();
            this.BoundFlowVariables();
            this.EnsureNoFlowOnDisconnected();
            this.ComputeTotalFlowPerDemand();
            this.AddCapacityConstraints();
            Logger.Info("Generate full constraints.");
            var objective = new Polynomial<TVar>(new Term<TVar>(1, this.TotalDemandMetVariable));
            Logger.Info("Calling inner rewrite function.");
            this.innerProblemEncoder.AddMaximizationConstraints(objective, noAdditionalConstraints);
            return new TEOptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.TotalDemandMetVariable,
                MaximizationObjective = objective,
                DemandVariables = this.DemandVariables,
            };
        }
        /// <summary>
        /// Get Solution.
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public override OptimizationSolution GetSolution(TSolution solution)
        {
            var LagStatus = new Dictionary<Edge, double>();
            var flows = new Dictionary<(string, string), double>();
            var flowPaths = new Dictionary<string[], double>(new PathComparer());
            var lagFlows = new Dictionary<Edge, double>();
            var demands = new Dictionary<(string, string), double>();
            foreach (var (pair, poly) in this.DemandVariables)
            {
                demands[pair] = 0;
                foreach (var term in poly.GetTerms())
                {
                    demands[pair] += this.Solver.GetVariable(solution, term.Variable.Value) * term.Coefficient;
                }
            }
            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    if (!this.FlowPathVariables.ContainsKey(path))
                    {
                        continue;
                    }
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.Topology.GetEdge(source, target);
                        if (lagFlows.ContainsKey(edge))
                        {
                            lagFlows[edge] += this.Solver.GetVariable(solution, this.FlowPathVariables[path]);
                        }
                        else
                        {
                            lagFlows[edge] += this.Solver.GetVariable(solution, this.FlowPathVariables[path]);
                        }
                    }
                }
            }
            foreach (var edge in this.Topology.GetAllEdges())
            {
                var source = edge.Source;
                var target = edge.Target;
                if (!LagStatus.ContainsKey(edge))
                {
                    LagStatus[edge] = 0;
                }
                foreach (var term in this.CapacityVariables[(source, target)].GetTerms())
                {
                    LagStatus[edge] += this.Solver.GetVariable(solution, term.Variable.Value);
                }
            }
            foreach (var (pair, variable) in this.FlowVariables)
            {
                flows[pair] = this.Solver.GetVariable(solution, variable);
            }
            foreach (var (path, variable) in this.FlowPathVariables)
            {
                flowPaths[path] = this.Solver.GetVariable(solution, variable);
            }
            return new FailureAnalysisOptimizationSolution
            {
                Demands = demands,
                LagStatus = LagStatus,
                Flows = flows,
                FlowsPaths = flowPaths,
                LagFlows = lagFlows,
                MaxObjective = this.Solver.GetVariable(solution, this.TotalDemandMetVariable),
            };
        }
    }
}