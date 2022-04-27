// <copyright file="OptimalEncoding2.cs" company="Microsoft">
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
    public class OptimalEncoding2
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
        /// Name to variable mapping.
        /// </summary>
        private BiDictionary<string, Zen<Real>> variableMapping;

        /// <summary>
        /// The kkt encoder used to construct the encoding.
        /// </summary>
        private KktOptimizationEncoder kktEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="OptimalEncoding2"/> class.
        /// </summary>
        /// <param name="topology">The network topology.</param>
        /// <param name="demandVariables">The shared demand variables.</param>
        public OptimalEncoding2(Topology topology, Dictionary<(string, string), Zen<Real>> demandVariables)
        {
            this.variableMapping = new BiDictionary<string, Zen<Real>>();

            this.Topology = topology;
            this.SimplePaths = new Dictionary<(string, string), IList<IList<string>>>();

            // establish the demand variables.
            this.DemandVariables = demandVariables;
            foreach (var demandVar in this.DemandVariables)
            {
                this.variableMapping.Associate("demand_" + demandVar.Key.Item1 + "_" + demandVar.Key.Item2, demandVar.Value);
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = Symbolic<Real>();
            this.variableMapping.Associate("total_demand_met", this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), Zen<Real>>();
            this.FlowPathVariables = new Dictionary<IList<string>, Zen<Real>>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                var simplePaths = this.Topology.SimplePaths(pair.Item1, pair.Item2).ToList();

                // establish the flow variable.
                this.FlowVariables[pair] = Symbolic<Real>();
                this.variableMapping.Associate("flow_" + pair.Item1 + "_" + pair.Item2, this.FlowVariables[pair]);

                this.SimplePaths[pair] = simplePaths;

                foreach (var simplePath in simplePaths)
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = Symbolic<Real>();
                    this.variableMapping.Associate("flowpath_" + string.Join("_", simplePath), this.FlowPathVariables[simplePath]);
                }
            }

            this.kktEncoder = new KktOptimizationEncoder(this.variableMapping);
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

            var objective = new Polynomial(new List<PolynomialTerm> { new PolynomialTerm(-1, this.variableMapping.GetKey(this.TotalDemandMetVariable)) });
            return this.kktEncoder.MinimizationConstraints(objective);

            // return this.kktEncoder.Constraints();
        }

        /// <summary>
        /// Ensure the total demand met is set as the sum of flow variables.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureTotalDemandMetIsSum(IList<Zen<bool>> constraints)
        {
            var terms = new List<PolynomialTerm>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(this.FlowVariables[pair])));
            }

            terms.Add(new PolynomialTerm(-1, this.variableMapping.GetKey(this.TotalDemandMetVariable)));
            this.kktEncoder.AddEqZeroConstraint(new Polynomial(terms));
        }

        /// <summary>
        /// This is used to ensure demands are finite.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureDemandsAreBoundedByMaximumCapacity(IList<Zen<bool>> constraints)
        {
            foreach (var (_, variable) in this.DemandVariables)
            {
                var terms = new List<PolynomialTerm>();
                terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(variable)));
                terms.Add(new PolynomialTerm(-1 * (this.Topology.MaximiumCapacity() * this.Topology.Graph.Vertices.Count())));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(terms));
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
                var terms = new List<PolynomialTerm>();
                terms.Add(new PolynomialTerm(-1, this.variableMapping.GetKey(variable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(terms));

                terms = new List<PolynomialTerm>();
                terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(variable)));
                terms.Add(new PolynomialTerm(-1, this.variableMapping.GetKey(this.DemandVariables[pair])));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial(terms));
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
                    var terms = new List<PolynomialTerm>();
                    terms.Add(new PolynomialTerm(-1, this.variableMapping.GetKey(this.FlowPathVariables[path])));
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial(terms));
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
                    var terms = new List<PolynomialTerm>();
                    terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(this.DemandVariables[pair])));
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(terms));

                    terms = new List<PolynomialTerm>();
                    terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(this.FlowVariables[pair])));
                    this.kktEncoder.AddEqZeroConstraint(new Polynomial(terms));
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
                var terms = new List<PolynomialTerm> { new PolynomialTerm(0) };
                foreach (var path in paths)
                {
                    terms.Add(new PolynomialTerm(1, this.variableMapping.GetKey(this.FlowPathVariables[path])));
                }

                terms.Add(new PolynomialTerm(-1, this.variableMapping.GetKey(this.FlowVariables[pair])));
                this.kktEncoder.AddEqZeroConstraint(new Polynomial(terms));
            }
        }

        /// <summary>
        /// Ensure all paths that traverse an edge, total do not exceed its capacity.
        /// </summary>
        /// <param name="constraints">The encoding constraints.</param>
        internal void EnsureSumOverPathsIsBoundedByCapacity(IList<Zen<bool>> constraints)
        {
            var sumPerEdge = new Dictionary<Edge, Polynomial>();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                sumPerEdge[edge] = new Polynomial(new List<PolynomialTerm> { new PolynomialTerm(0) });
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
                        var term = new PolynomialTerm(1, this.variableMapping.GetKey(this.FlowPathVariables[path]));
                        sumPerEdge[edge].PolynomialTerms.Add(term);
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                total.PolynomialTerms.Add(new PolynomialTerm(-edge.Capacity));
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
                var demand = solution.Get(variable);
                Console.WriteLine($"demand for {pair} = {demand}");
            }

            foreach (var (pair, paths) in this.SimplePaths)
            {
                foreach (var path in paths)
                {
                    var flow = solution.Get(this.FlowPathVariables[path]);
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
