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
    /// <summary>
    /// This is a class similar to failureAnalysisEncoder the only difference is that it changes one of the assumptions in the analysis encoder:
    /// that the number of primary paths is the same across all demands.
    /// </summary>
    /// <typeparam name="TVar"></typeparam>
    /// <typeparam name="TSolution"></typeparam>
    public class FailureAnalysisEncoderWithUnequalPaths<TVar, TSolution> : FailureAnalysisEncoder<TVar, TSolution>, IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// A dictionary of the primary paths in the problem.
        /// </summary>
        public Dictionary<(string, string), string[][]> primaryPaths;
        /// <summary>
        /// A dictionary of the backup paths in the problem.
        /// </summary>
        public Dictionary<(string, string), string[][]> backupPaths;
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        /// <summary>
        /// Initialization function. WARNING: the max num path will be ignored.
        /// </summary>
        /// <param name="solver"></param>
        public FailureAnalysisEncoderWithUnequalPaths(ISolver<TVar, TSolution> solver)
        : base(solver, 0)
        {
        }
        /// <summary>
        /// Initialize the variables for the optimal encoding.
        /// </summary>
        /// <param name="preDemandVariables"></param>
        /// <param name="demandEqualityConstraints"></param>
        /// <param name="rewriteMethod"></param>
        protected void InitializeVariables(Dictionary<(string, string), Polynomial<TVar>> preDemandVariables,
                                           Dictionary<(string, string), double> demandEqualityConstraints, InnerRewriteMethodChoice rewriteMethod)
        {
            this.variables = new HashSet<TVar>();
            this.DemandConstraints = demandEqualityConstraints ?? new Dictionary<(string, string), double>();
            this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
            var demandVariables = new HashSet<TVar>();
            if (preDemandVariables == null)
            {
                this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
                foreach (var pair in this.Topology.GetNodePairs())
                {
                    if (!IsDemandValid(pair, false))
                    {
                        continue;
                    }
                    var variable = this.Solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2);
                    this.DemandVariables[pair] = new Polynomial<TVar>(new Term<TVar>(1, variable));
                    this.variables.Add(variable);
                    demandVariables.Add(variable);
                }
            }
            else
            {
                foreach (var (pair, variable) in preDemandVariables)
                {
                    if (!IsDemandValid(pair, false))
                    {
                        continue;
                    }
                    this.DemandVariables[pair] = variable;
                    foreach (var term in variable.GetTerms())
                    {
                        this.variables.Add(term.Variable.Value);
                        demandVariables.Add(term.Variable.Value);
                    }
                }
            }
            this.TotalDemandMetVariable = this.Solver.CreateVariable("Total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);
            this.FlowVariables = new Dictionary<(string, string), TVar>();
            this.FlowPathVariables = new Dictionary<string[], TVar>(new PathComparer());
            foreach (var pair in this.DemandVariables.Keys)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                this.FlowVariables[pair] = this.Solver.CreateVariable("flow_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);
                if (this.primaryPaths.ContainsKey(pair))
                {
                    foreach (var simplePath in this.primaryPaths[pair])
                    {
                        Debug.Assert(this.FlowPathVariables.ContainsKey(simplePath) == false, "The path {0} is already in the flow path variables.", string.Join("_", simplePath));
                        this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("flowPath_" + string.Join("_", simplePath));
                        this.variables.Add(this.FlowPathVariables[simplePath]);
                    }
                }
                if (this.backupPaths.ContainsKey(pair))
                {
                    foreach (var simplePath in this.backupPaths[pair])
                    {
                        Debug.Assert(this.FlowPathVariables.ContainsKey(simplePath) == false, "The path {0} is already in the flow path variables.", string.Join("_", simplePath));
                        this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("flowPath_" + string.Join("_", simplePath));
                        this.variables.Add(this.FlowPathVariables[simplePath]);
                    }
                }
            }
            switch (rewriteMethod)
            {
                case InnerRewriteMethodChoice.KKT:
                    this.innerProblemEncoder = new KKTRewriteGenerator<TVar, TSolution>(this.Solver, this.variables, demandVariables);
                    break;
                case InnerRewriteMethodChoice.PrimalDual:
                    this.innerProblemEncoder = new PrimalDualRewriteGenerator<TVar, TSolution>(this.Solver,
                                                                                               this.variables,
                                                                                               demandVariables,
                                                                                               -1);
                    break;
                default:
                    throw new ("Invalid method for encoding the inner problem");
            }
        }
        /// <summary>
        /// This function is mostly similar to its counterpart in failureAnalysisEncoder.
        /// But I have made the change that it ensures we allocate variables correctly depending on
        /// whether the path is a primary or a backup path.
        /// </summary>
        /// <param name="preCapVariables"></param>
        /// <param name="capacityEqualityConstraints"></param>
        /// <param name="prePathExtensionCapacities"></param>
        /// <param name="pathExtensionCapacityConstraints"></param>
        /// <param name="numProcesses"></param>
        protected override void InitializeFailureAnalysisVariables(Dictionary<(string, string), Polynomial<TVar>> preCapVariables, Dictionary<(string, string), double> capacityEqualityConstraints, Dictionary<string[], Polynomial<TVar>> prePathExtensionCapacities, Dictionary<string[], double> pathExtensionCapacityConstraints, int numProcesses)
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
                    var variable = this.Solver.CreateVariable("EdgeCapacity_" + source + "_" + target);
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
                foreach (var (pair, paths) in this.primaryPaths)
                {
                    if (!this.IsDemandValid(pair))
                    {
                        continue;
                    }
                    foreach (var simplePath in this.primaryPaths[pair])
                    {
                        var variable = this.Solver.CreateVariable("ExtensionCapacity_" + string.Join("_", simplePath));
                        this.PathExtensionCapacities[simplePath] = new Polynomial<TVar>(new Term<TVar>(1, variable));
                        this.variables.Add(variable);
                        this.innerProblemEncoder.AddConstantVar(variable);
                    }
                }
                foreach (var (pair, paths) in this.backupPaths)
                {
                    if (!this.IsDemandValid(pair))
                    {
                        continue;
                    }
                    foreach (var simplePath in this.backupPaths[pair])
                    {
                        var variable = this.Solver.CreateVariable("ExtensionCapacity_" + string.Join("_", simplePath));
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
        private Dictionary<Edge, Polynomial<TVar>> AddCapCapacityConstraintsPerPathType(Dictionary<(string, string), string[][]> pathSet, Dictionary<Edge, Polynomial<TVar>> sumPerEdge)
        {
            foreach (var (pair, paths) in pathSet)
            {
                if (!this.IsDemandValid(pair))
                {
                    continue;
                }
                foreach (var path in paths)
                {
                    // For a path p_k ensure f_pk <= c_l'_pk. Where l'_pk is
                    // an auxiliary link to capture the constraint that only paths
                    // are active that are part of the k-shortest paths.
                    if (this.PathExtensionCapacities.ContainsKey(path))
                    {
                        if (!this.FlowPathVariables.ContainsKey(path))
                        {
                            Console.WriteLine("The path {0} is not in the flow path variables.", string.Join("_", path));
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
            return sumPerEdge;
        }
        /// <summary>
        /// This is an adapted version of the function of the same name in the original encoder.
        /// The main difference here is that we have a different data structure for the number of primary and backup paths.
        /// </summary>
        public override void AddCapCapacityConstraints()
        {
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            sumPerEdge = AddCapCapacityConstraintsPerPathType(this.primaryPaths, sumPerEdge);
            sumPerEdge = AddCapCapacityConstraintsPerPathType(this.backupPaths, sumPerEdge);
            foreach (var (edge, total) in sumPerEdge)
            {
                total.Add(this.CapacityVariables[(edge.Source, edge.Target)].Copy().Negate());
                this.innerProblemEncoder.AddLeqZeroConstraint(total);
            }
        }
        /// <summary>
        /// Ensure that f_k \geq 0.
        /// Ensure that f_k leq d_k.
        /// Ensure that f_k^p geq 0.
        /// </summary>
        public override void BoundFlowVariables()
        {
            foreach (var (pair, variable) in this.FlowVariables)
            {
                var flowSizeConstraints = this.DemandVariables[pair].Negate();
                flowSizeConstraints.Add(new Term<TVar>(1, variable));
                this.innerProblemEncoder.AddLeqZeroConstraint(flowSizeConstraints);
            }
            foreach (var (pair, paths) in this.primaryPaths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                foreach (var path in paths)
                {
                    this.innerProblemEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path])));
                }
            }
            foreach (var (pair, paths) in this.backupPaths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                foreach (var path in paths)
                {
                    this.innerProblemEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path])));
                }
            }
        }
        /// <summary>
        /// Ensure that nodes that are not connected have no flow or demand.
        /// This is needed for not fully connected topologies.
        /// This function is basically the same as its counterpart with the caveat that it is merging between the
        /// primary and backup paths.
        /// </summary>
        public override void EnsureNoFlowOnDisconnected()
        {
            foreach (var (pair, primaries) in this.primaryPaths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                if (!this.backupPaths.ContainsKey(pair))
                {
                    continue;
                }
                foreach (var backups in this.backupPaths[pair])
                {
                    if (primaries.Length == 0 && backups.Length == 0)
                    {
                        this.innerProblemEncoder.AddEqZeroConstraint(this.DemandVariables[pair].Copy());
                        this.innerProblemEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.FlowVariables[pair])));
                    }
                }
            }
        }
        /// <summary>
        /// Ensure that the flow f_k = sum_p f_k^p.
        /// Modifying this also to accomodate split paths.
        /// </summary>
        public override void ComputeTotalFlowPerDemand()
        {
            foreach (var pair in this.FlowVariables.Keys)
            {
                if (!this.DemandVariables.ContainsKey(pair) && this.FlowVariables.ContainsKey(pair))
                {
                    throw new Exception("There is no demand variable for this pair but there is a flow variable");
                }
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                var computeFlow = new Polynomial<TVar>(new Term<TVar>(0));
                if (this.primaryPaths.ContainsKey(pair))
                {
                    foreach (var path in this.primaryPaths[pair])
                    {
                        computeFlow.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                    }
                }
                if (this.backupPaths.ContainsKey(pair))
                {
                    foreach (var path in this.backupPaths[pair])
                    {
                        computeFlow.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                    }
                }
                computeFlow.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.innerProblemEncoder.AddEqZeroConstraint(computeFlow);
            }
        }
        /// <summary>
        /// Solves the problem.
        /// </summary>
        /// <returns></returns>
        public OptimizationEncoding<TVar, TSolution> Encoding(
            Topology topology,
            bool modelFailures = true,
            Dictionary<(string, string), string[][]> primaryPaths = null,
            Dictionary<(string, string), string[][]> backupPaths = null,
            Dictionary<(string, string), Polynomial<TVar>> preDemandVariables = null,
            Dictionary<(string, string), Polynomial<TVar>> preCapVariables = null,
            Dictionary<string[], Polynomial<TVar>> prePathExtensionCapacities = null,
            Dictionary<(string, string), double> demandEqualityConstraints = null,
            Dictionary<(string, string), double> capacityEqualityConstraints = null,
            Dictionary<string[], double> pathExtensionCapacityConstraints = null,
            bool noAdditionalConstraints = false,
            InnerRewriteMethodChoice innerRewriteMethodChoice = InnerRewriteMethodChoice.KKT,
            PathType pathType = PathType.KSP,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            HashSet<string> excludeNodesEdges = null,
            int numProcesses = -1)
            {
                this.Topology = topology;
                this.primaryPaths = primaryPaths;
                this.backupPaths = backupPaths;
                if (this.primaryPaths == null || this.backupPaths == null)
                {
                    throw new Exception("The primary and backup paths must be provided.");
                }
                this.InitializeVariables(preDemandVariables, demandEqualityConstraints, innerRewriteMethodChoice);
                this.InitializeFailureAnalysisVariables(preCapVariables, capacityEqualityConstraints, prePathExtensionCapacities, pathExtensionCapacityConstraints, numProcesses);
                this.CheckPreSetConstraints(demandEqualityConstraints, capacityEqualityConstraints, pathExtensionCapacityConstraints);
                this.EncodeTotalDemandConstraint(topology);
                this.MeetPreSetDemands();
                this.MeetPreSetCapacities();
                this.MeetPreSetExtensionCapacities();
                this.BoundFlowVariables();
                this.EnsureNoFlowOnDisconnected();
                this.ComputeTotalFlowPerDemand();
                this.AddCapCapacityConstraints();
                var objective = new Polynomial<TVar>(new Term<TVar>(1, this.TotalDemandMetVariable));
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
            foreach (var (pair, paths) in this.primaryPaths)
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
            foreach (var (pair, paths) in this.backupPaths)
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
                MaxObjective = this.Solver.GetVariable(solution, this.TotalDemandMetVariable),
            };
        }
    }
}