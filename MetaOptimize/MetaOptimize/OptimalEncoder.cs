// <copyright file="OptimalEncoder.cs" company="Microsoft">
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
    public class OptimalEncoder : INetworkEncoder
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
        /// The demand constraints in terms of constant values.
        /// </summary>
        public Dictionary<(string, string), Real> DemandConstraints { get; set; }

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
        /// The set of variables used in the encoding.
        /// </summary>
        private ISet<Zen<Real>> variables = new HashSet<Zen<Real>>();

        /// <summary>
        /// The kkt encoder used to construct the encoding.
        /// </summary>
        private KktOptimizationGenerator kktEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="OptimalEncoder"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="demandConstraints">Any concrete demand constraints.</param>
        public OptimalEncoder(Topology topology, Dictionary<(string, string), Real> demandConstraints = null)
        {
            this.Topology = topology;
            this.SimplePaths = new Dictionary<(string, string), IList<IList<string>>>();
            this.DemandConstraints = demandConstraints ?? new Dictionary<(string, string), Real>();

            // establish the demand variables.
            this.DemandVariables = new Dictionary<(string, string), Zen<Real>>();
            foreach (var pair in topology.GetNodePairs())
            {
                this.DemandVariables[pair] = Zen.Symbolic<Real>("demand" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.DemandVariables[pair]);
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = Symbolic<Real>("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var simplePaths = this.Topology.SimplePaths(pair.Item1, pair.Item2).ToList();

                // establish the flow variable.
                this.FlowVariables[pair] = Symbolic<Real>("flow_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);

                this.SimplePaths[pair] = simplePaths;

                foreach (var simplePath in simplePaths)
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = Symbolic<Real>("flowpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.FlowPathVariables[simplePath]);
                }
            }

            this.kktEncoder = new KktOptimizationGenerator(this.variables);
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding Encoding()
        {
            // Compute the maximum demand M.
            // Since we don't know the demands we have to be very conservative.
            var maxDemand = this.Topology.TotalCapacity();

            // Ensure that sum_k f_k = total_demand.
            var polynomial = new Polynomial();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                polynomial.Terms.Add(new Term(1, this.FlowVariables[pair]));
            }

            polynomial.Terms.Add(new Term(-1, this.TotalDemandMetVariable));
            this.kktEncoder.AddEqZeroConstraint(polynomial);

            // Ensure that the demands are finite.
            // This is needed because Z3 can return any value if demands can be infinite.
            foreach (var (_, variable) in this.DemandVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, variable), new Term(-1 * maxDemand)));
            }

            // Ensure that the demand constraints are respected
            foreach (var (pair, constant) in this.DemandConstraints)
            {
                this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.DemandVariables[pair]), new Term(-1 * constant)));
            }

            // Ensure that f_k geq 0.
            // Ensure that f_k leq d_k.
            foreach (var (pair, variable) in this.FlowVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, variable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(1, variable), new Term(-1, this.DemandVariables[pair])));
            }

            // Ensure that f_k^p geq 0.
            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, this.FlowPathVariables[path])));
                }
            }

            // Ensure that nodes that are not connected have no flow or demand.
            // This is needed for not fully connected topologies.
            foreach (var (pair, paths) in this.SimplePaths)
            {
                if (paths.Count == 0)
                {
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.DemandVariables[pair])));
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.FlowVariables[pair])));
                }
            }

            // Ensure that the flow f_k = sum_p f_k^p.
            foreach (var (pair, paths) in this.SimplePaths)
            {
                var poly = new Polynomial(new Term(0));
                foreach (var path in paths)
                {
                    poly.Terms.Add(new Term(1, this.FlowPathVariables[path]));
                }

                poly.Terms.Add(new Term(-1, this.FlowVariables[pair]));
                this.kktEncoder.AddEqZeroConstraint(poly);
            }

            // Ensure the capacity constraints hold.
            // The sum of flows over all paths through each edge are bounded by capacity.
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
                        var term = new Term(1, this.FlowPathVariables[path]);
                        sumPerEdge[edge].Terms.Add(term);
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                total.Terms.Add(new Term(-1 * edge.Capacity));
                this.kktEncoder.AddLeqZeroConstraint(total);
            }

            // Optimization objective is the total demand met.
            // Return the encoding, including the feasibility constraints, objective, and KKT conditions.
            var minObjective = new Polynomial(new Term(-1, this.TotalDemandMetVariable));
            return new OptimizationEncoding
            {
                FeasibilityConstraints = this.kktEncoder.Constraints(),
                OptimalConstraints = this.kktEncoder.MinimizationConstraints(minObjective),
                MaximizationObjective = this.TotalDemandMetVariable,
            };
        }

        /// <summary>
        /// Display a solution to this encoding.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public void DisplaySolution(ZenSolution solution)
        {
            Console.WriteLine($"total demand met: {solution.Get(this.TotalDemandMetVariable)}");

            foreach (var (pair, variable) in this.DemandVariables)
            {
                var demand = solution.Get(variable);
                if (demand > 0)
                    Console.WriteLine($"demand for {pair} = {demand}");
            }

            /* foreach (var (pair, variable) in this.FlowVariables)
            {
                Console.WriteLine($"flow for {pair} = {solution.Get(variable)}");
            } */

            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    var flow = solution.Get(this.FlowPathVariables[path]);
                    if (flow > 0)
                        Console.WriteLine($"allocation for [{string.Join(",", path)}] = {flow}");
                }
            }
        }
    }
}
