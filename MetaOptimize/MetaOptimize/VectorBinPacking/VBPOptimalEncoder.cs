// <copyright file="OptimalEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Gurobi;

    /// <summary>
    /// A class for the VBP optimal encoding.
    /// </summary>
    public class VBPOptimalEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The solver being user.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// The list of bins.
        /// </summary>
        public Bins bins { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        public Dictionary<int, List<TVar>> DemandVariables { get; set; }

        /// <summary>
        /// The demand variables per bin.
        /// </summary>
        public Dictionary<int, List<List<TVar>>> DemandPerBinVariables { get; set; }

        /// <summary>
        /// The demand constraints in terms of constant values.
        /// </summary>
        public Dictionary<int, List<double>> DemandConstraints { get; set; }

        /// <summary>
        /// The number of objects.
        /// </summary>
        public int NumItems { get; set; }

        /// <summary>
        /// variables to see which item end up in which bin.
        /// </summary>
        public Dictionary<int, List<TVar>> PlacementVariables { get; set; }

        /// <summary>
        /// variables to see if each bin is used or not.
        /// </summary>
        public List<TVar> BinUsedVariables { get; set; }

        /// <summary>
        /// number of dimensions.
        /// </summary>
        public int NumDimensions { get; set; }

        /// <summary>
        /// total num bins used.
        /// </summary>
        public TVar TotalNumBinsUsedVariable { get; set; }

        /// <summary>
        /// Create a new instance of the <see cref="VBPOptimalEncoder{TVar, TSolution}"/> class.
        /// </summary>
        public VBPOptimalEncoder(ISolver<TVar, TSolution> solver, int NumItems, int NumDimensions)
        {
            this.Solver = solver;
            this.NumDimensions = NumDimensions;
            this.NumItems = NumItems;
        }
        private bool IsDemandValid(int itemID) {
            if (this.DemandConstraints.ContainsKey(itemID)) {
                foreach (var demand in this.DemandConstraints[itemID]) {
                    if (demand > 0) {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        private void InitializeVariables(Dictionary<int, List<TVar>> preDemandVariables = null,
            Dictionary<int, List<double>> demandEqualityConstraints = null)
        {
            this.DemandConstraints = demandEqualityConstraints ?? new Dictionary<int, List<double>>();
            this.DemandVariables = new Dictionary<int, List<TVar>>();

            if (preDemandVariables == null) {
                for (int id = 0; id < this.NumItems; id++) {
                    if (!IsDemandValid(id)) {
                        continue;
                    }
                    this.DemandVariables[id] = new List<TVar>();
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        var variable = this.Solver.CreateVariable("demand_" + id + "_" + dimension);
                        this.DemandVariables[id].Add(variable);
                    }
                }
            } else {
                Debug.Assert(preDemandVariables.Count == this.NumItems);
                foreach (var (id, variable) in preDemandVariables) {
                    if (!IsDemandValid(id)) {
                        continue;
                    }
                    Debug.Assert(variable.Count == this.NumDimensions);
                    this.DemandVariables[id] = variable;
                }
            }

            this.TotalNumBinsUsedVariable = this.Solver.CreateVariable("total_num_bins");
            this.PlacementVariables = new Dictionary<int, List<TVar>>();
            this.DemandPerBinVariables = new Dictionary<int, List<List<TVar>>>();
            foreach (int id in this.DemandVariables.Keys) {
                this.PlacementVariables[id] = new List<TVar>();
                this.DemandPerBinVariables[id] = new List<List<TVar>>();
                for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                    this.PlacementVariables[id].Add(this.Solver.CreateVariable("placement_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.DemandPerBinVariables[id].Add(new List<TVar>());
                    for (int did = 0; did < this.NumDimensions; did++) {
                        this.DemandPerBinVariables[id][bid].Add(
                            this.Solver.CreateVariable("dem_per_bin_item_" + id + "_bin_" + bid + "_dim_" + did, lb: 0));
                    }
                }
            }

            this.BinUsedVariables = new List<TVar>();
            for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                this.BinUsedVariables.Add(this.Solver.CreateVariable("bin_used_" + bid, type: GRB.BINARY));
            }
        }

        /// <summary>
        /// Encoder the problem.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(Bins bins, Dictionary<int, List<TVar>> preDemandVariables = null,
            Dictionary<int, List<double>> demandEqualityConstraints = null, bool verbose = false)
        {
            Utils.logger("initialize variables", verbose);
            this.bins = bins;
            InitializeVariables(preDemandVariables, demandEqualityConstraints);

            Utils.logger("ensure capacity constraints are respected", verbose);
            var binSizeList = this.bins.getBinSizes();
            for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                var binSize = binSizeList[binId];
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    var capPoly = new Polynomial<TVar>(new Term<TVar>(-1 * binSize[dimension], this.BinUsedVariables[binId]));
                    // var demandCoeff = new List<Polynomial<TVar>>();
                    // var demandVar = new List<TVar>();
                    foreach (int itemID in this.DemandVariables.Keys) {
                        // demandVar.Add(this.DemandVariables[itemID][dimension]);
                        // demandCoeff.Add(new Polynomial<TVar>(new Term<TVar>(1, this.PlacementVariables[itemID][binId])));
                        capPoly.Add(new Term<TVar>(1, this.DemandPerBinVariables[itemID][binId][dimension]));
                    }
                    // this.Solver.AddLeqZeroConstraint(demandCoeff, demandVar, capPoly);
                    this.Solver.AddLeqZeroConstraint(capPoly);
                }
            }

            Utils.logger("ensure only one demandPerBinVariable is non-zero", verbose);
            foreach (int itemID in this.DemandVariables.Keys) {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    var sumPoly = new Polynomial<TVar>();
                    for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                        var poly = new Polynomial<TVar>();
                        poly.Add(new Term<TVar>(1, this.DemandPerBinVariables[itemID][binId][dimension]));
                        poly.Add(new Term<TVar>(-1 * this.bins.MaxCapacity(dimension), this.PlacementVariables[itemID][binId]));
                        this.Solver.AddLeqZeroConstraint(poly);

                        sumPoly.Add(new Term<TVar>(1, this.DemandPerBinVariables[itemID][binId][dimension]));
                    }
                    sumPoly.Add(new Term<TVar>(-1, this.DemandVariables[itemID][dimension]));
                    this.Solver.AddEqZeroConstraint(sumPoly);
                }
            }

            Utils.logger("ensure each item ends up in exactly one bin", verbose);
            foreach (var itemID in this.DemandVariables.Keys) {
                var placePoly = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    placePoly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                }
                this.Solver.AddEqZeroConstraint(placePoly);
            }

            Utils.logger("ensuring demand constraints are respected", verbose);
            foreach (var (itemID, demandConstant) in this.DemandConstraints)
            {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    if (demandConstant[dimension] < 0) {
                        continue;
                    }
                    var poly = new Polynomial<TVar>();
                    poly.Add(new Term<TVar>(1, this.DemandVariables[itemID][dimension]));
                    poly.Add(new Term<TVar>(-1 * demandConstant[dimension]));
                    this.Solver.AddEqZeroConstraint(poly);
                }
            }

            Utils.logger("ensure objective == total bins used", verbose);
            var objPoly = new Polynomial<TVar>(new Term<TVar>(-1, this.TotalNumBinsUsedVariable));
            foreach (var binUsedVar in BinUsedVariables) {
                objPoly.Add(new Term<TVar>(1, binUsedVar));
            }
            this.Solver.AddEqZeroConstraint(objPoly);

            var objective = new Polynomial<TVar>(new Term<TVar>(-1, this.TotalNumBinsUsedVariable));

            return new VBPptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.TotalNumBinsUsedVariable,
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
            var demands = new Dictionary<int, List<double>>();
            var placements = new Dictionary<int, List<int>>();

            foreach (var (id, itemDemand) in this.DemandVariables)
            {
                demands[id] = new List<double>();
                for (var dimension = 0; dimension < this.NumDimensions; dimension++) {
                    demands[id].Add(0.0);
                    var perDimensionDemand = itemDemand[dimension];
                    demands[id][dimension] = this.Solver.GetVariable(solution, perDimensionDemand);
                }
            }

            foreach (var (id, variableList) in this.PlacementVariables) {
                placements[id] = new List<int>();
                foreach (var variable in variableList) {
                    placements[id].Add(Convert.ToInt32(this.Solver.GetVariable(solution, variable)));
                }
            }

            return new VBPOptimizationSolution
            {
                Demands = demands,
                Placement = placements,
                TotalNumBinsUsed = Convert.ToInt32(this.Solver.GetVariable(solution, this.TotalNumBinsUsedVariable)),
            };
        }
    }
}