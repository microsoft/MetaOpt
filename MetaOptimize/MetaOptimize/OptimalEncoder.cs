// <copyright file="OptimalEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using ZenLib;

    /// <summary>
    /// A class for the optimal encoding.
    /// </summary>
    public class OptimalEncoder : IEncoder
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        public int K { get; set; }

        /// <summary>
        /// The enumeration of paths between all pairs of nodes.
        /// </summary>
        public Dictionary<(string, string), string[][]> Paths { get; set; }

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
        public Dictionary<string[], Zen<Real>> FlowPathVariables { get; set; }

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
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="demandConstraints">Any concrete demand constraints.</param>
        public OptimalEncoder(Topology topology, int k, Dictionary<(string, string), Real> demandConstraints = null)
        {
            this.Topology = topology;
            this.K = k;
            this.Paths = new Dictionary<(string, string), string[][]>();
            this.DemandConstraints = demandConstraints ?? new Dictionary<(string, string), Real>();

            // establish the demand variables.
            this.DemandVariables = new Dictionary<(string, string), Zen<Real>>();
            foreach (var pair in topology.GetNodePairs())
            {
                this.DemandVariables[pair] = Zen.Symbolic<Real>("demand" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.DemandVariables[pair]);
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = Zen.Symbolic<Real>("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<string[], Zen<Real>>(new PathComparer());
            foreach (var pair in this.Topology.GetNodePairs())
            {
                // establish the flow variable.
                this.FlowVariables[pair] = Zen.Symbolic<Real>("flow_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);

                var paths = this.Topology.ShortestKPaths(this.K, pair.Item1, pair.Item2);
                this.Paths[pair] = paths;

                foreach (var simplePath in paths)
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = Zen.Symbolic<Real>("flowpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.FlowPathVariables[simplePath]);
                }
            }

            var demandVariables = new HashSet<Zen<Real>>(this.DemandVariables.Values);
            this.kktEncoder = new KktOptimizationGenerator(this.variables, demandVariables);
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
            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, this.FlowPathVariables[path])));
                }
            }

            // Ensure that nodes that are not connected have no flow or demand.
            // This is needed for not fully connected topologies.
            foreach (var (pair, paths) in this.Paths)
            {
                if (paths.Length == 0)
                {
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.DemandVariables[pair])));
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(new Term(1, this.FlowVariables[pair])));
                }
            }

            // Ensure that the flow f_k = sum_p f_k^p.
            foreach (var (pair, paths) in this.Paths)
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

            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    for (int i = 0; i < path.Length - 1; i++)
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
                total.Terms.Add(new Term(-1 * edge.CapacityReal));
                this.kktEncoder.AddLeqZeroConstraint(total);
            }

            // Optimization objective is the total demand met.
            // Return the encoding, including the feasibility constraints, objective, and KKT conditions.
            var maxObjective = new Polynomial(new Term(1, this.TotalDemandMetVariable));
            return new OptimizationEncoding
            {
                FeasibilityConstraints = this.kktEncoder.Constraints(),
                OptimalConstraints = this.kktEncoder.MaximizationConstraints(maxObjective),
                MaximizationObjective = this.TotalDemandMetVariable,
                DemandExpressions = this.DemandVariables,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver solution.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(ZenSolution solution)
        {
            var demands = new Dictionary<(string, string), Real>();
            var flows = new Dictionary<(string, string), Real>();
            var flowPaths = new Dictionary<string[], Real>(new PathComparer());

            foreach (var (pair, variable) in this.DemandVariables)
            {
                demands[pair] = solution.Get(variable);
            }

            foreach (var (pair, variable) in this.FlowVariables)
            {
                flows[pair] = solution.Get(variable);
            }

            foreach (var (path, variable) in this.FlowPathVariables)
            {
                flowPaths[path] = solution.Get(variable);
            }

            return new OptimizationSolution
            {
                TotalDemandMet = solution.Get(this.TotalDemandMetVariable),
                Demands = demands,
                Flows = flows,
                FlowsPaths = flowPaths,
            };
        }
    }
}
