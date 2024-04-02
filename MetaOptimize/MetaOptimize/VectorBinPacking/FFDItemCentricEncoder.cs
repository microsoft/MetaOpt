// <copyright file="FFDItemCentricEncoder.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gurobi;
using NLog;

namespace MetaOptimize
{
    /// <summary>
    /// Encodes the first fit decreasing algorithm that solves the vector bin packing problem.
    /// The vector bin packing problem is one which takes as input a set of multi-dimensional bins
    /// and a set of multi-dimensional items. The goal of the algorithm is to fit the items in as few bins as possible.
    /// TODO-Engineering: work on changing the variable names to map better to the VBP problem.
    /// The first fit decreasing problem sorts items by a particular weight value and then places each ball in the first bin that it fits in.
    /// </summary>
    public class FFDItemCentricEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private double bigM = Math.Pow(10, 3);
        private double Epsilon = Math.Pow(10, -6);

        /// <summary>
        /// The solver being used.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// The list of bins.
        /// </summary>
        public Bins bins { get; set; }

        /// <summary>
        /// The variables that capture the incoming items.
        /// Each item is a list of variables that encode the demand of that item for each dimension.
        /// </summary>
        public Dictionary<int, List<TVar>> IncomingItemVars { get; set; }

        /// <summary>
        /// The items per bin variables.
        /// For each item, for ALL bins we have a variable FOR EACH DIMENSION of the item.
        /// </summary>
        public Dictionary<int, List<List<TVar>>> ItemsPerBinVars { get; set; }

        /// <summary>
        /// Pre-specified constraints on the item sizes.
        /// </summary>
        public Dictionary<int, List<double>> ItemSizeConstraints { get; set; }

        /// <summary>
        /// Prespecify where some of the items should be placed.
        /// </summary>
        public Dictionary<int, int> ItemPlacementConstraints { get; set; }

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
        public FFDItemCentricEncoder(ISolver<TVar, TSolution> solver, int NumItems, int NumDimensions)
        {
            this.Solver = solver;
            this.NumDimensions = NumDimensions;
            this.NumItems = NumItems;
        }

