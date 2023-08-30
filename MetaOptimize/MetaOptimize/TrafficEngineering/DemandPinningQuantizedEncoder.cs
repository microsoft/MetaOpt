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
    public class DemandPinningQuantizedEncoder<TVar, TSolution> : DemandPinningEncoder<TVar, TSolution>
    {
        /// <summary>
        /// Auxilary variable used to encode DP.
        /// </summary>
        private Dictionary<(string, string), Polynomial<TVar>> SPLowerBound { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="DemandPinningEncoder{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="solver">The solver.</param>
        /// <param name="k">The max number of paths between nodes.</param>
        /// <param name="threshold"> The threshold to use for demand pinning.</param>
        /// <param name="scaleFactor"> The scale factor to show the input is downscaled.</param>
        public DemandPinningQuantizedEncoder(ISolver<TVar, TSolution> solver, int k, double threshold = 0,
                double scaleFactor = 1.0) : base(solver, k, threshold, scaleFactor)
        {
        }

        /// <summary>
        /// Create auxiliary variables to model max() in DP formulation.
        /// </summary>
        protected override void CreateAuxVariable()
        {
            this.SPLowerBound = new Dictionary<(string, string), Polynomial<TVar>>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!IsDemandValid(pair))
                {
                    continue;
                }
                this.SPLowerBound[pair] = this.DemandPolynomials[pair].GetTermsWithCoeffLeq(this.Threshold);
            }
        }

        /// <summary>
        /// verify output.
        /// </summary>
        protected override void VerifyOutput(TSolution solution, Dictionary<(string, string), double> demands, Dictionary<(string, string), double> flows)
        {
            foreach (var (pair, demand) in demands)
            {
                if (!flows.ContainsKey(pair))
                {
                    continue;
                }
                if (demand <= this.Threshold && Math.Abs(flows[pair] - demand) > 0.001)
                {
                    Console.WriteLine($"{pair.Item1},{pair.Item2},{demand},{flows[pair]}");
                    throw new Exception("does not match");
                }
                bool found = false;
                if (demand <= 0.001)
                {
                    found = true;
                }
                else
                {
                    foreach (var demandlvl in this.DemandPolynomials[pair].GetTerms())
                    {
                        // Console.WriteLine("_" + demandlvl.Coefficient + " " + demand);
                        if (Math.Abs(demand - demandlvl.Coefficient) <= 0.001)
                        {
                            found = true;
                        }
                    }
                }
                if (!found)
                {
                    Console.WriteLine($"{pair.Item1},{pair.Item2},{demand},{flows[pair]}");
                    throw new Exception("does not match");
                }
            }
        }

        /// <summary>
        /// add Demand Pinning Constraints.
        /// </summary>
        protected override void GenerateDPConstraints(Polynomial<TVar> objectiveFunction, bool verbose)
        {
            // generating the max constraints that achieve pinning.
            Utils.logger("Generating Quantized DP constraints.", verbose);
            foreach (var (pair, polyTerm) in sumNonShortestDict)
            {
                // shortest path flows \geq quantized demand with coefficient less than equal threshold
                var shortestPathUB = this.SPLowerBound[pair].Copy();
                shortestPathUB.Add(new Term<TVar>(-1, shortestFlowVariables[pair]));
                this.innerProblemEncoder.AddLeqZeroConstraint(shortestPathUB);
            }

            // double alpha = Math.Ceiling(this.Topology.TotalCapacity() * 1.1);
            // Console.WriteLine("$$$$$$ alpha value for demand pinning objective = " + alpha);
            // foreach (var (pair, demandVar) in DemandVariables) {
            //     this.Solver.AddGlobalTerm(demandVar.Multiply(Math.Round(-1 / alpha, 3)));
            // }
        }
    }
}
