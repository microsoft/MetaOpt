// <copyright file="OptimalEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;

    /// <summary>
    /// A class for the optimal encoding.
    /// </summary>
    public class TEMaxFlowOptimalEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The solver being used.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        public int MaxNumPaths { get; set; }

        /// <summary>
        /// The enumeration of paths between all pairs of nodes.
        /// </summary>
        public Dictionary<(string, string), string[][]> Paths { get; set; }

        /// <summary>
        /// The demand constraints in terms of constant values.
        /// </summary>
        public Dictionary<(string, string), double> DemandConstraints { get; set; }

        /// <summary>
        /// The demand variables for the network (d_k).
        /// </summary>
        public Dictionary<(string, string), Polynomial<TVar>> DemandVariables { get; set; }

        /// <summary>
        /// The flow variables for the network (f_k).
        /// </summary>
        public Dictionary<(string, string), TVar> FlowVariables { get; set; }

        /// <summary>
        /// The flow variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<string[], TVar> FlowPathVariables { get; set; }

        /// <summary>
        /// The total demand met variable.
        /// </summary>
        public TVar TotalDemandMetVariable { get; set; }

        /// <summary>
        /// The set of variables used in the encoding.
        /// </summary>
        private ISet<TVar> variables;

        /// <summary>
        /// The kkt encoder used to construct the encoding.
        /// </summary>
        private KKTRewriteGenerator<TVar, TSolution> innerProblemEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="TEMaxFlowOptimalEncoder{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="solver">The solver.</param>
        /// <param name="maxNumPaths">The max number of paths between nodes.</param>
        public TEMaxFlowOptimalEncoder(ISolver<TVar, TSolution> solver, int maxNumPaths)
        {
            this.Solver = solver;
            this.MaxNumPaths = maxNumPaths;
        }

        private bool IsDemandValid((string, string) pair)
        {
            if (this.DemandConstraints.ContainsKey(pair))
            {
                if (this.DemandConstraints[pair] <= 0)
                {
                    return false;
                }
            }
            return true;
        }

        // TODO: this needs a comment that describes what it does and what each input is.
        private void InitializeVariables(Dictionary<(string, string), Polynomial<TVar>> preDemandVariables,
                Dictionary<(string, string), double> demandEqualityConstraints, InnerRewriteMethodChoice encodingMethod,
                PathType pathType, Dictionary<(string, string), string[][]> selectedPaths,
                int numProcesses, bool verbose)
        {
            this.variables = new HashSet<TVar>();
            // establish the demand variables.
            this.DemandConstraints = demandEqualityConstraints ?? new Dictionary<(string, string), double>();
            this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
            var demandVariables = new HashSet<TVar>();

            if (preDemandVariables == null)
            {
                this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
                foreach (var pair in this.Topology.GetNodePairs())
                {
                    if (!IsDemandValid(pair))
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
                    if (!IsDemandValid(pair))
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

            // establish the total demand met variable.
            this.TotalDemandMetVariable = this.Solver.CreateVariable("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), TVar>();
            this.FlowPathVariables = new Dictionary<string[], TVar>(new PathComparer());

            // compute the paths
            this.Paths = this.Topology.ComputePaths(pathType, selectedPaths, this.MaxNumPaths, numProcesses, verbose);

            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                // establish the flow variable.
                this.FlowVariables[pair] = this.Solver.CreateVariable("flow_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);

                foreach (var simplePath in this.Paths[pair])
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("flowpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.FlowPathVariables[simplePath]);
                }
            }

            switch (encodingMethod)
            {
                case InnerRewriteMethodChoice.KKT:
                    this.innerProblemEncoder = new KKTRewriteGenerator<TVar, TSolution>(this.Solver, this.variables, demandVariables);
                    break;
                case InnerRewriteMethodChoice.PrimalDual:
                    this.innerProblemEncoder = new PrimalDualRewriteGenerator<TVar, TSolution>(this.Solver,
                                                                                               this.variables,
                                                                                               demandVariables,
                                                                                               numProcesses);
                    break;
                default:
                    throw new Exception("invalid method for encoding the inner problem");
            }
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        /// TODO: need a better comment that describes the function and what each input variable is.
        public OptimizationEncoding<TVar, TSolution> Encoding(Topology topology, Dictionary<(string, string), Polynomial<TVar>> preDemandVariables = null,
            Dictionary<(string, string), double> demandEqualityConstraints = null, bool noAdditionalConstraints = false,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            PathType pathType = PathType.KSP, Dictionary<(string, string), string[][]> selectedPaths = null,
            int numProcesses = -1, bool verbose = false)
        {
            // Initialize Variables for the encoding
            Utils.logger("initializing variables", verbose);
            this.Topology = topology;
            InitializeVariables(preDemandVariables, demandEqualityConstraints,
                innerEncoding, pathType, selectedPaths, numProcesses, verbose);
            // Compute the maximum demand M.
            // Since we don't know the demands we have to be very conservative.
            // var maxDemand = this.Topology.TotalCapacity() * 10;
            // var maxDemand = this.Topology.MaxCapacity() * this.K * 2;

            // Ensure that sum_k f_k = total_demand.
            Utils.logger("ensuring sum_k f_k = total demand", verbose);
            var total_flow_equality = new Polynomial<TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                total_flow_equality.Add(new Term<TVar>(1, this.FlowVariables[pair]));
            }

            total_flow_equality.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));

            // TODO: we seem to re-use the kkt encoder when we don't want to do any rewrite (there is a condition in the KKT rewrite block that checks if
            // we want to do a rewrite or not i think). We should probably seperate that into its on instance of the rewrite interface and initiate the inner problem
            // encoder depending on whether we have an aligned follower or not.
            // TODO: when we want to re-factor this we should first write a test case, then create a deprecated instance of this file and check they produce the same answer.
            this.innerProblemEncoder.AddEqZeroConstraint(total_flow_equality);

            // TODO: will commenting this out cause problems if/when someone wants to use Z3 for their solver? if yes, we should add it back properly.
            // Ensure that the demands are finite.
            // This is needed because Z3 can return any value if demands can be infinite.
            // foreach (var (_, variable) in this.DemandVariables)
            // {
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1 * maxDemand)));
            // }

            // Ensure that the demand constraints are respected
            Utils.logger("ensuring demand constraints are respected", verbose);
            foreach (var (pair, constant) in this.DemandConstraints)
            {
                if (constant <= 0)
                {
                    continue;
                }
                var demandConstraint = this.DemandVariables[pair].Copy();
                demandConstraint.Add(new Term<TVar>(-1 * constant));
                this.innerProblemEncoder.AddEqZeroConstraint(demandConstraint);
            }

            // Ensure that f_k geq 0.
            // Ensure that f_k leq d_k.
            Utils.logger("ensuring flows are within a correct range.", verbose);
            foreach (var (pair, variable) in this.FlowVariables)
            {
                // this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
                var flowSizeConstraints = this.DemandVariables[pair].Negate();
                flowSizeConstraints.Add(new Term<TVar>(1, variable));
                this.innerProblemEncoder.AddLeqZeroConstraint(flowSizeConstraints);
            }

            // Ensure that f_k^p geq 0.
            Utils.logger("ensuring sum_k f_k^p geq 0", verbose);
            foreach (var (pair, paths) in this.Paths)
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

            // Ensure that nodes that are not connected have no flow or demand.
            // This is needed for not fully connected topologies.
            Utils.logger("ensuring disconnected nodes do not have any flow", verbose);
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                if (paths.Length == 0)
                {
                    this.innerProblemEncoder.AddEqZeroConstraint(this.DemandVariables[pair].Copy());
                    this.innerProblemEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.FlowVariables[pair])));
                }
            }

            // Ensure that the flow f_k = sum_p f_k^p.
            Utils.logger("ensuring f_k = sum_p f_k^p", verbose);
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                var computeFlow = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var path in paths)
                {
                    computeFlow.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                }

                computeFlow.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.innerProblemEncoder.AddEqZeroConstraint(computeFlow);
            }

            // Ensure the capacity constraints hold.
            // The sum of flows over all paths through each edge are bounded by capacity.
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            // foreach (var edge in this.Topology.GetAllEdges())
            // {
            //     sumPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
            // }

            Utils.logger("ensuring capacity constraints", verbose);
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
                this.innerProblemEncoder.AddLeqZeroConstraint(total);
            }

            Utils.logger("generating full constraints", verbose);
            // Generate the full constraints.
            var objective = new Polynomial<TVar>(new Term<TVar>(1, this.TotalDemandMetVariable));
            Utils.logger("calling inner encoder", verbose);
            this.innerProblemEncoder.AddMaximizationConstraints(objective, noAdditionalConstraints, verbose);

            // Optimization objective is the total demand met.
            // Return the encoding, including the feasibility constraints, objective, and KKT conditions.
            return new TEOptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.TotalDemandMetVariable,
                MaximizationObjective = objective,
                DemandVariables = this.DemandVariables,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver solution.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var demands = new Dictionary<(string, string), double>();
            var flows = new Dictionary<(string, string), double>();
            var flowPaths = new Dictionary<string[], double>(new PathComparer());

            foreach (var (pair, poly) in this.DemandVariables)
            {
                demands[pair] = 0;
                foreach (var term in poly.GetTerms())
                {
                    demands[pair] += this.Solver.GetVariable(solution, term.Variable.Value) * term.Coefficient;
                }
            }

            foreach (var (pair, variable) in this.FlowVariables)
            {
                flows[pair] = this.Solver.GetVariable(solution, variable);
            }

            foreach (var (path, variable) in this.FlowPathVariables)
            {
                flowPaths[path] = this.Solver.GetVariable(solution, variable);
            }

            return new TEMaxFlowOptimizationSolution
            {
                MaxObjective = this.Solver.GetVariable(solution, this.TotalDemandMetVariable),
                Demands = demands,
                Flows = flows,
                FlowsPaths = flowPaths,
            };
        }
    }
}