        private bool IsItemValid(int itemID) {
            if (this.ItemSizeConstraints.ContainsKey(itemID)) {
                foreach (var demand in this.ItemSizeConstraints[itemID]) {
                    if (demand > 0) {
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        private void InitializeVariables(Dictionary<int, List<TVar>> preItemVariables = null,
            Dictionary<int, List<double>> itemEqualityConstraints = null,
            Dictionary<int, int> itemPlacementEqualityConstraints = null)
        {
            this.ItemSizeConstraints = itemEqualityConstraints ?? new Dictionary<int, List<double>>();
            this.ItemPlacementConstraints = itemPlacementEqualityConstraints ?? new Dictionary<int, int>();
            this.IncomingItemVars = new Dictionary<int, List<TVar>>();

            if (preItemVariables == null) {
                for (int id = 0; id < this.NumItems; id++) {
                    if (!IsItemValid(id)) {
                        continue;
                    }
                    this.IncomingItemVars[id] = new List<TVar>();
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        var variable = this.Solver.CreateVariable("demand_" + id + "_" + dimension);
                        this.IncomingItemVars[id].Add(variable);
                    }
                }
            } else {
                Debug.Assert(preItemVariables.Count == this.NumItems);
                foreach (var (id, variable) in preItemVariables) {
                    if (!IsItemValid(id)) {
                        continue;
                    }
                    Debug.Assert(variable.Count == this.NumDimensions);
                    this.IncomingItemVars[id] = variable;
                }
            }

            this.TotalNumBinsUsedVariable = this.Solver.CreateVariable("total_num_bins");
            this.PlacementVariables = new Dictionary<int, List<TVar>>();
            this.FitVariable = new Dictionary<int, List<TVar>>();
            this.FitVariablePerDimension = new Dictionary<int, List<List<TVar>>>();
            this.ItemsPerBinVars = new Dictionary<int, List<List<TVar>>>();

            foreach (int id in this.IncomingItemVars.Keys) {
                this.PlacementVariables[id] = new List<TVar>();
                this.FitVariable[id] = new List<TVar>();
                this.FitVariablePerDimension[id] = new List<List<TVar>>();
                this.ItemsPerBinVars[id] = new List<List<TVar>>();
                for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                    this.PlacementVariables[id].Add(this.Solver.CreateVariable("placement_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.FitVariable[id].Add(this.Solver.CreateVariable("fit_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.FitVariablePerDimension[id].Add(new List<TVar>());
                    this.ItemsPerBinVars[id].Add(new List<TVar>());
                    for (int did = 0; did < this.NumDimensions; did++) {
                        this.FitVariablePerDimension[id][bid].Add(
                            this.Solver.CreateVariable("dim_fit_item_" + id + "_bin_" + bid + "_dim_" + did, type: GRB.BINARY));
                        this.ItemsPerBinVars[id][bid].Add(
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
        /// This is where we encode the problem properly.
        /// It takes as input the bins (which describe the total number of the bins we have available
        /// and the size of the bin along each dimension). The pre-InputVariables are the input variables
        /// (in this case these are the variables that encode the items and their size along each dimention) that
        /// come from the adversarial generator.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(Bins bins,
            Dictionary<int, List<TVar>> preInputVariables = null,
            Dictionary<int, List<double>> inputEqualityConstraints = null,
            Dictionary<int, int> inputPlacementEqualityConstraints = null,
            bool verbose = false)
        {
            Logger.Info("initialize variables");
            this.bins = bins;
            InitializeVariables(preInputVariables, inputEqualityConstraints,
                    inputPlacementEqualityConstraints);

            Logger.Info("ensuring constraints on the items are respected");
            foreach (var (itemID, sizeConstant) in this.ItemSizeConstraints)
            {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    if (!IsItemValid(itemID) || sizeConstant[dimension] < 0) {
                        continue;
                    }
                    var sizeConstraintPoly = new Polynomial<TVar>();
                    sizeConstraintPoly.Add(new Term<TVar>(1, this.IncomingItemVars[itemID][dimension]));
                    sizeConstraintPoly.Add(new Term<TVar>(-1 * sizeConstant[dimension]));
                    this.Solver.AddEqZeroConstraint(sizeConstraintPoly);
                }
            }
            // ensures the solution respects pre-specified item-placement decisions.
            Logger.Info("ensure item placement constraints are respected");
            foreach (var (itemID, placementConstant) in this.ItemPlacementConstraints)
            {
                Debug.Assert(placementConstant < this.bins.GetNum());
                var itemPlacementConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                itemPlacementConstraint.Add(new Term<TVar>(1, this.PlacementVariables[itemID][placementConstant]));
                this.Solver.AddEqZeroConstraint(itemPlacementConstraint);
            }

            Logger.Info("ensure each item ends up in exactly one bin");
            foreach (var itemID in this.IncomingItemVars.Keys) {
                if (!IsItemValid(itemID)) {
                    continue;
                }
                var placePoly = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    placePoly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                }
                this.Solver.AddEqZeroConstraint(placePoly);
            }

            Logger.Info("ensure each item placed in a bin if it is not placed in any prior bins");
            foreach (var itemID in this.IncomingItemVars.Keys) {
                if (!IsItemValid(itemID)) {
                    continue;
                }
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    var coeff = (1 + binId);
                    var placePoly = new Polynomial<TVar>(new Term<TVar>(-1));
                    placePoly.Add(new Term<TVar>(coeff, PlacementVariables[itemID][binId]));
                    for (int j = 0; j < binId; j++) {
                        placePoly.Add(new Term<TVar>(-1, FitVariable[itemID][j]));
                    }
                    placePoly.Add(new Term<TVar>(1, FitVariable[itemID][binId]));
                    this.Solver.AddLeqZeroConstraint(placePoly);
                }
            }

            Logger.Info("ensure each item can fit in the right bin");
            var binSizeList = this.bins.getBinSizes();
            foreach (var itemID in this.IncomingItemVars.Keys) {
                if (!IsItemValid(itemID)) {
                    continue;
                }
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    var sumAllFitVariablePerDimension = new Polynomial<TVar>();
                    var binSize = binSizeList[binId];
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        // encode constraint for residual capacity.
                        var binFitPoly = new Polynomial<TVar>();
                        // adding - c_j + y_i
                        binFitPoly.Add(new Term<TVar>(1, this.IncomingItemVars[itemID][dimension]));
                        binFitPoly.Add(new Term<TVar>(-1 * binSize[dimension]));
                        // adding sum (alpha_kj y_k)
                        for (var k = 0; k < itemID; k++) {
                            if (!IsItemValid(k)) {
                                continue;
                            }
                            binFitPoly.Add(new Term<TVar>(1, this.ItemsPerBinVars[k][binId][dimension]));
                        }
                        // adding - beta_ijd * y_id
                        binFitPoly.Add(new Term<TVar>(-1 * this.bigM, this.FitVariablePerDimension[itemID][binId][dimension]));
                        // Encodes equation 15 in the arxive paper.
                        this.Solver.AddLeqZeroConstraint(binFitPoly);

                        // ensure item fits in the bin with sufficient capacity
                        binFitPoly = new Polynomial<TVar>();
                        // adding c_j + (epsilon - M -1) y_i
                        binFitPoly.Add(new Term<TVar>(-1, this.IncomingItemVars[itemID][dimension]));
                        binFitPoly.Add(new Term<TVar>(binSize[dimension] + this.Epsilon - this.bigM));
                        // adding M * beta_ijd * y_id
                        binFitPoly.Add(new Term<TVar>(this.bigM,  this.FitVariablePerDimension[itemID][binId][dimension]));
                        // adding -1 * sum (alpha_kj y_k)
                        for (var k = 0; k < itemID; k++) {
                            if (!IsItemValid(k)) {
                                continue;
                            }
                            binFitPoly.Add(new Term<TVar>(-1, this.ItemsPerBinVars[k][binId][dimension]));
                        }
                        // TODO: add reference for which equation in the paper this encodes.
                        this.Solver.AddLeqZeroConstraint(binFitPoly);

                        // ensure FitVariables >= FitVariablePerDimension
                        var fitPoly = new Polynomial<TVar>();
                        fitPoly.Add(new Term<TVar>(1, this.FitVariablePerDimension[itemID][binId][dimension]));
                        fitPoly.Add(new Term<TVar>(-1, this.FitVariable[itemID][binId]));
                        this.Solver.AddLeqZeroConstraint(fitPoly);

                        sumAllFitVariablePerDimension.Add(new Term<TVar>(-1, this.FitVariablePerDimension[itemID][binId][dimension]));
                    }
                    sumAllFitVariablePerDimension.Add(new Term<TVar>(1, this.FitVariable[itemID][binId]));
                    this.Solver.AddLeqZeroConstraint(sumAllFitVariablePerDimension);
                }
            }

            Logger.Info("ensure only one itemPerBinVariable is non-zero");
            foreach (int itemID in this.IncomingItemVars.Keys) {
                if (!IsItemValid(itemID)) {
                    continue;
                }
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    var totalAssignedPoly = new Polynomial<TVar>();
                    for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                        var capacityCheckPoly = new Polynomial<TVar>();
                        capacityCheckPoly.Add(new Term<TVar>(1, this.ItemsPerBinVars[itemID][binId][dimension]));
                        capacityCheckPoly.Add(new Term<TVar>(-1 * this.bins.MaxCapacity(dimension), this.PlacementVariables[itemID][binId]));
                        this.Solver.AddLeqZeroConstraint(capacityCheckPoly);

                        totalAssignedPoly.Add(new Term<TVar>(1, this.ItemsPerBinVars[itemID][binId][dimension]));
                    }
                    totalAssignedPoly.Add(new Term<TVar>(-1, this.IncomingItemVars[itemID][dimension]));
                    this.Solver.AddEqZeroConstraint(totalAssignedPoly);
                }
            }

            Logger.Info("ensure bin used = 1 if any item in bin");
            for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                var checkBinUsedPoly = new Polynomial<TVar>();
                foreach (int itemID in this.IncomingItemVars.Keys) {
                    if (!IsItemValid(itemID)) {
                        continue;
                    }
                    var markBinUsedPoly = new Polynomial<TVar>();
                    markBinUsedPoly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                    markBinUsedPoly.Add(new Term<TVar>(-1, this.BinUsedVariables[binId]));
                    this.Solver.AddLeqZeroConstraint(markBinUsedPoly);
                    checkBinUsedPoly.Add(new Term<TVar>(-1, this.PlacementVariables[itemID][binId]));
                }
                checkBinUsedPoly.Add(new Term<TVar>(1, this.BinUsedVariables[binId]));
                this.Solver.AddLeqZeroConstraint(checkBinUsedPoly);
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
                ItemVariables = this.IncomingItemVars,
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

            foreach (var (id, itemDemand) in this.IncomingItemVars)
            {
                items[id] = new List<double>();
                for (var dimension = 0; dimension < this.NumDimensions; dimension++) {
                    items[id].Add(0.0);
                    var perDimensionDemand = itemDemand[dimension];
                    items[id][dimension] = this.Solver.GetVariable(solution, perDimensionDemand);
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