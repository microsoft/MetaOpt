// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Gurobi;

    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public class VBPAdversarialInputGenerator<TVar, TSolution>
    {
        private double smallestDemandUnit =  Math.Pow(10, -2);
        /// <summary>
        /// The bins to fill.
        /// </summary>
        protected Bins Bins { get; set; }

        /// <summary>
        /// The number of items.
        /// </summary>
        protected int NumItems { get; set; }

        /// <summary>
        /// The dimension of resources.
        /// </summary>
        protected int NumDimensions { get; set; }

        /// <summary>
        /// number of processors to use for multiprocessing purposes.
        /// </summary>
        protected int NumProcesses { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        protected Dictionary<int, List<TVar>> DemandVariables { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public VBPAdversarialInputGenerator(Bins bins, int numItems, int numDimensions, int numProcesses = -1) {
            this.Bins = bins;
            this.NumItems = numItems;
            this.NumDimensions = numDimensions;
            this.NumProcesses = numProcesses;
        }

        private Dictionary<int, List<TVar>> CreateDemandVariables(
                ISolver<TVar, TSolution> solver) {
            var output = new Dictionary<int, List<TVar>>();
            Console.WriteLine("[INFO] In total " + this.Bins.GetNum() + " bins");
            for (int itemID = 0; itemID < NumItems; itemID++) {
                output[itemID] = new List<TVar>();
                for (int dim = 0; dim < NumDimensions; dim++) {
                    // output[itemID].Add(new Polynomial<TVar>(new Term<TVar>(smallestDemandUnit, solver.CreateVariable("demand_" + itemID + "_" + dim, type: GRB.INTEGER))));
                    output[itemID].Add(solver.CreateVariable("demand_" + itemID + "_" + dim, lb: 0));
                }
            }
            return output;
        }

        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            IDictionary<int, List<double>> demandUB)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++) {
                foreach (var (itemID, perDemandUb) in demandUB) {
                    var ub = perDemandUb[dim];
                    if (ub < 0) {
                        ub = double.PositiveInfinity;
                    }
                    ub = Math.Min(this.Bins.MaxCapacity(dim), ub);
                    var poly = new Polynomial<TVar>();
                    poly.Add(new Term<TVar>(1, DemandVariables[itemID][dim]));
                    poly.Add(new Term<TVar>(-1 * ub));
                    solver.AddLeqZeroConstraint(poly);
                }
            }
        }

        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            double origDemandUB)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++) {
                var demandUB = origDemandUB;
                if (demandUB < 0) {
                    demandUB = double.PositiveInfinity;
                }
                demandUB = Math.Min(this.Bins.MaxCapacity(dim), demandUB);
                foreach (var (itemID, variable) in this.DemandVariables)
                {
                    var poly = new Polynomial<TVar>();
                    poly.Add(new Term<TVar>(1, variable[dim]));
                    poly.Add(new Term<TVar>(-1 * demandUB));
                    solver.AddLeqZeroConstraint(poly);
                }
            }
        }

        private void AddSingleDemandEquality(
            ISolver<TVar, TSolution> solver,
            int itemID,
            List<double> demand)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++) {
                var poly = new Polynomial<TVar>();
                poly.Add(new Term<TVar>(1, DemandVariables[itemID][dim]));
                poly.Add(new Term<TVar>(-1 * demand[dim]));
                solver.AddEqZeroConstraint(poly);
            }
        }

        private void EnsureDemandEquality(
            ISolver<TVar, TSolution> solver,
            IDictionary<int, List<double>> constrainedDemands)
        {
            if (constrainedDemands == null) {
                return;
            }
            foreach (var (itemID, demand) in constrainedDemands) {
                AddSingleDemandEquality(solver, itemID, demand);
            }
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        public (VBPOptimizationSolution, VBPOptimizationSolution) MaximizeOptimalityGapSumFFD(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numBinsUsedOptimal,
            double demandUB = -1,
            IDictionary<int, List<double>> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<int, List<double>> perDemandUB = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (demandUB != -1 & perDemandUB != null) {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver) {
                solver.CleanAll();
            }

            Utils.logger("creating demand variables.", verbose);
            this.DemandVariables = CreateDemandVariables(solver);
            foreach (var (itemID, demandVar) in this.DemandVariables) {
                for (int dim = 0; dim < NumDimensions; dim++) {
                    var outPoly = new Polynomial<TVar>();
                    outPoly.Add(new Term<TVar>(-1 * smallestDemandUnit,
                                    solver.CreateVariable("demand_" + itemID + "_" + dim, type: GRB.INTEGER, lb: 0, ub: this.Bins.MaxCapacity(dim) / smallestDemandUnit)));
                    outPoly.Add(new Term<TVar>(1, demandVar[dim]));
                    solver.AddEqZeroConstraint(outPoly);
                }
            }
            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(Bins, preDemandVariables: this.DemandVariables, verbose: verbose);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(Bins, preDemandVariables: this.DemandVariables, verbose: verbose);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            if (perDemandUB != null) {
                EnsureDemandUB(solver, perDemandUB);
            } else {
                EnsureDemandUB(solver, demandUB);
            }
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);

            for (int itemID = 0; itemID < this.NumItems - 1; itemID++) {
                var poly = new Polynomial<TVar>();
                for (int dim = 0; dim < this.NumDimensions; dim++) {
                    poly.Add(new Term<TVar>(1, this.DemandVariables[itemID + 1][dim]));
                    poly.Add(new Term<TVar>(-1, this.DemandVariables[itemID][dim]));
                }
                solver.AddLeqZeroConstraint(poly);
            }

            var optimalBinsPoly = new Polynomial<TVar>();
            optimalBinsPoly.Add(new Term<TVar>(-1 * numBinsUsedOptimal));
            optimalBinsPoly.Add(new Term<TVar>(1, optimalEncoding.GlobalObjective));
            solver.AddEqZeroConstraint(optimalBinsPoly);

            Utils.logger("setting the objective.", verbose);
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(1, heuristicEncoding.GlobalObjective));
            var solution = solver.Maximize(objective, reset: true);

            return ((VBPOptimizationSolution)optimalEncoder.GetSolution(solution), (VBPOptimizationSolution)heuristicEncoder.GetSolution(solution));
        }
    }
}
