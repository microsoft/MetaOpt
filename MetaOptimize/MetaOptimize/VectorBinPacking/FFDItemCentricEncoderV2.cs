// <copyright file="FFDItemCentricEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Gurobi;
    using NLog;

    /// <summary>
    /// Encodes the first fit decreasing algorithm that solves the vector bin packing problem.
    /// The vector bin packing problem is one which takes as input a set of multi-dimensional bins
    /// and a set of multi-dimensional items. The goal of the algorithm is to fit the items in as few bins as possible.
    /// TODO-Engineering: work on changing the variable names to map better to the VBP problem.
    /// The first fit decreasing problem sorts items by a particular weight value and then places each ball in the first bin that it fits in.
    /// </summary>
    public class FFDItemCentricEncoderV2<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private double bigM = Math.Pow(10, 3);
        private double Epsilon = Math.Pow(10, -3);

        /// <summary>
        /// The solver being user.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// The list of bins.
        /// </summary>
        public Bins bins { get; set; }

        /// <summary>
        /// Variables that represent the inputs (the sequence of arriving items).
        /// </summary>
        public Dictionary<int, List<TVar>> itemVariables { get; set; }

        /// <summary>
        /// The items per bin.
        /// </summary>
        public Dictionary<int, List<List<TVar>>> ItemsPerBinVariables { get; set; }

        /// <summary>
        /// The item constraints in terms of constant values.
        /// </summary>
        public Dictionary<int, List<double>> itemConstraints { get; set; }

        /// <summary>
        /// The number of objects.
        /// </summary>
        public int NumItems { get; set; }

        /// <summary>
        /// variables to see which item end up in which bin.
        /// </summary>
        public Dictionary<int, List<TVar>> PlacementVariables { get; set; }

        /// <summary>
        /// variables to see if an item fits in a bin.
        /// </summary>
        public Dictionary<int, List<TVar>> FitVariable { get; set; }

        /// <summary>
        /// variables to see if an item fits in an specific dimension of a bin.
        /// </summary>
        public Dictionary<int, List<List<TVar>>> FitVariablePerDimension { get; set; }

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
        /// Create a new instance of the <see cref="FFDItemCentricEncoder{TVar, TSolution}"/> class.
        /// </summary>
        public FFDItemCentricEncoderV2(ISolver<TVar, TSolution> solver, int NumItems, int NumDimensions)
        {
            this.Solver = solver;
            this.NumDimensions = NumDimensions;
            this.NumItems = NumItems;
        }

        private bool IsItemValid(int itemID) {
            if (this.itemConstraints.ContainsKey(itemID)) {
                foreach (var demand in this.itemConstraints[itemID]) {
                    if (demand > 0) {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        private void InitializeVariables(Dictionary<int, List<TVar>> preItemVariables = null,
            Dictionary<int, List<double>> itemEqualityConstraints = null)
        {
            this.itemConstraints = itemEqualityConstraints ?? new Dictionary<int, List<double>>();
            this.itemVariables = new Dictionary<int, List<TVar>>();

            if (preItemVariables == null) {
                for (int id = 0; id < this.NumItems; id++) {
                    if (!IsItemValid(id)) {
                        continue;
                    }
                    this.itemVariables[id] = new List<TVar>();
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        var variable = this.Solver.CreateVariable("demand_" + id + "_" + dimension);
                        this.itemVariables[id].Add(variable);
                    }
                }
            } else {
                Debug.Assert(preItemVariables.Count == this.NumItems);
                foreach (var (id, variable) in preItemVariables) {
                    if (!IsItemValid(id)) {
                        continue;
                    }
                    Debug.Assert(variable.Count == this.NumDimensions);
                    this.itemVariables[id] = variable;
                }
            }

            this.TotalNumBinsUsedVariable = this.Solver.CreateVariable("total_num_bins");
            this.PlacementVariables = new Dictionary<int, List<TVar>>();
            this.FitVariable = new Dictionary<int, List<TVar>>();
            this.FitVariablePerDimension = new Dictionary<int, List<List<TVar>>>();
            this.ItemsPerBinVariables = new Dictionary<int, List<List<TVar>>>();

            foreach (int id in this.itemVariables.Keys) {
                this.PlacementVariables[id] = new List<TVar>();
                this.FitVariable[id] = new List<TVar>();
                this.FitVariablePerDimension[id] = new List<List<TVar>>();
                this.ItemsPerBinVariables[id] = new List<List<TVar>>();
                for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                    this.PlacementVariables[id].Add(this.Solver.CreateVariable("placement_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.FitVariable[id].Add(this.Solver.CreateVariable("fit_item_" + id + "_bin_" + bid, lb: 0));
                    this.FitVariablePerDimension[id].Add(new List<TVar>());
                    this.ItemsPerBinVariables[id].Add(new List<TVar>());
                    for (int did = 0; did < this.NumDimensions; did++) {
                        this.FitVariablePerDimension[id][bid].Add(
                            this.Solver.CreateVariable("dim_fit_item_" + id + "_bin_" + bid + "_dim_" + did, lb: 0));
                        this.ItemsPerBinVariables[id][bid].Add(
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
        /// This is where we fully describe the Bin Packingfirst fit decreasing problem as a feasibility problem.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(Bins bins, Dictionary<int, List<TVar>> preDemandVariables = null,
            Dictionary<int, List<double>> demandEqualityConstraints = null, bool verbose = false)
        {
            Logger.Info("initialize variables");
            this.bins = bins;
            InitializeVariables(preDemandVariables, demandEqualityConstraints);

            Logger.Info("ensuring item constraints are respected");
            foreach (var (itemID, demandConstant) in this.itemConstraints)
            {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    if (demandConstant[dimension] <= 0) {
                        continue;
                    }
                    var itemConstraintPoly = new Polynomial<TVar>();
                    itemConstraintPoly.Add(new Term<TVar>(1, this.itemVariables[itemID][dimension]));
                    itemConstraintPoly.Add(new Term<TVar>(-1 * demandConstant[dimension]));
                    this.Solver.AddEqZeroConstraint(itemConstraintPoly);
                }
            }

            Logger.Info("ensure each item ends up in exactly one bin");
            foreach (var itemID in this.itemVariables.Keys) {
                var placePoly = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    placePoly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                }
                this.Solver.AddEqZeroConstraint(placePoly);
            }

            Logger.Info("ensure each item placed in a bin if it is not placed in any prior bins");
            foreach (var itemID in this.itemVariables.Keys) {
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    var coeff = (1 + binId);
                    var placePoly = new Polynomial<TVar>(new Term<TVar>(-1 * coeff));
                    placePoly.Add(new Term<TVar>(coeff, PlacementVariables[itemID][binId]));
                    for (int j = 0; j < binId; j++) {
                        placePoly.Add(new Term<TVar>(1, FitVariable[itemID][j]));
                    }
                    this.Solver.AddLeqZeroConstraint(placePoly);

                    var placePoly2 = new Polynomial<TVar>(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                    placePoly2.Add(new Term<TVar>(-1 * this.bigM, FitVariable[itemID][binId]));
                    this.Solver.AddLeqZeroConstraint(placePoly2);
                }
            }

            Logger.Info("ensure each item can fit in the right bin");
            var binSizeList = this.bins.getBinSizes();
            foreach (var itemID in this.itemVariables.Keys) {
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    var sumAllFitVariablePerDimension = new Polynomial<TVar>();
                    var binSize = binSizeList[binId];
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        var residualCap = new Polynomial<TVar>(new Term<TVar>(binSize[dimension], this.BinUsedVariables[binId]));
                        residualCap.Add(new Term<TVar>(this.Epsilon));
                        residualCap.Add(new Term<TVar>(-1, this.itemVariables[itemID][dimension]));
                        for (var k = 0; k < itemID; k++) {
                            residualCap.Add(new Term<TVar>(-1, this.ItemsPerBinVariables[k][binId][dimension]));
                        }

                        // adding B_ij = max(residualCap, 0)
                        residualCap.Add(new Term<TVar>(-1, this.FitVariablePerDimension[itemID][binId][dimension]));
                        this.Solver.AddLeqZeroConstraint(residualCap);
                        this.Solver.AddGlobalTerm(
                            new Polynomial<TVar>(new Term<TVar>(-1 * this.Epsilon, this.FitVariablePerDimension[itemID][binId][dimension])));

                        sumAllFitVariablePerDimension.Add(new Term<TVar>(1, this.FitVariablePerDimension[itemID][binId][dimension]));
                    }
                    sumAllFitVariablePerDimension.Add(new Term<TVar>(-1, this.FitVariable[itemID][binId]));
                    this.Solver.AddEqZeroConstraint(sumAllFitVariablePerDimension);
                }
            }

            Logger.Info("ensure only one itemPerBinVariable is non-zero");
            foreach (int itemID in this.itemVariables.Keys) {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    var totalBinsPerItemPoly = new Polynomial<TVar>();
                    for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                        var isBinAssignedToItemPoly = new Polynomial<TVar>();
                        isBinAssignedToItemPoly.Add(new Term<TVar>(1, this.ItemsPerBinVariables[itemID][binId][dimension]));
                        isBinAssignedToItemPoly.Add(new Term<TVar>(-1 * this.bins.MaxCapacity(dimension), this.PlacementVariables[itemID][binId]));
                        this.Solver.AddLeqZeroConstraint(isBinAssignedToItemPoly);

                        totalBinsPerItemPoly.Add(new Term<TVar>(1, this.ItemsPerBinVariables[itemID][binId][dimension]));
                    }
                    totalBinsPerItemPoly.Add(new Term<TVar>(-1, this.itemVariables[itemID][dimension]));
                    this.Solver.AddEqZeroConstraint(totalBinsPerItemPoly);
                }
            }

            Logger.Info("ensure bin used = 1 if any item in bin");
            for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                var totalBinsUsedForItemPoly = new Polynomial<TVar>();
                foreach (int itemID in this.itemVariables.Keys) {
                    totalBinsUsedForItemPoly.Add(new Term<TVar>(-1, this.PlacementVariables[itemID][binId]));
                }
                totalBinsUsedForItemPoly.Add(new Term<TVar>(1, this.BinUsedVariables[binId]));
                this.Solver.AddLeqZeroConstraint(totalBinsUsedForItemPoly);
            }

            Logger.Info("ensure objective == total bins used");
            var objPoly = new Polynomial<TVar>(new Term<TVar>(-1, this.TotalNumBinsUsedVariable));
            foreach (var binUsedVar in BinUsedVariables) {
                objPoly.Add(new Term<TVar>(1, binUsedVar));
            }
            this.Solver.AddEqZeroConstraint(objPoly);

            var objective = new Polynomial<TVar>(new Term<TVar>(0));

            return new VBPptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.TotalNumBinsUsedVariable,
                MaximizationObjective = objective,
                ItemVariables = this.itemVariables,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver solution.
        /// </summary>
        /// <param name="solution">The solution.</param>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var items = new Dictionary<int, List<double>>();
            var placements = new Dictionary<int, List<int>>();

            foreach (var (id, itemDemand) in this.itemVariables)
            {
                items[id] = new List<double>();
                for (var dimension = 0; dimension < this.NumDimensions; dimension++) {
                    items[id].Add(0.0);
                    var perDimensionDemand = itemDemand[dimension];
                    items[id][dimension] = this.Solver.GetVariable(solution, perDimensionDemand);
                }
            }

            foreach (var (id, fitVar) in this.FitVariablePerDimension) {
                for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                    for (int dim = 0; dim < this.NumDimensions; dim++) {
                        Console.WriteLine(String.Format("=== item id {0}, bin id {1}, dim {2}, beta_ijd {3}, beta_ij {6}, per bin demand {4} placed {5}",
                            id, bid, dim, this.Solver.GetVariable(solution, this.FitVariablePerDimension[id][bid][dim]),
                            this.Solver.GetVariable(solution, this.ItemsPerBinVariables[id][bid][dim]),
                            this.Solver.GetVariable(solution, this.PlacementVariables[id][bid]),
                            this.Solver.GetVariable(solution, this.FitVariable[id][bid])));
                    }
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
                Items = items,
                Placement = placements,
                TotalNumBinsUsed = Convert.ToInt32(this.Solver.GetVariable(solution, this.TotalNumBinsUsedVariable)),
            };
        }
    }
}