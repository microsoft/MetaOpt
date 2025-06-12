namespace MetaOptimize.FailureAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Mail;
    using Gurobi;
    using NLog;
    /// <summary>
    /// This uses the path form to do capacity augmentation but it only increases capacity on existing lags as opposed to adding new lags to the network.
    /// </summary>
    public class CapacityAugmentsOnExisting<TVar, TSolution>
    {
        private double MinCapacity { get; set; }
        /// <summary>
        /// Number of paths to use for path computation.
        /// </summary>
        protected int MaxNumPaths { get; set; }

        private Topology topology;
        /// <summary>
        /// The solver we will be using.
        /// </summary>
        /// <value></value>
        public ISolver<TVar, TSolution> Solver { get; set; }
        /// <summary>
        /// These are the augmentation variables --> how much capacity we will be adding to each lag.
        /// </summary>
        public Dictionary<(string, string), TVar> AugmentVariables { get; set; }
        /// <summary>
        /// The variables in the optimization.
        /// </summary>
        protected ISet<TVar> variables;
        /// <summary>
        /// The total demand met variable.
        /// </summary>
        /// <value></value>
        public TVar TotalDemandMetVariable { get; set; }
        /// <summary>
        /// The metANode to actual node set so i know who the metanodes are.
        /// </summary>
        public Dictionary<string, HashSet<string>> MetaNodeToActualNode = null;
        private Dictionary<(string, string), string[][]> Paths = null;
        private Dictionary<(string, string), TVar> FlowVariables = null;
        private Dictionary<string[], TVar> FlowPathVariables = null;
        /// <summary>
        /// Number of lags added as the objective.
        /// </summary>
        /// <value></value>
        public TVar NumberOfLagsAdded { get; set; }
        /// <summary>
        /// Initializer for this class.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="MetaNodeToActualNode"></param>
        /// <param name="maxNumPath"></param>
        public CapacityAugmentsOnExisting(ISolver<TVar, TSolution> solver, Dictionary<string, HashSet<string>> MetaNodeToActualNode = null, int maxNumPath = 1)
        {
            this.Solver = solver;
            this.MetaNodeToActualNode = MetaNodeToActualNode;
            this.MaxNumPaths = maxNumPath;
        }
        /// <summary>
        /// Sets the objective we want to optimize for.
        /// </summary>
        /// <returns></returns>
        public Polynomial<TVar> GetObjective()
        {
            var objective = new Polynomial<TVar>(new Term<TVar>(0));
            foreach (var (edge, lagVar) in this.AugmentVariables)
            {
                objective.Add(new Term<TVar>(-1, lagVar));
            }
            return objective;
        }
        private Topology FilteredTopology()
        {
            var t = new Topology();
            foreach (var node in this.topology.GetAllNodes())
            {
                if (!this.MetaNodeToActualNode.ContainsKey(node))
                {
                    t.AddNode(node);
                }
            }
            foreach (var edge in this.topology.GetAllEdges())
            {
                if (!this.MetaNodeToActualNode.ContainsKey(edge.Source) && !this.MetaNodeToActualNode.ContainsKey(edge.Target))
                {
                    t.AddEdge(edge.Source, edge.Target, edge.Capacity);
                }
            }
            return t;
        }
        /// <summary>
        /// Sets the path for the failure analysis use-case.
        /// Note that this is different from the function of the same name in failure analysis adversarial generator.
        /// Here, we only define path proper between the metanodes because the assumption is that the only non-negative demand is between metanodes.
        /// One ohter differentiator that is important here is that I am not allowing traffic on backup paths, because we are looking at whether the network in the
        /// abscence of failure can carry the traffic we want it to carry.
        /// </summary>
        /// <param name="pathType"></param>
        public void SetPath(PathType pathType = PathType.KSP)
        {
            Topology t = this.FilteredTopology();
            this.Paths = this.topology.ComputePaths(pathType, null, this.MaxNumPaths, -1, false);
            var filteredPaths = t.ComputePaths(pathType, null, this.MaxNumPaths, -1, false);
            foreach (var pair in this.Paths.Keys)
            {
                if (this.MetaNodeToActualNode.ContainsKey(pair.Item1) && this.MetaNodeToActualNode.ContainsKey(pair.Item2))
                {
                    this.Paths[pair] = new string[0][];
                    foreach (var sourceEdge in MetaNodeToActualNode[pair.Item1])
                    {
                        foreach (var destinationEdge in MetaNodeToActualNode[pair.Item2])
                        {
                            this.Paths[pair] = this.Paths[pair].Concat(filteredPaths[(sourceEdge, destinationEdge)].Select(r => r.Prepend(pair.Item1).Append(pair.Item2).ToArray())).ToArray();
                        }
                    }
                }
            }
            this.Paths = this.Paths.Where(p => this.MetaNodeToActualNode.ContainsKey(p.Key.Item1) && this.MetaNodeToActualNode.ContainsKey(p.Key.Item2)).ToDictionary(p => p.Key, p => p.Value);
        }
        /// <summary>
        /// Ensure that the flow f_k = sum_p f_k^p.
        /// </summary>
        /// <param name="demands"></param>
        public void ComputeTotalFlowPerDemand(Dictionary<(string, string), double> demands)
        {
            foreach (var (pair, paths) in this.Paths)
            {
                if (!demands.ContainsKey(pair) && this.FlowVariables.ContainsKey(pair))
                {
                    throw new Exception("Ther is no demand variable for this pair but there is a flow variable.");
                }
                var computeFlow = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var path in paths)
                {
                    computeFlow.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                }
                computeFlow.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.Solver.AddEqZeroConstraint(computeFlow);
            }
        }
        /// <summary>
        /// Initializes the variables.
        /// </summary>
        /// <param name="demands"></param>
        /// <param name="flows"></param>
        protected void InitializeVariables(Dictionary<(string, string), double> demands, Dictionary<(string, string), double> flows)
        {
            this.MinCapacity = this.topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Min();
            this.AugmentVariables = new Dictionary<(string, string), TVar>();
            this.TotalDemandMetVariable = this.Solver.CreateVariable("total_demand_met", lb: 0);
            this.variables = new HashSet<TVar>();
            this.variables.Add(this.TotalDemandMetVariable);
            this.NumberOfLagsAdded = this.Solver.CreateVariable("NumberOfLagsAdded", lb: 0);
            this.variables.Add(this.NumberOfLagsAdded);

            this.FlowVariables = new Dictionary<(string, string), TVar>();
            this.FlowPathVariables = new Dictionary<string[], TVar>(new PathComparer());
            var avgCapacity = this.topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Average();
            foreach (var pair in demands.Keys)
            {
                this.FlowVariables[pair] = this.Solver.CreateVariable("Flow_" + pair.Item1 + "_" + pair.Item2, lb: this.Paths[pair].Where(x => x.Length > 0).Count() > 0 ? flows[pair] : 0, ub: demands[pair]);
                this.variables.Add(this.FlowVariables[pair]);
                foreach (var simplePath in this.Paths[pair])
                {
                    if (simplePath.Length == 0)
                    {
                        this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("flowPath_" + string.Join("_", simplePath), lb: 0, ub: 0);
                        this.variables.Add(this.FlowPathVariables[simplePath]);
                        continue;
                    }
                    this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("FlowPath_" + string.Join("_", simplePath), lb: 0);
                    this.variables.Add(this.FlowPathVariables[simplePath]);
                }
                foreach (var path in this.Paths[pair])
                {
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        if (this.AugmentVariables.ContainsKey((path[i], path[i + 1])))
                        {
                            continue;
                        }
                        this.AugmentVariables[(path[i], path[i + 1])] = this.Solver.CreateVariable("augment_" + path[i] + "_" + path[i + 1], lb: 0);
                        this.variables.Add(this.AugmentVariables[(path[i], path[i + 1])]);
                    }
                }
            }
        }
        /// <summary>
        /// Add Capacity constraints.
        /// </summary>
        public void AddCapacityConstraints()
        {
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    for (int i = 0; i < path.Length - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.topology.GetEdge(source, target);
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
                total.Add(new Term<TVar>(-1 * edge.Capacity));
                total.Add(new Term<TVar>(-1, this.AugmentVariables[(edge.Source, edge.Target)]));
                this.Solver.AddLeqZeroConstraint(total);
            }
        }
        /// <summary>
        /// The totla demand dtarget constraint is added here.
        /// </summary>
        /// <param name="target"></param>
        public void AddTotalDemandTarget(double target)
        {
            var targetConstraint = new Polynomial<TVar>(new Term<TVar>(target));
            targetConstraint.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));
            this.Solver.AddLeqZeroConstraint(targetConstraint);
        }
        /// <summary>
        /// Encodes the capacity augmentation problem.
        /// I have not added an inner problem encoder on purpose to this one.
        /// </summary>
        /// <param name="topology"></param>
        /// <param name="demands"></param>
        /// <param name="flows"></param>
        /// <param name="targetDemandMet"></param>
        /// <returns></returns>
        public OptimizationEncoding<TVar, TSolution> Encoding(Topology topology,
                                                              Dictionary<(string, string), double> demands = null,
                                                              Dictionary<(string, string), double> flows = null,
                                                              double targetDemandMet = -1)
        {
            if (targetDemandMet < 0)
            {
                throw new Exception("This function is not meant to be used this way");
            }
            this.topology = topology.Copy();
            this.SetPath();
            InitializeVariables(demands, flows);
            ComputeTotalFlowPerDemand(demands);
            AddCapacityConstraints();
            AddTotalDemandTarget(targetDemandMet);
            var objective = GetObjective();
            var setNumLagsAdded = objective.Copy();
            setNumLagsAdded.Add(new Term<TVar>(1, this.NumberOfLagsAdded));
            this.Solver.AddEqZeroConstraint(setNumLagsAdded);
            return new OptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.NumberOfLagsAdded,
                MaximizationObjective = objective,
            };
        }
        /// <summary>
        /// Parses out the solution of the optimization.
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var AddedLags = new Dictionary<(string, string), double>();
            foreach (var edge in this.topology.GetAllEdges())
            {
                var source = edge.Source;
                var target = edge.Target;
                if (!this.AugmentVariables.ContainsKey((source, target)))
                {
                    continue;
                }
                AddedLags[(source, target)] = this.Solver.GetVariable(solution, this.AugmentVariables[(source, target)]);
            }
            return new CapacityAugmentSolution
            {
                LagStatus = AddedLags,
            };
        }
    }
}