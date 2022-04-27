// <copyright file="OptimalEncoding.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static ZenLib.Zen;

    /// <summary>
    /// A class for the optimal encoding.
    /// </summary>
    public class OptimalEncoding : INetworkEncoding<Zen<bool>, Zen<Real>, ZenSolution>
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
        /// The flow variables for the network (f_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> FlowVariables { get; set; }

        /// <summary>
        /// The flow variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<IList<string>, Zen<Real>> FlowPathVariables { get; set; }

        /// <summary>
        /// The total demand met variable.
        /// </summary>
        public Zen<Real> TotalDemandMetVariable { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="OptimalEncoding"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="demandVariables">The shared demand variables.</param>
        public OptimalEncoding(Topology topology, Dictionary<(string, string), Zen<Real>> demandVariables)
        {
            this.Topology = topology;
            this.SimplePaths = new Dictionary<(string, string), IList<IList<string>>>();
            this.DemandVariables = demandVariables;
            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<IList<string>, Zen<Real>>();
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
                this.SimplePaths[pair] = simplePaths;

                foreach (var simplePath in simplePaths)
                {
                    this.FlowPathVariables[simplePath] = Symbolic<Real>();
                }
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
                totalMet = totalMet + this.FlowVariables[pair];
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
                foreach (var path in paths)
                {
                    sumFlowByPath = sumFlowByPath + this.FlowPathVariables[path];
                }

                constraints.Add(this.FlowVariables[pair] == sumFlowByPath);
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
                        sumPerEdge[edge] = sumPerEdge[edge] + this.FlowPathVariables[path];
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                constraints.Add(total <= (Real)edge.Capacity);
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
                    if (flow > 0)
                       Console.WriteLine($"allocation for [{string.Join(",", path)}] = {flow}");
                }
            }

            /* foreach (var (pair, variable) in this.FlowVariables)
            {
                Console.WriteLine($"flow for {pair} = {solution.Get(variable)}");
            } */
        }
    }
}
