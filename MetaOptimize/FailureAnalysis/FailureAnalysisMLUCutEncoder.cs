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
    /// Failure analysis encoder that uses the MLU objective instead of the regular one.
    /// </summary>
    /// <typeparam name="TVar"></typeparam>
    /// <typeparam name="TSolution"></typeparam>
    public class FailureAnalysisMLUCutEncoder<TVar, TSolution> : FailureAnalysisEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// The MLU variable.
        /// </summary>
        public TVar MLU { get; set; }
        private double splitRatioLB;
        private double splitRatioUB;
        private Dictionary<string[], double> preSplitRatios;
        private int FRACTION_DIG = 4;
        /// <summary>
        /// Initializer for the class.
        /// </summary>
        public FailureAnalysisMLUCutEncoder(ISolver<TVar, TSolution> solver, int maxNumPathTotal, double splitRatioLB = 0, double splitRatioUB = 1, Dictionary<string[], double> preSplitRatios = null)
            : base(solver, maxNumPathTotal)
        {
            this.splitRatioLB = splitRatioLB;
            this.splitRatioUB = splitRatioUB;
            this.preSplitRatios = preSplitRatios;
        }
        /// <summary>
        /// Ensure split ratios bounds are respected.
        /// </summary>
        protected virtual void EnsureSplitRatioConstraints()
        {
            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    if (this.splitRatioLB > 0)
                    {
                        var constr1 = new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path]));
                        constr1.Add(this.DemandVariables[pair].Multiply(this.splitRatioLB));
                        this.innerProblemEncoder.AddLeqZeroConstraint(constr1);
                    }
                    if (this.splitRatioUB < 1)
                    {
                        var constr2 = new Polynomial<TVar>(new Term<TVar>(1, this.FlowPathVariables[path]));
                        constr2.Add(this.DemandVariables[pair].Multiply(-this.splitRatioUB));
                        this.innerProblemEncoder.AddLeqZeroConstraint(constr2);
                    }
                    if (this.preSplitRatios != null && this.preSplitRatios.ContainsKey(path))
                    {
                        var constr3 = new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path]));
                        constr3.Add(this.DemandVariables[pair].Multiply(this.preSplitRatios[path]));
                        this.innerProblemEncoder.AddEqZeroConstraint(constr3);
                    }
                }
            }
        }
        private Dictionary<Edge, Polynomial<TVar>> ComputeUtilizationPerEdge(HashSet<string> excludeEdges)
        {
            var utilizationPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            Logger.Info("computing utilization per edge");
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                foreach (var path in paths)
                {
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.Topology.GetEdge(source, target);
                        var term = new Term<TVar>(1, this.FlowPathVariables[path]);
                        if (excludeEdges == null || (excludeEdges != null && (!excludeEdges.Contains(source) || !excludeEdges.Contains(target))))
                        {
                            if (!utilizationPerEdge.ContainsKey(edge))
                            {
                                utilizationPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
                            }
                            utilizationPerEdge[edge].Add(term.Multiply(Math.Round(1.0 / edge.Capacity, this.FRACTION_DIG)));
                        }
                    }
                }
            }
            return utilizationPerEdge;
        }
        /// <summary>
        /// Sets the capacity constraints for the paths and the extensions of them.
        /// </summary>
        public override void AddCapCapacityConstraints()
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
                    if (this.PathExtensionCapacities.ContainsKey(path))
                    {
                        if (!this.FlowPathVariables.ContainsKey(path))
                        {
                            continue;
                        }
                        var extensionCapacityConstraint = new Polynomial<TVar>(new Term<TVar>(0));
                        extensionCapacityConstraint.Add(this.PathExtensionCapacities[path].Copy().Negate().Multiply(100 * this.MaxNumPaths));
                        extensionCapacityConstraint.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                        this.innerProblemEncoder.AddLeqZeroConstraint(extensionCapacityConstraint);
                    }
                }
            }
        }
        /// <summary>
        /// Compute MLU objective.
        /// </summary>
        /// <param name="utilizationPerEdge"></param>
        protected void ComputeMLU(Dictionary<Edge, Polynomial<TVar>> utilizationPerEdge)
        {
            foreach (var (edge, utilPoly) in utilizationPerEdge)
            {
                // MLU >= link util.
                var poly = new Polynomial<TVar>(new Term<TVar>(-1, this.MLU));
                poly.Add(utilPoly.Copy());
                this.innerProblemEncoder.AddLeqZeroConstraint(poly);
            }
        }
        /// <summary>
        /// Compute MLU objective.
        /// </summary>
        /// <param name="noAdditionalConstraints"></param>
        /// <returns></returns>
        protected Polynomial<TVar> GenerateFullConstraints(bool noAdditionalConstraints)
        {
            var objective = new Polynomial<TVar>(new Term<TVar>(-1, this.MLU));
            Logger.Info("Calling inner encoder.");
            this.innerProblemEncoder.AddMaximizationConstraints(objective, noKKT: noAdditionalConstraints);
            return objective;
        }
        private void EnsureDemandsEqual()
        {
            Logger.Info("Ensuring d_k = sum_p f_k^p");
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                var poly = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var path in paths)
                {
                    poly.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                }
                poly.Add(this.DemandVariables[pair].Negate());
                this.innerProblemEncoder.AddEqZeroConstraint(poly);
            }
        }
        /// <summary>
        /// Encodes the failure analysis cust scenario.See the overleaf doc for details of the formulation.
        /// </summary>
        public override OptimizationEncoding<TVar, TSolution> Encoding(
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
            HashSet<string> excludeEdges = null,
            int numProcesses = -1)
            {
                this.Topology = topology;
                Logger.Info("Initialize the base optimizer variables");
                this.InitializeVariables(preDemandVariables, demandEqualityConstraints, innerRewriteMethod, pathType, selectedPaths, numProcesses);
                this.MLU = this.Solver.CreateVariable("MLU");
                this.variables.Add(this.MLU);
                if (selectedPaths != null)
                {
                    if (selectedPaths.Count != this.Topology.GetNodePairs().Count() && selectedPaths.Count != preDemandVariables.Count)
                    {
                        throw new ArgumentException("The number of selected paths does not match the number of node pairs in the graph.");
                    }
                    this.Paths = selectedPaths;
                }
                Logger.Info("Initalize the failure analysis scenario variables.");
                this.InitializeFailureAnalysisVariables(preCapVariables, capacityEqualityConstraints, prePathExtensionCapacities, pathExtensionCapacityConstraints, numProcesses);
                this.CheckPreSetConstraints(demandEqualityConstraints, capacityEqualityConstraints, pathExtensionCapacityConstraints);
                this.MeetPreSetDemands();
                this.MeetPreSetCapacities();
                this.MeetPreSetExtensionCapacities();
                this.BoundFlowVariables();
                this.EnsureNoFlowOnDisconnected();
                this.ComputeTotalFlowPerDemand();
                EnsureDemandsEqual();
                var utilizationPerEdge = ComputeUtilizationPerEdge(excludeEdges);
                foreach (var edge in utilizationPerEdge.Keys)
                {
                    var constrain = this.CapacityVariables[(edge.Source, edge.Target)].Copy().Negate().Multiply(6000);
                    constrain.Add(utilizationPerEdge[edge]);
                    this.innerProblemEncoder.AddLeqZeroConstraint(constrain);
                }
                ComputeMLU(utilizationPerEdge);
                Logger.Info("Ensuring lb and ub of split ratio are respected");
                EnsureSplitRatioConstraints();
                AddCapCapacityConstraints();
                Logger.Info("Generate full constraints.");
                Polynomial<TVar> objective = GenerateFullConstraints(noAdditionalConstraints);

                return new TEOptimizationEncoding<TVar, TSolution>
                {
                    GlobalObjective = this.MLU,
                    MaximizationObjective = objective,
                    DemandVariables = this.DemandVariables,
                };
            }
        /// <summary>
        /// Gets the solution of the optimization in the right format.
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public override OptimizationSolution GetSolution(TSolution solution)
        {
            var lagStatus = new Dictionary<Edge, double>();
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
                            lagFlows[edge] = this.Solver.GetVariable(solution, this.FlowPathVariables[path]);
                        }
                    }
                }
            }
            foreach (var edge in this.Topology.GetAllEdges())
            {
                var source = edge.Source;
                var target = edge.Target;
                if (!lagStatus.ContainsKey(edge))
                {
                    lagStatus[edge] = 0;
                }
                foreach (var term in this.CapacityVariables[(source, target)].GetTerms())
                {
                    lagStatus[edge] += this.Solver.GetVariable(solution, term.Variable.Value);
                }
            }
            foreach (var (pair, variable) in this.FlowVariables)
            {
                flows[pair] = this.Solver.GetVariable(solution, variable);
            }
            return new FailureAnalysisOptimizationSolution
            {
                Demands = demands,
                LagStatus = lagStatus,
                Flows = flows,
                FlowsPaths = flowPaths,
                LagFlows = lagFlows,
                MaxObjective = -1 * this.Solver.GetVariable(solution, this.MLU),
            };
        }
    }
}