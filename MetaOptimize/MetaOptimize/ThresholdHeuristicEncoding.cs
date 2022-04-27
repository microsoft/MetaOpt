// <copyright file="ThresholdHeuristicEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static ZenLib.Zen;

    /// <summary>
    /// A class that encodes the threshold heuristic encoding.
    /// </summary>
    public class ThresholdHeuristicEncoding : INetworkEncoding<Zen<bool>, Zen<Real>, ZenSolution>
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

        /// <summary>
        /// The enumeration of paths between all pairs of nodes.
        /// </summary>
        public Dictionary<(string, string), IList<IList<string>>> SimplePaths { get; set; }

        /// <summary>
        /// The demand variables for the network (d_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> DemandVariables { get; set; }

        /// <summary>
        /// The threshold for the heuristic.
        /// </summary>
        public long Threshold { get; set; }

        /// <summary>
        /// Whether to add constraints ensuring we have a local optimum.
        /// </summary>
        public bool EnsureLocalOptimum { get; set; }

        /// <summary>
        /// The flow variables for the network (f_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> FlowVariables { get; set; }

        /// <summary>
        /// The flow variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<IList<string>, Zen<Real>> FlowPathVariables { get; set; }

        /// <summary>
        /// The heuristic variables for the network (h_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> HeuristicVariables { get; set; }

        /// <summary>
        /// The heuristic variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<IList<string>, Zen<Real>> HeuristicPathVariables { get; set; }

        /// <summary>
        /// The capacity used variables for each edge.
        /// </summary>
        public Dictionary<Edge, Zen<Real>> CapacityUsedVariables { get; set; }

        /// <summary>
        /// The total demand met variable.
        /// </summary>
        public Zen<Real> TotalDemandMetVariable { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="ThresholdHeuristicEncoding"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="demandVariables">The shared demand variables.</param>
        /// <param name="threshold">The threshold for the heuristic.</param>
        /// <param name="ensureLocalOptimum">Whether to ensure we have a local optimum.</param>
        public ThresholdHeuristicEncoding(Topology topology, Dictionary<(string, string), Zen<Real>> demandVariables, long threshold, bool ensureLocalOptimum)
        {
            this.Topology = topology;
            this.SimplePaths = new Dictionary<(string, string), IList<IList<string>>>();
            this.Threshold = threshold;
            this.EnsureLocalOptimum = ensureLocalOptimum;
            this.DemandVariables = demandVariables;
            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            this.HeuristicVariables = new Dictionary<(string, string), Zen<Real>>();
            this.HeuristicPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            this.CapacityUsedVariables = new Dictionary<Edge, Zen<Real>>();
            this.TotalDemandMetVariable = Symbolic<Real>();
            this.InitVariables();
        }

        /// <summary>
        /// Initialize all the encoding variables.
        /// </summary>
        private void InitVariables()
        {
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var simplePaths = this.Topology.SimplePaths(pair.Item1, pair.Item2).ToList();
                this.FlowVariables[pair] = Symbolic<Real>();
                this.HeuristicVariables[pair] = Symbolic<Real>();
                this.SimplePaths[pair] = simplePaths;

                foreach (var simplePath in simplePaths)
                {
                    this.FlowPathVariables[simplePath] = Symbolic<Real>();
                    this.HeuristicPathVariables[simplePath] = Symbolic<Real>();
                }
            }

            foreach (var edge in this.Topology.GetAllEdges())
            {
                this.CapacityUsedVariables[edge] = Symbolic<Real>();
            }
        }

        /// <summary>
        /// Computes the optimization objective.
        /// </summary>
        /// <returns>The optimization objective.</returns>
        public Zen<Real> MaximizationObjective()
        {
            return this.TotalDemandMetVariable;
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public Zen<bool> Constraints()
        {
            var constraints = new List<Zen<bool>>();

            this.EnsureTotalDemandMetIsSum(constraints);
            this.EnsureDemandsAreBoundedByMaximumCapacity(constraints);
            this.EnsureAllFlowVariablesAreNonNegativeAndLessThanDemand(constraints);
            this.EnsureAllFlowPathVariablesAreNonNegative(constraints);
            this.EnsureUnconnectedNodesHaveNoFlowOrDemand(constraints);
            this.EnsureFlowMetPerNodePairIsTheSumOverAllPaths(constraints);
            this.EnsureSumOverPathsIsBoundedByCapacity(constraints);

            // constraints special to this heuristic
            this.EnsureOnlyOneOfHeuristicOrFlowIsNonZero(constraints);
            this.EnsureHeuristicUsedOnlyIfDemandIsBelowThreshold(constraints);
            this.EnsureHeuristicIsOnlyAllocatedToShortestPaths(constraints);

            if (this.EnsureLocalOptimum)
            {
                this.EnsureNoSpareCapacityGoesUnused(constraints);
            }

            return Zen.And(constraints.ToArray());
        }

        /// <summary>
        /// Ensure the total demand met is set as the sum of flow variables.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureTotalDemandMetIsSum(IList<Zen<bool>> constraints)
        {
            var totalMet = Constant<Real>(0);
            foreach (var pair in this.Topology.GetNodePairs())
            {
                totalMet = totalMet + this.FlowVariables[pair] + this.HeuristicVariables[pair];
            }

            constraints.Add(this.TotalDemandMetVariable == totalMet);
        }

        /// <summary>
        /// This is used to ensure demands are finite.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureDemandsAreBoundedByMaximumCapacity(IList<Zen<bool>> constraints)
        {
            foreach (var (_, variable) in this.DemandVariables)
            {
                constraints.Add(variable <= (Real)(this.Topology.MaximiumCapacity() * this.Topology.Graph.Vertices.Count()));
            }
        }

        /// <summary>
        /// Ensure that f_k >= 0 and no more than d_k.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureAllFlowVariablesAreNonNegativeAndLessThanDemand(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, variable) in this.FlowVariables)
            {
                constraints.Add(variable >= (Real)0);
                constraints.Add(variable <= this.DemandVariables[pair]);
            }

            foreach (var (pair, variable) in this.HeuristicVariables)
            {
                constraints.Add(variable >= (Real)0);
                constraints.Add(variable <= this.DemandVariables[pair]);
            }
        }

        /// <summary>
        /// Ensure that all per-path encoding variables f_k^p >= 0.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureAllFlowPathVariablesAreNonNegative(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    constraints.Add(this.FlowPathVariables[path] >= (Real)0);
                    constraints.Add(this.HeuristicPathVariables[path] >= (Real)0);
                }
            }
        }

        /// <summary>
        /// Ensure that nodes that are not connected have no flow.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureUnconnectedNodesHaveNoFlowOrDemand(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                if (paths.Count == 0)
                {
                    constraints.Add(this.DemandVariables[pair] == (Real)0);
                    constraints.Add(this.FlowVariables[pair] == (Real)0);
                    constraints.Add(this.HeuristicVariables[pair] == (Real)0);
                }
            }
        }

        /// <summary>
        /// Ensure that the flow f_k = sum_p f_k^p.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureFlowMetPerNodePairIsTheSumOverAllPaths(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                var sumFlowByPath = Constant<Real>(0);
                var sumHeuristicByPath = Constant<Real>(0);
                foreach (var path in paths)
                {
                    sumFlowByPath = sumFlowByPath + this.FlowPathVariables[path];
                    sumHeuristicByPath = sumHeuristicByPath + this.HeuristicPathVariables[path];
                }

                constraints.Add(this.FlowVariables[pair] == sumFlowByPath);
                constraints.Add(this.HeuristicVariables[pair] == sumHeuristicByPath);
            }
        }

        /// <summary>
        /// Ensure all paths that traverse an edge, total do not exceed its capacity.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureSumOverPathsIsBoundedByCapacity(IList<Zen<bool>> constraints)
        {
            var sumPerEdge = new Dictionary<Edge, Zen<Real>>();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                sumPerEdge[edge] = Constant<Real>(0);
            }

            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.Topology.GetEdge(source, target);
                        sumPerEdge[edge] = sumPerEdge[edge] + this.FlowPathVariables[path] + this.HeuristicPathVariables[path];
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                constraints.Add(this.CapacityUsedVariables[edge] == total);
                constraints.Add(this.CapacityUsedVariables[edge] <= (Real)edge.Capacity);
            }
        }

        /// <summary>
        /// Ensure only one of h_k or f_k is non-zero.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureOnlyOneOfHeuristicOrFlowIsNonZero(IList<Zen<bool>> constraints)
        {
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var flowVariable = this.FlowVariables[pair];
                var heuristicVariable = this.HeuristicVariables[pair];
                constraints.Add(Or(flowVariable == (Real)0, heuristicVariable == (Real)0));
            }
        }

        /// <summary>
        /// Ensure that h_k is d_k if d_k is less than or equal to T otherwise h_k is 0.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureHeuristicUsedOnlyIfDemandIsBelowThreshold(IList<Zen<bool>> constraints)
        {
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var isBelowThreshold = this.DemandVariables[pair] <= (Real)this.Threshold;
                var thresholdConstraint = If(isBelowThreshold, this.HeuristicVariables[pair] == this.DemandVariables[pair], this.HeuristicVariables[pair] == (Real)0);
                constraints.Add(thresholdConstraint);
            }
        }

        /// <summary>
        /// Ensure that h_k is d_k if d_k is less than or equal to T otherwise h_k is 0.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureHeuristicIsOnlyAllocatedToShortestPaths(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                if (paths.Count == 0)
                {
                    continue;
                }

                var minPathLength = paths.Select(p => p.Count).Min();

                foreach (var path in paths)
                {
                    if (path.Count > minPathLength)
                    {
                        constraints.Add(this.HeuristicPathVariables[path] == (Real)0);
                    }
                }
            }
        }

        /// <summary>
        /// These constraints are used only to ensure we have a locally optimal solution
        /// for the cases where we are not optimizing this encoding. We do this by ensuring
        /// that if a pair of nodes (s, d) have f_k + h_k less than d_k then it must be because
        /// all paths between s and d are bottlenecked by the network capacity.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureNoSpareCapacityGoesUnused(IList<Zen<bool>> constraints)
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                if (paths.Count == 0)
                {
                    continue;
                }

                var flowVariable = this.FlowVariables[pair];
                var heuristicVariable = this.HeuristicVariables[pair];
                var maxNotAchieved = flowVariable + heuristicVariable < this.DemandVariables[pair];

                foreach (var path in paths)
                {
                    var isSpareCapacity = True();
                    for (int i = 0; i < path.Count - 1; i++)
                    {
                        var source = path[i];
                        var target = path[i + 1];
                        var edge = this.Topology.GetEdge(source, target);
                        isSpareCapacity = And(isSpareCapacity, this.CapacityUsedVariables[edge] < (Real)edge.Capacity);
                    }

                    constraints.Add(Implies(maxNotAchieved, Not(isSpareCapacity)));
                }
            }
        }

        /// <summary>
        /// Display a solution to this encoding.
        /// </summary>
        /// <param name="solution"></param>
        public void DisplaySolution(ZenSolution solution)
        {
            Console.WriteLine($"total demand met: {solution.Get(this.TotalDemandMetVariable)}");

            foreach (var (pair, variable) in this.DemandVariables)
            {
                var demand = solution.Get(variable);
                if (demand > 0)
                    Console.WriteLine($"demand for {pair} = {demand}");
            }

            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    var flow = solution.Get(this.FlowPathVariables[path]);
                    var heur = solution.Get(this.HeuristicPathVariables[path]);
                    if (flow > 0)
                        Console.WriteLine($"allocation[f] for [{string.Join(",", path)}] = {flow}");
                    if (heur > 0)
                        Console.WriteLine($"allocation[h] for [{string.Join(",", path)}] = {heur}");
                }
            }

            /* foreach (var (pair, variable) in this.FlowVariables)
            {
                Console.WriteLine($"flow for {pair} = {solution.Get(variable)}");
            }

            foreach (var (pair, variable) in this.HeuristicVariables)
            {
                Console.WriteLine($"heuristic for {pair} = {solution.Get(variable)}");
            } */
        }
    }
}
