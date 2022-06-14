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
        public Dictionary<(string, string), Polynomial<TVar>> DemandVariables { get; set; }

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
        private KktOptimizationGenerator<TVar, TSolution> innerProblemEncoder;

        /// <summary>
        /// Create a new instance of the <see cref="OptimalEncoder{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="solver">The solver.</param>
        /// <param name="topology">The network topology.</param>
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="threshold"> The threshold to use for demand pinning.</param>
        public DemandPinningEncoder(ISolver<TVar, TSolution> solver, Topology topology, int k, double threshold = 0)
        {
            this.Solver = solver;
            this.Topology = topology;
            this.K = k;
            this.Threshold = threshold != 0 ? threshold : this.Topology.TotalCapacity();
        }

        private void InitializeVariables(Dictionary<(string, string), Polynomial<TVar>> preDemandVariables, Dictionary<(string, string), double> demandConstraints,
                InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT) {
            this.variables = new HashSet<TVar>();
            this.Paths = new Dictionary<(string, string), string[][]>();
            // establish the demand variables.
            this.DemandConstraints = demandConstraints ?? new Dictionary<(string, string), double>();
            this.DemandVariables = preDemandVariables;
            var demandVariables = new HashSet<TVar>();

            if (this.DemandVariables == null) {
                this.DemandVariables = new Dictionary<(string, string), Polynomial<TVar>>();
                foreach (var pair in this.Topology.GetNodePairs())
                {
                    var variable = this.Solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2);
                    this.DemandVariables[pair] = new Polynomial<TVar>(new Term<TVar>(1, variable));
                    this.variables.Add(variable);
                    demandVariables.Add(variable);
                }
            } else {
                foreach (var (pair, variable) in this.DemandVariables) {
                    foreach (var term in variable.Terms) {
                        this.variables.Add(term.Variable.Value);
                        demandVariables.Add(term.Variable.Value);
                    }
                }
            }

            // establish the total demand met variable.
            this.TotalDemandMetVariable = this.Solver.CreateVariable("total_demand_met");
            this.variables.Add(this.TotalDemandMetVariable);

            this.FlowVariables = new Dictionary<(string, string), TVar>();
            this.MaxAuxVariables = new Dictionary<(string, string), TVar>();
            this.FlowPathVariables = new Dictionary<string[], TVar>(new PathComparer());
            this.sumNonShortest = new Dictionary<(string, string), Polynomial<TVar>>();
            this.shortestFlowVariables = new Dictionary<(string, string), TVar>();

            foreach (var pair in this.Topology.GetNodePairs())
            {
                var paths = this.Topology.ShortestKPaths(this.K, pair.Item1, pair.Item2);
                this.Paths[pair] = paths;
                if (!IsDemandValid(pair)) {
                    continue;
                }
                // establish the flow variable.
                this.FlowVariables[pair] = this.Solver.CreateVariable("flow_" + pair.Item1 + "_" + pair.Item2);
                this.MaxAuxVariables[pair] = this.Solver.CreateVariable("maxNonPinned_" + pair.Item1 + "_" + pair.Item2);
                this.variables.Add(this.FlowVariables[pair]);
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
            }

            switch (innerEncoding)
            {
                case InnerEncodingMethodChoice.KKT:
                    this.innerProblemEncoder = new KktOptimizationGenerator<TVar, TSolution>(this.Solver, this.variables, demandVariables);
                    break;
                case InnerEncodingMethodChoice.PrimalDual:
                    this.innerProblemEncoder = new PrimalDualOptimizationGenerator<TVar, TSolution>(this.Solver, this.variables, demandVariables);
                    break;
                default:
                    throw new Exception("invalid method for encoding the inner problem");
            }
        }

        private bool IsDemandValid((string, string) pair) {
            if (this.DemandConstraints.ContainsKey(pair)) {
                if (this.DemandConstraints[pair] <= 0) {
                    return false;
                }
            }
            if (this.Paths[pair].Length < 1) {
                Console.WriteLine("$$$$$$$$$$$$$$$$$ pair:" + pair);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Encode the problem.
        /// </summary>
        /// <returns>The constraints and maximization objective.</returns>
        public OptimizationEncoding<TVar, TSolution> Encoding(Dictionary<(string, string), Polynomial<TVar>> preDemandVariables = null,
            Dictionary<(string, string), double> demandConstraints = null, bool noAdditionalConstraints = false,
            InnerEncodingMethodChoice innerEncoding = InnerEncodingMethodChoice.KKT)
        {
            InitializeVariables(preDemandVariables, demandConstraints, innerEncoding);
            // Compute the maximum demand M.
            // Since we don't know the demands we have to be very conservative.
            // var maxDemand = this.Topology.TotalCapacity() * 10;

            // Ensure that sum_k f_k = total_demand.
            // This includes both the ones that are pinned and
            // those that are not.
            var polynomial = new Polynomial<TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!IsDemandValid(pair)) {
                    continue;
                }
                polynomial.Terms.Add(new Term<TVar>(1, this.FlowVariables[pair]));
            }

            polynomial.Terms.Add(new Term<TVar>(-1, this.TotalDemandMetVariable));
            this.innerProblemEncoder.AddEqZeroConstraint(polynomial);

            // Ensure that the demands are finite.
            // This is needed because Z3 can return any value if demands can be infinite.
            // foreach (var (_, variable) in this.DemandVariables)
            // {
            //     this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1 * maxDemand)));
            // }

            // Ensure that the demand constraints are respected
            foreach (var (pair, constant) in this.DemandConstraints)
            {
                if (constant <= 0) {
                    continue;
                }
                var poly = this.DemandVariables[pair].Copy();
                poly.Add(new Term<TVar>(-1 * constant));
                this.innerProblemEncoder.AddEqZeroConstraint(poly);
            }

            // Ensure that f_k geq 0.
            // Ensure that f_k leq d_k.
            foreach (var (pair, variable) in this.FlowVariables)
            {
                // this.kktEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, variable)));
                var poly = this.DemandVariables[pair].Negate();
                poly.Add(new Term<TVar>(1, variable));
                this.innerProblemEncoder.AddLeqZeroConstraint(poly);
            }

            // Ensure that f_k^p geq 0.
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair)) {
                    continue;
                }
                foreach (var path in paths)
                {
                    this.innerProblemEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, this.FlowPathVariables[path])));
                }
            }

            // Ensure that the flow f_k = sum_p f_k^p.
            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair)) {
                    continue;
                }
                var poly = new Polynomial<TVar>(new Term<TVar>(0));
                foreach (var path in paths)
                {
                    poly.Terms.Add(new Term<TVar>(1, this.FlowPathVariables[path]));
                }

                poly.Terms.Add(new Term<TVar>(-1, this.FlowVariables[pair]));
                this.innerProblemEncoder.AddEqZeroConstraint(poly);
            }

            // Ensure the capacity constraints hold.
            // The sum of flows over all paths through each edge are bounded by capacity.
            var sumPerEdge = new Dictionary<Edge, Polynomial<TVar>>();
            // foreach (var edge in this.Topology.GetAllEdges())
            // {
            //     sumPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
            // }

            foreach (var (pair, paths) in this.Paths)
            {
                if (!IsDemandValid(pair)) {
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
                        if (!sumPerEdge.ContainsKey(edge)) {
                            sumPerEdge[edge] = new Polynomial<TVar>(new Term<TVar>(0));
                        }
                        sumPerEdge[edge].Terms.Add(term);
                    }
                }
            }

            foreach (var (edge, total) in sumPerEdge)
            {
                total.Terms.Add(new Term<TVar>(-1 * edge.Capacity));
                this.innerProblemEncoder.AddLeqZeroConstraint(total);
            }
            // generating the max constraints that achieve pinning.
            foreach (var (pair, polyTerm) in sumNonShortest) {
                // sum non shortest flows \leq MaxAuxVariables
                polyTerm.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.innerProblemEncoder.AddLeqZeroConstraint(polyTerm);
                // MaxAuxVariables \geq 0
                this.innerProblemEncoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, MaxAuxVariables[pair])));
                // maxNontPinned \geq M(d_k - T_d)
                var maxNonPinnedLB = this.DemandVariables[pair].Multiply(this._bigM);
                maxNonPinnedLB.Terms.Add(new Term<TVar>(-1 * this._bigM * Threshold));
                maxNonPinnedLB.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.innerProblemEncoder.AddLeqZeroConstraint(maxNonPinnedLB);
                // demand - shortest path flows \leq MaxAuxVariables
                var shortestPathUB = this.DemandVariables[pair].Copy();
                shortestPathUB.Terms.Add(new Term<TVar>(-1, shortestFlowVariables[pair]));
                shortestPathUB.Terms.Add(new Term<TVar>(-1, MaxAuxVariables[pair]));
                this.innerProblemEncoder.AddLeqZeroConstraint(shortestPathUB);
            }

            var objectiveFunction = new Polynomial<TVar>(new Term<TVar>(1, this.TotalDemandMetVariable));
            double alpha = this.Topology.TotalCapacity() * 1.1;
            Console.WriteLine("$$$$$$ alpha value for demand pinning objective = " + alpha);
            foreach (var (pair, maxVar) in MaxAuxVariables) {
                objectiveFunction.Terms.Add(new Term<TVar>(-1 * alpha, maxVar));
            }

            // Generate the full constraints.
            this.innerProblemEncoder.AddMaximizationConstraints(objectiveFunction, noAdditionalConstraints);

            // Optimization objective is the total demand met.
            // Return the encoding, including the feasibility constraints, objective, and KKT conditions.
            return new OptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.TotalDemandMetVariable,
                MaximizationObjective = objectiveFunction,
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

            foreach (var (pair, poly) in this.DemandVariables)
            {
                demands[pair] = 0;
                foreach (var term in poly.Terms) {
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
