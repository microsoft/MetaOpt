namespace MetaOptimize.FailureAnalysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Mail;
    using Gurobi;
    using NLog;
    /// <summary>
    /// This class does capacity augmentation.
    /// The optimization it solves assumes a topology where failures have already occured,
    /// finds the minimum number of links to add such that we can carry the target demand.
    /// </summary>
    /// <typeparam name="TVar"></typeparam>
    /// <typeparam name="TSolution"></typeparam>
    public class CapacityAugmentEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private double avgCapacity { get; set; }
        private Topology Topology;
        /// <summary>
        /// The solver being used.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }
        /// <summary>
        /// These are the links that we can add to the topology.
        /// </summary>
        public Dictionary<(string, string), TVar> CapacityVariables { get; set; }
        /// <summary>
        /// The links which already exist in the original topology.
        /// </summary>
        public HashSet<(string, string)> ExistingVariables { get; set; }
        /// <summary>
        /// The set of variables used in the encoding.
        /// </summary>
        protected ISet<TVar> variables;
        /// <summary>
        /// The flow variables for the network f_k.
        /// </summary>
        public Dictionary<(string, string), TVar> FlowVariables { get; set; }
        /// <summary>
        /// One flow variable per demand per edge.
        /// </summary>
        /// <returns></returns>
        public Dictionary<(string, string), Dictionary<(string, string), TVar>> FlowLagVariables { get; set; }
        /// <summary>
        /// The total demand met variable.
        /// </summary>
        /// <value></value>
        public TVar TotalDemandMetVariable { get; set; }
        /// <summary>
        /// Number of links added as the objective.
        /// </summary>
        /// <value></value>
        public TVar NumberOfLagsAdded { get; set; }
        /// <summary>
        /// The mapping from metanodes to nodes.
        /// </summary>
        public Dictionary<string, HashSet<string>> MetaNodeToActualNode = null;
        private HashSet<string> ImpactedDemandSources = null;
        private HashSet<string> ImpactedDemandDestinations = null;
        /// <summary>
        /// Initializing the class.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="MetaNodeToActualNode"></param>
        public CapacityAugmentEncoder(ISolver<TVar, TSolution> solver, Dictionary<string, HashSet<string>> MetaNodeToActualNode = null)
        {
            this.Solver = solver;
            this.MetaNodeToActualNode = MetaNodeToActualNode;
        }
        /// <summary>
        /// Initializes the capacity variables.
        /// Todo-research: One assumption here is that we are not adding links to existing edges, just creating new edges.
        /// One can change this if we want more fine-grained behavior.
        /// </summary>
        /// <param name="demands"></param>
        /// <param name="flows"></param>
        /// <param name="exclude"></param>
        /// <param name="paths"></param>
        /// <param name="addOnExisting"></param>
        protected void InitializeVariables(Dictionary<(string, string), double> demands,
                                           Dictionary<(string, string), double> flows,
                                           Dictionary<(string, string), int> exclude = null,
                                           Dictionary<(string, string), string[][]> paths = null,
                                           bool addOnExisting = false)
        {
            this.avgCapacity = this.Topology.GetAllEdges().Where(x => x.Capacity > 0).Select(x => x.Capacity).Average();
            this.CapacityVariables = new Dictionary<(string, string), TVar>();
            this.variables = new HashSet<TVar>();
            this.ExistingVariables = new HashSet<(string, string)>();
            foreach (var (source, dest) in this.Topology.GetNodePairs())
            {
                if (exclude.ContainsKey((source, dest)))
                {
                    if (addOnExisting)
                    {
                        this.ExistingVariables.Add((source, dest));
                        this.CapacityVariables[(source, dest)] = this.Solver.CreateVariable("capacity_" + source + "_" + dest, type: GRB.BINARY);
                        this.variables.Add(this.CapacityVariables[(source, dest)]);
                        this.Topology.AddEdge(source, dest, this.avgCapacity);
                    }
                    continue;
                }
                if (this.MetaNodeToActualNode.ContainsKey(source))
                {
                    continue;
                }
                if (this.MetaNodeToActualNode.ContainsKey(dest))
                {
                    continue;
                }
                if (!this.Topology.Graph.TryGetEdge(source, dest, out var taggedEdge))
                {
                    this.CapacityVariables[(source, dest)] = this.Solver.CreateVariable("capacity_" + source + "_" + dest, type: GRB.BINARY);
                    this.variables.Add(this.CapacityVariables[(source, dest)]);
                    this.Topology.AddEdge(source, dest, this.avgCapacity);
                }
                else
                {
                    if (addOnExisting)
                    {
                        this.ExistingVariables.Add((source, dest));
                        this.CapacityVariables[(source, dest)] = this.Solver.CreateVariable("capacity_" + source + "_" + dest, type: GRB.BINARY);
                        this.variables.Add(this.CapacityVariables[(source, dest)]);
                        this.Topology.AddEdge(source, dest, this.avgCapacity);
                    }
                }
            }
            this.TotalDemandMetVariable = this.Solver.CreateVariable("total_demand_met", lb: 0);
            this.NumberOfLagsAdded = this.Solver.CreateVariable("NumberOfLinksAdded", lb: 0);
            this.variables.Add(this.TotalDemandMetVariable);
            this.variables.Add(this.NumberOfLagsAdded);
            this.FlowVariables = new Dictionary<(string, string), TVar>();
            this.FlowLagVariables = new Dictionary<(string, string), Dictionary<(string, string), TVar>>();
            foreach (var pair in demands.Keys)
            {
                this.FlowVariables[pair] = this.Solver.CreateVariable("flow_" + pair.Item1 + "_" + pair.Item2, lb: flows[pair]);
                this.variables.Add(this.FlowVariables[pair]);
            }
            foreach (var edge in this.Topology.GetAllEdges())
            {
                this.FlowLagVariables[(edge.Source, edge.Target)] = new Dictionary<(string, string), TVar>();
                foreach (var pair in demands.Keys)
                {
                    if (paths == null || this.CapacityVariables.ContainsKey((edge.Source, edge.Target)))
                    {
                        this.FlowLagVariables[(edge.Source, edge.Target)][pair] = this.Solver.CreateVariable("flowLag_" + edge.Source + "_" + edge.Target + "_demand_" + pair.Item1 + "_" + pair.Item2, lb: 0, ub: edge.Capacity);
                        this.variables.Add(this.FlowLagVariables[(edge.Source, edge.Target)][pair]);
                    }
                }
            }
            this.ImpactedDemandSources = new HashSet<string>();
            this.ImpactedDemandDestinations = new HashSet<string>();
            if (paths != null)
            {
                foreach (var pair in demands.Keys)
                {
                    var pathSet = paths[pair];
                    foreach (var path in pathSet)
                    {
                        for (int i = 0; i < path.Length - 1; i++)
                        {
                            if (this.CapacityVariables.ContainsKey((path[i], path[i + 1])))
                            {
                                throw new Exception("Check because the intuition does not match what we expect.");
                            }
                            if (exclude.ContainsKey((path[i], path[i + 1])))
                            {
                                this.ImpactedDemandSources.Add(pair.Item1);
                                this.ImpactedDemandDestinations.Add(pair.Item2);
                                continue;
                            }
                            if (this.FlowLagVariables[(path[i], path[i + 1])].ContainsKey(pair))
                            {
                                continue;
                            }
                            this.FlowLagVariables[(path[i], path[i + 1])][pair] = this.Solver.CreateVariable("flowLag_" + path[i] + "_" + path[i + 1] + "_demand_" + pair.Item1 + "_" + pair.Item2, lb: 0, ub: this.Topology.GetEdge(path[i], path[i + 1]).Capacity);
                            this.variables.Add(this.FlowLagVariables[(path[i], path[i + 1])][pair]);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Ensure total demand = sum flows.
        /// </summary>
        /// <param name="topology"></param>
        /// <param name="demands"></param>
        public void EncodeTotalDemandConstraint(Topology topology, Dictionary<(string, string), double> demands)
        {
            Logger.Info("Ensuring sum_k f_k = total demands");
            var totalFlowEquality = new Polynomial<TVar>();
            if (demands.Count != this.FlowVariables.Count)
            {
                throw new Exception("There is a bug in the code somewhere");
            }
            foreach (var (pair, demand) in demands)
            {
                totalFlowEquality.Add(new Term<TVar>(1, this.FlowVariables[pair]));
            }
            totalFlowEquality.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));
            this.Solver.AddEqZeroConstraint(totalFlowEquality);
        }
        /// <summary>
        /// Ensure that f_k gap 0.
        /// Ensure f_k leq d_k.
        /// Ensure f_k^l geq 0.
        /// </summary>
        /// <param name="demands"></param>
        public void BoundFlowVariables(Dictionary<(string, string), double> demands)
        {
            Logger.Info("Ensuring that flows are within the correct range.");
            foreach (var (pair, variable) in this.FlowVariables)
            {
                if (!demands.ContainsKey(pair))
                {
                    throw new Exception("I shouldn't have this flow variable.");
                }
                var flowSizeConstraints = new Polynomial<TVar>(new Term<TVar>(-1 * demands[pair]));
                flowSizeConstraints.Add(new Term<TVar>(1, variable));
                this.Solver.AddLeqZeroConstraint(flowSizeConstraints);
            }
        }
        /// <summary>
        /// Ensure that flow f_k = sum_{j} f_{(s,j)k}.
        /// </summary>=
        public void ComputeTotalFlowPerDemand(Dictionary<(string, string), double> demands)
        {
            Logger.Info("Ensuring f_k = sum_p f_k^p");
            foreach (var pair in demands.Keys)
            {
                var computeFlow = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var edge in this.FlowLagVariables.Keys.Where(x => x.Item1 == pair.Item1))
                {
                    computeFlow.Add(new Term<TVar>(1, this.FlowLagVariables[edge][pair]));
                }
                computeFlow.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.Solver.AddEqZeroConstraint(computeFlow);
                computeFlow = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var edge in this.FlowLagVariables.Keys.Where(x => x.Item2 == pair.Item2))
                {
                    computeFlow.Add(new Term<TVar>(1, this.FlowLagVariables[edge][pair]));
                }
                this.Solver.AddEqZeroConstraint(computeFlow);
            }
        }
        /// <summary>
        /// Sets flow conservation constraints per demand.
        /// </summary>
        /// <param name="demands"></param>
        public void AddFlowConservationConstraints(Dictionary<(string, string), double> demands)
        {
            foreach (var node in this.Topology.GetAllNodes())
            {
                var outGoingEdges = this.Topology.GetAllEdges().Where(x => x.Source == node);
                var incomingEdges = this.Topology.GetAllEdges().Where(x => x.Target == node);
                foreach (var pair in demands.Keys)
                {
                    if (node == pair.Item2 || node == pair.Item2)
                    {
                        continue;
                    }
                    var flowConservationConstraints = new Polynomial<TVar>(new Term<TVar>(0));
                    foreach (var edge in outGoingEdges)
                    {
                        flowConservationConstraints.Add(new Term<TVar>(-1, this.FlowLagVariables[(edge.Source, edge.Target)][pair]));
                    }
                    foreach (var edge in incomingEdges)
                    {
                        flowConservationConstraints.Add(new Term<TVar>(1, this.FlowLagVariables[(edge.Source, edge.Target)][pair]));
                    }
                    this.Solver.AddEqZeroConstraint(flowConservationConstraints);
                }
            }
        }
        /// <summary>
        /// Adds capacity constraints.
        /// </summary>
        /// <param name="demands"></param>
        /// <param name="addOnExisting"></param>
        public void AddCapacityConstraints(Dictionary<(string, string), double> demands, bool addOnExisting)
        {
            foreach (var edge in this.Topology.GetAllEdges())
            {
                var sumForEdge = new Polynomial<TVar>(new Term<TVar>(0));
                if (this.CapacityVariables.ContainsKey((edge.Source, edge.Target)) && (!this.ExistingVariables.Contains((edge.Source, edge.Target)) || !addOnExisting))
                {
                    sumForEdge.Add(new Term<TVar>(-1 * this.avgCapacity, this.CapacityVariables[(edge.Source, edge.Target)]));
                }
                else
                {
                    if (this.CapacityVariables.ContainsKey((edge.Source, edge.Target)))
                    {
                        sumForEdge.Add(new Term<TVar>(-1 * this.avgCapacity, this.CapacityVariables[(edge.Source, edge.Target)]));
                    }
                    sumForEdge.Add(new Term<TVar>(-1 * edge.Capacity));
                }
                foreach (var pair in demands.Keys)
                {
                    sumForEdge.Add(new Term<TVar>(1, this.FlowLagVariables[(edge.Source, edge.Target)][pair]));
                }
                this.Solver.AddLeqZeroConstraint(sumForEdge);
            }
        }
        /// <summary>
        /// The total demand target constraint is added here.
        /// </summary>
        /// <param name="target"></param>
        public void AddTotalDemandTarget(double target)
        {
            var targetConstraint = new Polynomial<TVar>(new Term<TVar>(target));
            targetConstraint.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));
            this.Solver.AddLeqZeroConstraint(targetConstraint);
        }
        /// <summary>
        /// Sets the objective we want to optimize for.
        /// </summary>
        /// <param name="specialWeight"></param>
        /// <returns></returns>
        public Polynomial<TVar> GetObjective(bool specialWeight)
        {
            double weight = -1;
            Dictionary<(string, string), string[][]> paths = null;
            if (specialWeight)
            {
                paths = this.Topology.ComputePaths(PathType.KSP, null, 1, -1, false);
            }
            var objective = new Polynomial<TVar>(new Term<TVar>(0));
            foreach (var (edge, lagVar) in this.CapacityVariables)
            {
                if (specialWeight)
                {
                    weight = paths.Where(path => (this.ImpactedDemandSources.Contains(path.Key.Item1) && (path.Key.Item2 == edge.Item1)) || (this.ImpactedDemandDestinations.Contains(path.Key.Item2) && (path.Key.Item1 == edge.Item2)))
                                  .SelectMany(paths => paths.Value).Select(path => path.Count()).DefaultIfEmpty(this.Topology.GetAllNodes().Count()).Min() * -1;
                }
                objective.Add(new Term<TVar>(weight, lagVar));
            }
            return objective;
        }
        private void AddMinAugmentation(int minAugmentation)
        {
            var minAugmentationConstraint = new Polynomial<TVar>(new Term<TVar>(1 * minAugmentation));
            foreach (var lag in this.CapacityVariables.Keys)
            {
                minAugmentationConstraint.Add(new Term<TVar>(-1, this.CapacityVariables[lag]));
            }
            this.Solver.AddLeqZeroConstraint(minAugmentationConstraint);
        }
        /// <summary>
        /// Encodes the capacity augmentation problem.
        /// I have not added an inner problem encoder on purpose to this one.
        /// We could through in case we wanted evaluate it as a heuristic at some point.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(
            Topology topology,
            Dictionary<(string, string), double> demands = null,
            Dictionary<(string, string), double> flows = null,
            double targetDemandMet = -1,
            int minAugmentation = 0,
            Dictionary<(string, string), int> exclude = null,
            Dictionary<(string, string), string[][]> path = null,
            bool specialWeight = true,
            bool addOnExisting = false)
        {
            if (targetDemandMet < 0)
            {
                throw new Exception("This function is not meant to be used this way.");
            }
            Logger.Info("Initializing variables.");
            this.Topology = topology.Copy();
            InitializeVariables(demands, flows, exclude, path, addOnExisting);
            EncodeTotalDemandConstraint(topology, demands);
            BoundFlowVariables(demands);
            AddCapacityConstraints(demands, addOnExisting);
            AddFlowConservationConstraints(demands);
            AddMinAugmentation(minAugmentation);
            AddTotalDemandTarget(targetDemandMet);
            var objective = GetObjective(specialWeight);
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
            foreach (var edge in this.Topology.GetAllEdges())
            {
                var source = edge.Source;
                var target = edge.Target;
                if (!this.CapacityVariables.ContainsKey((source, target)))
                {
                    continue;
                }
                AddedLags[(source, target)] = this.Solver.GetVariable(solution, this.CapacityVariables[(source, target)]);
            }
            return new CapacityAugmentSolution
            {
                LagStatus = AddedLags,
            };
        }
    }
}