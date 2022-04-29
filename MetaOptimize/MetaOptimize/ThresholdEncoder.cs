// <copyright file="ThresholdEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static ZenLib.Zen;

    /// <summary>
    /// A class for the threshold heuristic encoding.
    /// </summary>
    public class ThresholdEncoder : INetworkEncoder
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
        /// The heuristic variables for the network (h_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> HeuristicVariables { get; set; }

        /// <summary>
        /// The alpha variables for the network (a_k).
        /// </summary>
        public Dictionary<(string, string), Zen<Real>> AlphaVariables { get; set; }

        /// <summary>
        /// The heuristic variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<IList<string>, Zen<Real>> HeuristicPathVariables { get; set; }

        /// <summary>
        /// The threshold for the heuristic.
        /// </summary>
        public Real Threshold { get; set; }

        /// <summary>
        /// The total demand met variable.
        /// </summary>
        public Zen<Real> TotalDemandMetVariable { get; set; }

        /// <summary>
        /// The set of variables used in the encoding.
        /// </summary>
        private ISet<Zen<Real>> variables = new HashSet<Zen<Real>>();

        /// <summary>
        /// The kkt encoder used to construct the encoding.
        /// </summary>
        private KktOptimizationEncoder kktEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="ThresholdEncoder"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="demandVariables">The shared demand variables.</param>
        /// <param name="threshold">The heuristic threshold.</param>
        public ThresholdEncoder(Topology topology, Dictionary<(string, string), Zen<Real>> demandVariables, Real threshold)
        {
            this.Topology = topology;
            this.SimplePaths = new Dictionary<(string, string), IList<IList<string>>>();
            this.Threshold = threshold;

            // establish the demand variables.
            this.DemandVariables = demandVariables;
            foreach (var demandVar in this.DemandVariables)
            {
                this.variables.Add(demandVar.Value);
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = Symbolic<Real>("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            this.HeuristicVariables = new Dictionary<(string, string), Zen<Real>>();
            this.HeuristicPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            this.AlphaVariables = new Dictionary<(string, string), Zen<Real>>();

            foreach (var pair in this.Topology.GetNodePairs())
            {
                var simplePaths = this.Topology.SimplePaths(pair.Item1, pair.Item2).ToList();

                // establish the flow variable.
                this.FlowVariables[pair] = Symbolic<Real>("flow_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);

                // establish the heuristic variable.
                this.HeuristicVariables[pair] = Symbolic<Real>("heuristic_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.HeuristicVariables[pair]);

                // establish the alpha variable.
                this.AlphaVariables[pair] = Symbolic<Real>("alpha_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.AlphaVariables[pair]);

                this.SimplePaths[pair] = simplePaths;

                foreach (var simplePath in simplePaths)
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = Symbolic<Real>("flowpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.FlowPathVariables[simplePath]);

                    // establish the heuristic path variables.
                    this.HeuristicPathVariables[simplePath] = Symbolic<Real>("heuristicpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.HeuristicPathVariables[simplePath]);
                }
            }

            this.kktEncoder = new KktOptimizationEncoder(this.variables);
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding Encoding()
        {
            this.EnsureTotalDemandMetIsSum();
            this.EnsureDemandsAreBoundedByMaximumCapacity();
            this.EnsureAllFlowVariablesAreNonNegativeAndLessThanDemand();
            this.EnsureAllFlowPathVariablesAreNonNegative();
            this.EnsureUnconnectedNodesHaveNoFlowOrDemand();
            this.EnsureFlowMetPerNodePairIsTheSumOverAllPaths();
            this.EnsureHeuristicMetPerNodePairIsTheSumOverAllPaths();
            this.EnsureSumOverPathsIsBoundedByCapacity();

            // new constraints
            this.EnsureAllHeuristicAndAlphaVariablesRelated();

            var minObjective = new Polynomial(new Term(-1, this.TotalDemandMetVariable));
            return new OptimizationEncoding
            {
                FeasibilityConstraints = this.kktEncoder.Constraints(),
                OptimalConstraints = this.kktEncoder.MinimizationConstraints(minObjective),
                MaximizationObjective = this.TotalDemandMetVariable,
            };
        }

        /// <summary>
        /// Ensure the total demand met is set as the sum of flow variables.
        /// </summary>
        internal void EnsureTotalDemandMetIsSum()
        {
            var polynomial = new Polynomial();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                polynomial.Terms.Add(new Term(1, this.FlowVariables[pair]));
                polynomial.Terms.Add(new Term(1, this.HeuristicVariables[pair]));
            }

            polynomial.Terms.Add(new Term(-1, this.TotalDemandMetVariable));
            this.kktEncoder.AddEqZeroConstraint(polynomial);
        }

        /// <summary>
        /// This is used to ensure demands are finite.
        /// </summary>
        internal void EnsureDemandsAreBoundedByMaximumCapacity()
        {
            var maxDemand = this.Topology.MaximiumCapacity() * this.Topology.Graph.Vertices.Count();
            foreach (var (_, variable) in this.DemandVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, variable), new Term(-1 * maxDemand)));
            }
        }

        /// <summary>
        /// Ensure that f_k >= 0 and no more than d_k.
        /// </summary>
        internal void EnsureAllFlowVariablesAreNonNegativeAndLessThanDemand()
        {
            foreach (var (pair, variable) in this.FlowVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, variable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, variable), new Term(-1, this.DemandVariables[pair])));
            }
        }

        /// <summary>
        /// Ensure that a_k geq 0 and leq 1.
        /// Ensure that h_k geq 0.
        /// Ensure that h_k leq d_k.
        /// Ensure that h_k leq T.
        /// Ensure that h_k leq a_k * M.
        /// Ensure that h_k geq d_k - (1 - a_k) * M.
        /// Ensure that f_k leq (1 - a_k) * M.
        /// Ensure that (d_k - T) leq (1 - a_k) * M.
        /// Ensure that (d_k - T) geq a_k * M.
        /// </summary>
        internal void EnsureAllHeuristicAndAlphaVariablesRelated()
        {
            var maxDemand = this.Topology.MaximiumCapacity() * this.Topology.Graph.Vertices.Count();
            foreach (var (pair, heuristicVariable) in this.HeuristicVariables)
            {
                var alphaVariable = this.AlphaVariables[pair];
                var demandVariable = this.DemandVariables[pair];
                var flowVariable = this.FlowVariables[pair];

                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, alphaVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, alphaVariable), new Term(-1)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, heuristicVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, heuristicVariable), new Term(-1, demandVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, heuristicVariable), new Term(-1 * this.Threshold)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, heuristicVariable), new Term(-maxDemand, alphaVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, demandVariable), new Term(-maxDemand), new Term(maxDemand, alphaVariable), new Term(-1, heuristicVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, flowVariable), new Term(-maxDemand), new Term(maxDemand, alphaVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, demandVariable), new Term(-1 * this.Threshold), new Term(-maxDemand), new Term(maxDemand, alphaVariable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-maxDemand, alphaVariable), new Term(-1, demandVariable), new Term(this.Threshold)));
            }
        }

        /// <summary>
        /// Ensure that all per-path encoding variables f_k^p >= 0.
        /// </summary>
        internal void EnsureAllFlowPathVariablesAreNonNegative()
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, this.FlowPathVariables[path])));
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, this.HeuristicPathVariables[path])));
                }
            }
        }

        /// <summary>
        /// Ensure that nodes that are not connected have no flow.
        /// </summary>
        internal void EnsureUnconnectedNodesHaveNoFlowOrDemand()
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                if (paths.Count == 0)
                {
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.DemandVariables[pair])));
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.FlowVariables[pair])));
                }
            }
        }

        /// <summary>
        /// Ensure that the flow f_k = sum_p f_k^p.
        /// </summary>
        internal void EnsureFlowMetPerNodePairIsTheSumOverAllPaths()
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                var polynomial = new Polynomial(new Term(0));
                foreach (var path in paths)
                {
                    polynomial.Terms.Add(new Term(1, this.FlowPathVariables[path]));
                }

                polynomial.Terms.Add(new Term(-1, this.FlowVariables[pair]));
                this.kktEncoder.AddEqZeroConstraint(polynomial);
            }
        }

        /// <summary>
        /// Ensure that the h_k = sum_p h_k^p.
        /// </summary>
        internal void EnsureHeuristicMetPerNodePairIsTheSumOverAllPaths()
        {
            foreach (var (pair, paths) in this.SimplePaths)
            {
                var polynomial = new Polynomial(new Term(0));
                foreach (var path in paths)
                {
                    polynomial.Terms.Add(new Term(1, this.HeuristicPathVariables[path]));
                }

                polynomial.Terms.Add(new Term(-1, this.HeuristicVariables[pair]));
                this.kktEncoder.AddEqZeroConstraint(polynomial);
            }
        }

        /// <summary>
        /// Ensure all paths that traverse an edge, total do not exceed its capacity.
        /// </summary>
        internal void EnsureSumOverPathsIsBoundedByCapacity()
        {
            var sumPerEdge = new Dictionary<Edge, Polynomial>();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                sumPerEdge[edge] = new Polynomial(new Term(0));
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
                        sumPerEdge[edge].Terms.Add(new Term(1, this.FlowPathVariables[path]));
                        sumPerEdge[edge].Terms.Add(new Term(1, this.HeuristicPathVariables[path]));
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                total.Terms.Add(new Term(-edge.Capacity));
                this.kktEncoder.AddLeqZeroConstraint(total);
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
                Console.WriteLine($"demand for {pair} = {solution.Get(variable)}");
            }

            foreach (var (pair, variable) in this.FlowVariables)
            {
                Console.WriteLine($"flow for {pair} = {solution.Get(variable)}");
            }

            foreach (var (pair, variable) in this.HeuristicVariables)
            {
                Console.WriteLine($"heuristic for {pair} = {solution.Get(variable)}");
            }

            foreach (var (pair, variable) in this.AlphaVariables)
            {
                Console.WriteLine($"alpha for {pair} = {solution.Get(variable)}");
            }

            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    var flow = solution.Get(this.FlowPathVariables[path]);
                    var heur = solution.Get(this.HeuristicPathVariables[path]);
                    Console.WriteLine($"allocation[f] for [{string.Join(",", path)}] = {flow}");
                    Console.WriteLine($"allocation[h] for [{string.Join(",", path)}] = {heur}");
                }
            }
        }
    }
}
