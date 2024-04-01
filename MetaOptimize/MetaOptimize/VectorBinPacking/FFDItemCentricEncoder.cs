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
    /// A class for the VBP optimal encoding.
    /// </summary>
    public class FFDItemCentricEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private double bigM = Math.Pow(10, 3);
        private double Epsilon = Math.Pow(10, -6);

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
        /// Prespecify where some of the demands should be placed.
        /// </summary>
        public Dictionary<int, int> DemandPlacementConstraints { get; set; }

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
            Dictionary<int, List<double>> demandEqualityConstraints = null,
            Dictionary<int, int> demandPlacementEqualityConstraints = null)
        {
            this.DemandConstraints = demandEqualityConstraints ?? new Dictionary<int, List<double>>();
            this.DemandPlacementConstraints = demandPlacementEqualityConstraints ?? new Dictionary<int, int>();
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
            this.FitVariable = new Dictionary<int, List<TVar>>();
            this.FitVariablePerDimension = new Dictionary<int, List<List<TVar>>>();
            this.DemandPerBinVariables = new Dictionary<int, List<List<TVar>>>();

            foreach (int id in this.DemandVariables.Keys) {
                this.PlacementVariables[id] = new List<TVar>();
                this.FitVariable[id] = new List<TVar>();
                this.FitVariablePerDimension[id] = new List<List<TVar>>();
                this.DemandPerBinVariables[id] = new List<List<TVar>>();
                for (int bid = 0; bid < this.bins.GetNum(); bid++) {
                    this.PlacementVariables[id].Add(this.Solver.CreateVariable("placement_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.FitVariable[id].Add(this.Solver.CreateVariable("fit_item_" + id + "_bin_" + bid, type: GRB.BINARY));
                    this.FitVariablePerDimension[id].Add(new List<TVar>());
                    this.DemandPerBinVariables[id].Add(new List<TVar>());
                    for (int did = 0; did < this.NumDimensions; did++) {
                        this.FitVariablePerDimension[id][bid].Add(
                            this.Solver.CreateVariable("dim_fit_item_" + id + "_bin_" + bid + "_dim_" + did, type: GRB.BINARY));
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

            Logger.Info("ensuring demand constraints are respected");
            foreach (var (itemID, demandConstant) in this.DemandConstraints)
            {
                for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                    if (!IsDemandValid(itemID) || demandConstant[dimension] < 0) {
                        continue;
                    }
                    var poly = new Polynomial<TVar>();
                    poly.Add(new Term<TVar>(1, this.DemandVariables[itemID][dimension]));
                    poly.Add(new Term<TVar>(-1 * demandConstant[dimension]));
                    this.Solver.AddEqZeroConstraint(poly);
                }
            }

            Logger.Info("ensure demand placement constraints are respected");
            foreach (var (itemID, placementConstant) in this.DemandPlacementConstraints)
            {
                Debug.Assert(placementConstant < this.bins.GetNum());
                var poly = new Polynomial<TVar>(new Term<TVar>(-1));
                poly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][placementConstant]));
                this.Solver.AddEqZeroConstraint(poly);
            }

            Logger.Info("ensure each item ends up in exactly one bin");
            foreach (var itemID in this.DemandVariables.Keys) {
                if (!IsDemandValid(itemID)) {
                    continue;
                }
                var placePoly = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    placePoly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                }
                this.Solver.AddEqZeroConstraint(placePoly);
            }

            Logger.Info("ensure each item placed in a bin if it is not placed in any prior bins");
            foreach (var itemID in this.DemandVariables.Keys) {
                if (!IsDemandValid(itemID)) {
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
            foreach (var itemID in this.DemandVariables.Keys) {
                if (!IsDemandValid(itemID)) {
                    continue;
                }
                for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                    var sumAllFitVariablePerDimension = new Polynomial<TVar>();
                    var binSize = binSizeList[binId];
                    for (int dimension = 0; dimension < this.NumDimensions; dimension++) {
                        // ensure item does not fit in the bin with low capacity
                        // var polyCoeff = new List<Polynomial<TVar>>();
                        // var polyVar = new List<TVar>();
                        var linearPoly = new Polynomial<TVar>();
                        // adding - c_j + y_i
                        linearPoly.Add(new Term<TVar>(1, this.DemandVariables[itemID][dimension]));
                        // linearPoly.Add(new Term<TVar>(-1 * binSize[dimension] - this.Epsilon));
                        linearPoly.Add(new Term<TVar>(-1 * binSize[dimension]));
                        // adding sum (alpha_kj y_k)
                        for (var k = 0; k < itemID; k++) {
                            if (!IsDemandValid(k)) {
                                continue;
                            }
                            // polyCoeff.Add(new Polynomial<TVar>(new Term<TVar>(1, this.PlacementVariables[k][binId])));
                            // polyVar.Add(this.DemandVariables[k][dimension]);
                            linearPoly.Add(new Term<TVar>(1, this.DemandPerBinVariables[k][binId][dimension]));
                        }
                        // adding - beta_ijd * y_id
                        linearPoly.Add(new Term<TVar>(-1 * this.bigM, this.FitVariablePerDimension[itemID][binId][dimension]));
                        // polyCoeff.Add(new Polynomial<TVar>(new Term<TVar>(-1, this.FitVariablePerDimension[itemID][binId][dimension])));
                        // polyVar.Add(this.DemandVariables[itemID][dimension]);
                        // this.Solver.AddLeqZeroConstraint(polyCoeff, polyVar, linearPoly);
                        this.Solver.AddLeqZeroConstraint(linearPoly);

                        // ensure item fits in the bin with sufficient capacity
                        // polyCoeff = new List<Polynomial<TVar>>();
                        // polyVar = new List<TVar>();
                        linearPoly = new Polynomial<TVar>();
                        // adding c_j + (epsilon - M -1) y_i
                        // linearPoly.Add(new Term<TVar>(-1 * this.bigM - 1, this.DemandVariables[itemID][dimension]));
                        linearPoly.Add(new Term<TVar>(-1, this.DemandVariables[itemID][dimension]));
                        linearPoly.Add(new Term<TVar>(binSize[dimension] + this.Epsilon - this.bigM));
                        // adding M * beta_ijd * y_id
                        linearPoly.Add(new Term<TVar>(this.bigM,  this.FitVariablePerDimension[itemID][binId][dimension]));
                        // polyCoeff.Add(new Polynomial<TVar>(new Term<TVar>(this.bigM, this.FitVariablePerDimension[itemID][binId][dimension])));
                        // polyVar.Add(this.DemandVariables[itemID][dimension]);
                        // adding -1 * sum (alpha_kj y_k)
                        for (var k = 0; k < itemID; k++) {
                            if (!IsDemandValid(k)) {
                                continue;
                            }
                            // polyCoeff.Add(new Polynomial<TVar>(new Term<TVar>(-1, this.PlacementVariables[k][binId])));
                            // polyVar.Add(this.DemandVariables[k][dimension]);
                            linearPoly.Add(new Term<TVar>(-1, this.DemandPerBinVariables[k][binId][dimension]));
                        }
                        // this.Solver.AddLeqZeroConstraint(polyCoeff, polyVar, linearPoly);
                        this.Solver.AddLeqZeroConstraint(linearPoly);

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

            Logger.Info("ensure only one demandPerBinVariable is non-zero");
            foreach (int itemID in this.DemandVariables.Keys) {
                if (!IsDemandValid(itemID)) {
                    continue;
                }
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

            Logger.Info("ensure bin used = 1 if any item in bin");
            for (int binId = 0; binId < this.bins.GetNum(); binId++) {
                var sumPoly = new Polynomial<TVar>();
                foreach (int itemID in this.DemandVariables.Keys) {
                    if (!IsDemandValid(itemID)) {
                        continue;
                    }
                    var poly = new Polynomial<TVar>();
                    poly.Add(new Term<TVar>(1, this.PlacementVariables[itemID][binId]));
                    poly.Add(new Term<TVar>(-1, this.BinUsedVariables[binId]));
                    this.Solver.AddLeqZeroConstraint(poly);
                    sumPoly.Add(new Term<TVar>(-1, this.PlacementVariables[itemID][binId]));
                }
                sumPoly.Add(new Term<TVar>(1, this.BinUsedVariables[binId]));
                this.Solver.AddLeqZeroConstraint(sumPoly);
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