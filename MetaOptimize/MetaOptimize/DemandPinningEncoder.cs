using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ZenLib;
    /// <summary>
    /// Encodes demand pinning solution.
    /// </summary>
    public class DemandPinningEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
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
        public int K { get; set; }

        /// <summary>
        /// The threshold for the demand pinning problem.
        /// </summary>
        public double Threshold { get; set; }

        /// <summary>
        /// The enumeration of paths between all pairs of nodes.
        /// </summary>
        public Dictionary<(string, string), string[][]> Paths { get; set; }

        /// <summary>
        /// Absolute shortest paths.
        /// </summary>
        public Dictionary<(string, string), string[][]> absShortestPaths { get; set; }

        /// <summary>
        /// The demand constraints in terms of constant values.
        /// </summary>
        public Dictionary<(string, string), double> DemandConstraints { get; set; }

        /// <summary>
        /// The demand variables for the network (d_k).
        /// </summary>
        public Dictionary<(string, string), TVar> DemandVariables { get; set; }

        /// <summary>
        /// The flow variables for the network (f_k).
        /// </summary>
        public Dictionary<(string, string), TVar> FlowVariables { get; set; }

        /// <summary>
        /// Auxilary variable used to encode maximum.
        /// </summary>
        public Dictionary<(string, string), TVar> MaxAuxVariables { get; set; }

        // /// <summary>
        // /// max for pinned flow.
        // /// </summary>
        // public Dictionary<(string, string), TVar> maxPinned { get; set; }

        private double _bigM = Math.Pow(10, 6);

        /// <summary>
        /// The flow variables for a given path in the network (f_k^p).
        /// </summary>
        public Dictionary<string[], TVar> FlowPathVariables { get; set; }

        /// <summary>
        /// The total demand met variable.
        /// </summary>
        public TVar TotalDemandMetVariable { get; set; }

        // /// <summary>
        // /// Those flows that are pinned.
        // /// </summary>
        // public Dictionary<(string, string), TVar> pinnedFlowVariables { get; set; }

        /// <summary>
        /// Sum non shortest path flows.
        /// </summary>
        public Dictionary<(string, string), Polynomial<TVar>> sumNonShortest { get; set; }

        /// <summary>
        /// Shortest path flow.
        /// </summary>
        public Dictionary<(string, string), TVar> shortestFlowVariables { get; set; }
        /// <summary>
        /// The set of variables used in the encoding.
        /// </summary>
        private ISet<TVar> variables;

        /// <summary>
        /// The kkt encoder used to construct the encoding.
        /// </summary>
        private KktOptimizationGenerator<TVar, TSolution> kktEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="OptimalEncoder{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="solver">The solver.</param>
        /// <param name="topology">The network topology.</param>
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="demandConstraints">Any concrete demand constraints.</param>
        /// <param name="threshold"> The threshold to use for demand pinning.</param>
        public DemandPinningEncoder(ISolver<TVar, TSolution> solver, Topology topology, int k, double threshold = 0, Dictionary<(string, string), double> demandConstraints = null)
        {
            this.Solver = solver;
            this.Topology = topology;
            this.K = k;
            this.variables = new HashSet<TVar>();
            this.Paths = new Dictionary<(string, string), string[][]>();
            // this.absShortestPaths = new Dictionary<(string, string), string[][]>();
            this.Threshold = threshold != 0 ? threshold : this.Topology.TotalCapacity();
            this.DemandConstraints = demandConstraints ?? new Dictionary<(string, string), double>();

            // establish the demand variables.
            this.DemandVariables = new Dictionary<(string, string), TVar>();
            foreach (var pair in topology.GetNodePairs())
            {
                this.DemandVariables[pair] = this.Solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.DemandVariables[pair]);
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = this.Solver.CreateVariable("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), TVar>();
            // this.maxPinned = new Dictionary<(string, string), TVar>();
            this.MaxAuxVariables = new Dictionary<(string, string), TVar>();
            this.FlowPathVariables = new Dictionary<string[], TVar>(new PathComparer());
            this.sumNonShortest = new Dictionary<(string, string), Polynomial<TVar>>();
            this.shortestFlowVariables = new Dictionary<(string, string), TVar>();
            // this.pinnedFlowVariables = new Dictionary<(string, string), TVar>();

            foreach (var pair in this.Topology.GetNodePairs())
            {
                var paths = this.Topology.ShortestKPaths(this.K, pair.Item1, pair.Item2);
                this.Paths[pair] = paths;
                if (paths.Length < 1) {
                    continue;
                }
                // establish the flow variable.
                this.FlowVariables[pair] = this.Solver.CreateVariable("flow_" + pair.Item1 + "_" + pair.Item2);
                // this.pinnedFlowVariables[pair] = this.Solver.CreateVariable("pinnedFlow_" + pair.Item1 + "_" + pair.Item2);
                // this.maxPinned[pair] = this.Solver.CreateVariable("maxPinned_" + pair.Item1 + "_" + pair.Item2);
                this.MaxAuxVariables[pair] = this.Solver.CreateVariable("maxNonPinned_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);
                // this.variables.Add(this.pinnedFlowVariables[pair]);
                // this.variables.Add(this.maxPinned[pair]);
                this.variables.Add(this.MaxAuxVariables[pair]);
                this.sumNonShortest[pair] = new Polynomial<TVar>(new Term<TVar>(0));

                var shortestPaths = this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2);

                foreach (var simplePath in paths)
                {
                    // establish the flow path variables.
                    this.FlowPathVariables[simplePath] = this.Solver.CreateVariable("flowpath_" + string.Join("_", simplePath));
                    this.variables.Add(this.FlowPathVariables[simplePath]);
                    if (shortestPaths[0].SequenceEqual(simplePath)) {
                        this.shortestFlowVariables[pair] = this.FlowPathVariables[simplePath];
                    } else {
                        this.sumNonShortest[pair].Terms.Add(new Term<TVar>(1, this.FlowPathVariables[simplePath]));
                    }
                }
                // this.absShortestPaths[pair] = shortestPaths;
            }

            var demandVariables = new HashSet<TVar>(this.DemandVariables.Values);
            this.kktEncoder = new KktOptimizationGenerator<TVar, TSolution>(this.Solver, this.variables, demandVariables);
        }

        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding<TVar, TSolution> Encoding(bool noKKT = false)
        {
            // Compute the maximum demand M.
            // Since we don't know the demands we have to be very conservative.
            var maxDemand = this.Topology.TotalCapacity() * 10;

            // Ensure that sum_k f_k = total_demand.
            // This includes both the ones that are pinned and
            // those that are not.
            var polynomial = new Polynomial<TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (this.Paths[pair].Length < 1) {
                    continue;
                }
                polynomial.Terms.Add(new Term<TVar>(1, this.FlowVariables[pair]));
            }
            // foreach (var pair in this.Topology.GetNodePairs())
            // {
            //     polynomial.Terms.Add(new Term<TVar>(1, this.pinnedFlowVariables[pair]));
            // }

            polynomial.Terms.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));
            this.kktEncoder.AddEqZeroConstraint(polynomial);

            // Ensure that the demands are finite.
            // This is needed because Z3 can return any value if demands can be infinite.
            foreach (var (_, variable) in this.DemandVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1 * maxDemand)));
            }

            // Ensure that the demand constraints are respected
            foreach (var (pair, constant) in this.DemandConstraints)
            {
                this.kktEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.DemandVariables[pair]), new Term<TVar>(-1 * constant)));
            }

            // Ensure that f_k geq 0.
            // Ensure that f_k leq d_k.
            foreach (var (pair, variable) in this.FlowVariables)
            {
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, this.DemandVariables[pair])));
            }
            // // Ensure \beta_k geq 0
            // // Ensure \beta_k \leq d_k
            // foreach (var (pair, variable) in this.pinnedFlowVariables)
            // {
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, this.DemandVariables[pair])));
            // }

            // Ensure that f_k^p geq 0.
            foreach (var (pair, paths) in this.Paths)
            {
                foreach (var path in paths)
                {
                    this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path])));
                }
            }

            // Ensure that nodes that are not connected have no flow or demand.
            // This is needed for not fully connected topologies.
            // foreach (var (pair, paths) in this.Paths)
            // {
            //     if (paths.Length == 0)
            //     {
            //         this.kktEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.DemandVariables[pair])));
            //         this.kktEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.FlowVariables[pair])));
            //         // this.kktEncoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, this.pinnedFlowVariables[pair])));
            //     }
            // }

            // Ensure that the flow f_k = sum_p f_k^p.
            foreach (var (pair, paths) in this.Paths)
            {
                if (paths.Length < 1) {
                    continue;
                }
                var poly = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var path in paths)
                {
                    poly.Terms.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                }

                poly.Terms.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.kktEncoder.AddEqZeroConstraint(poly);
            }

            // Ensure the capacity constraints hold.
            // The sum of flows over all paths through each edge are bounded by capacity.
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                sumPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
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
                        var term = new Term<TVar>(1, this.FlowPathVariables[path]);
                        sumPerEdge[edge].Terms.Add(term);
                    }
                }
                // foreach (var path in this.absShortestPaths[pair])
                // {
                //     for (int i = 0; i < path.Length - 1; i++)
                //     {
                //         var source = path[i];
                //         var target = path[i + 1];
                //         var edge = this.Topology.GetEdge(source, target);
                //         var term = new Term<TVar>(1, this.pinnedFlowVariables[pair]);
                //         sumPerEdge[edge].Terms.Add(term);
                //     }
                // }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                total.Terms.Add(new Term<TVar>(-1 * edge.Capacity));
                this.kktEncoder.AddLeqZeroConstraint(total);
            }

            // generating the max constraints that achieve pinning.
            foreach (var (pair, polyTerm) in sumNonShortest) {
                // sum non shortest flows \leq MaxAuxVariables
                polyTerm.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.kktEncoder.AddLeqZeroConstraint(polyTerm);
                // MaxAuxVariables \geq 0
                this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, MaxAuxVariables[pair])));
                // maxNontPinned \geq M(d_k - T_d)
                var maxNonPinnedLB = new Polynomial<TVar>(new Term<TVar>(this._bigM, this.DemandVariables[pair]));
                maxNonPinnedLB.Terms.Add(new Term<TVar>(-1 * this._bigM * Threshold));
                maxNonPinnedLB.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.kktEncoder.AddLeqZeroConstraint(maxNonPinnedLB);

                // shortest path flows \leq MaxAuxVariables
                var shortestPathUB = new Polynomial<TVar>(new Term<TVar>(1, DemandVariables[pair]));
                shortestPathUB.Terms.Add(new Term<TVar>(-1, shortestFlowVariables[pair]));
                shortestPathUB.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.kktEncoder.AddLeqZeroConstraint(shortestPathUB);
            }
            // foreach (var (pair, variable) in MaxAuxVariables)
            // {
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
            //     var poly = new Polynomial<TVar>(new Term<TVar>(0));
            //     poly.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
            //     poly.Terms.Add(new Term<TVar>(this._bigM, this.DemandVariables[pair]));
            //     poly.Terms.Add(new Term<TVar>(-1 * this._bigM * this.Threshold));
            //     this.kktEncoder.AddLeqZeroConstraint(poly);
            //     // need to ensure f_k <= maxnonpinned
            //     var poly2 = new Polynomial<TVar>(new Term<TVar>(1, this.FlowVariables[pair]));
            //     poly2.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
            //     this.kktEncoder.AddLeqZeroConstraint(poly2);
            // }
            // foreach (var (pair, variable) in maxPinned)
            // {
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
            //     var poly = new Polynomial<TVar>(new Term<TVar>(0));
            //     poly.Terms.Add(new Term<TVar>(-1, maxPinned[pair]));
            //     poly.Terms.Add(new Term<TVar>(this._bigM * this.Threshold));
            //     poly.Terms.Add(new Term<TVar>(-1 * this._bigM, this.DemandVariables[pair]));
            //     this.kktEncoder.AddLeqZeroConstraint(poly);
            //     // need to ensure \beta_k \le maxpinned.
            //     var poly2 = new Polynomial<TVar>(new Term<TVar>(1, this.pinnedFlowVariables[pair]));
            //     poly2.Terms.Add(new Term<TVar>(-1, maxPinned[pair]));
            //     this.kktEncoder.AddLeqZeroConstraint(poly2);
            // }

            var objectiveFunction = new Polynomial<TVar>(new Term<TVar>(1, this.TotalDemandMetVariable));
            double alpha = maxDemand;
            foreach (var (pair, maxVar) in MaxAuxVariables) {
                objectiveFunction.Terms.Add(new Term<TVar>(-1 * alpha, maxVar));
            }

            // Generate the full constraints.
            this.kktEncoder.AddMaximizationConstraints(objectiveFunction, noKKT);

            // Optimization objective is the total demand met.
            // Return the encoding, including the feasibility constraints, objective, and KKT conditions.
            return new OptimizationEncoding<TVar, TSolution>
            {
                MaximizationObjective = this.TotalDemandMetVariable,
                DemandVariables = this.DemandVariables,
            };
        }
        /// <summary>
        /// placeholder for getsolution.
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var demands = new Dictionary<(string, string), double>();
            var flows = new Dictionary<(string, string), double>();
            var flowPaths = new Dictionary<string[], double>(new PathComparer());

            foreach (var (pair, variable) in this.DemandVariables)
            {
                demands[pair] = this.Solver.GetVariable(solution, variable);
            }

            foreach (var (pair, variable) in this.FlowVariables)
            {
                flows[pair] = this.Solver.GetVariable(solution, variable);
            }

            foreach (var (path, variable) in this.FlowPathVariables)
            {
                flowPaths[path] = this.Solver.GetVariable(solution, variable);
            }

            return new OptimizationSolution
            {
                TotalDemandMet = this.Solver.GetVariable(solution, this.TotalDemandMetVariable),
                Demands = demands,
                Flows = flows,
                FlowsPaths = flowPaths,
            };
        }
    }
}
