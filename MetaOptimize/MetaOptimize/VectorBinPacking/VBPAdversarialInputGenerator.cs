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
    using NLog;

    /// <summary>
    /// This is where we solve the MetaOpt problem.
    /// The MetaOpt adversarialInputGenerators take as input the encoders for the
    /// two algorithms they want to compare and then encode both problems through these encoders' encoding function.
    /// Then they maximize the gap between the two algorithms.
    /// TODO -- research: right now, we have encoded the FFD heuristic by putting constraints on the order of the item's weights.
    /// This means we assume the items arrive in the order that they will be assigned by FFD. This is fine (does not loose any generality) because
    /// we can re-shuffle the items after the fact. However, if we want to CHAIN the VBP algorithm with any other algorithm, we have to be careful:
    /// it may be the case that this assumption would impact the other heuristics that interact with the VBP algorithm. We plan to fix this limitation in future work.
    /// </summary>
    public class VBPAdversarialInputGenerator<TVar, TSolution>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private double smallestDemandUnit = 2 * Math.Pow(10, -2);
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
        protected Dictionary<int, List<TVar>> ItemVariables { get; set; }

        /// <summary>
        /// demand to binary polynomial.
        /// </summary>
        protected Dictionary<int, List<Polynomial<TVar>>> ItemToBinaryPoly { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public VBPAdversarialInputGenerator(Bins bins, int numItems, int numDimensions, int numProcesses = -1)
        {
            this.Bins = bins;
            this.NumItems = numItems;
            this.NumDimensions = numDimensions;
            this.NumProcesses = numProcesses;
        }

        private Dictionary<int, List<TVar>> CreateItemVariables(
                ISolver<TVar, TSolution> solver)
        {
            var output = new Dictionary<int, List<TVar>>();
            Console.WriteLine("[INFO] In total " + this.Bins.GetNum() + " bins");
            for (int itemID = 0; itemID < NumItems; itemID++)
            {
                output[itemID] = new List<TVar>();
                for (int dim = 0; dim < NumDimensions; dim++)
                {
                    output[itemID].Add(solver.CreateVariable("demand_" + itemID + "_" + dim, lb: 0, ub: this.Bins.MaxCapacity(dim)));
                }
            }
            return output;
        }

        private void EnsureItemDimensionUB(
            ISolver<TVar, TSolution> solver,
            IDictionary<int, List<double>> itemPerDimensionUB)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++)
            {
                foreach (var (itemID, perDimensionItemUb) in itemPerDimensionUB)
                {
                    var ub = perDimensionItemUb[dim];
                    if (ub < 0)
                    {
                        ub = double.PositiveInfinity;
                    }
                    ub = Math.Min(this.Bins.MaxCapacity(dim), ub);
                    var boundEnforcerPoly = new Polynomial<TVar>();
                    boundEnforcerPoly.Add(new Term<TVar>(1, ItemVariables[itemID][dim]));
                    boundEnforcerPoly.Add(new Term<TVar>(-1 * ub));
                    solver.AddLeqZeroConstraint(boundEnforcerPoly);
                }
            }
        }

        private void EnsureItemDimensionUB(
            ISolver<TVar, TSolution> solver,
            double origItemDimensionUB)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++)
            {
                var itemDimensionUB = origItemDimensionUB;
                if (itemDimensionUB < 0)
                {
                    itemDimensionUB = double.PositiveInfinity;
                }
                itemDimensionUB = Math.Min(this.Bins.MaxCapacity(dim), itemDimensionUB);
                foreach (var (itemID, variable) in this.ItemVariables)
                {
                    var boundEnforcerPoly = new Polynomial<TVar>();
                    boundEnforcerPoly.Add(new Term<TVar>(1, variable[dim]));
                    boundEnforcerPoly.Add(new Term<TVar>(-1 * itemDimensionUB));
                    solver.AddLeqZeroConstraint(boundEnforcerPoly);
                }
            }
        }

        private void AddSingleItemEquality(
            ISolver<TVar, TSolution> solver,
            int itemID,
            List<double> demand)
        {
            for (int dim = 0; dim < this.NumDimensions; dim++)
            {
                var constrainerPoly = new Polynomial<TVar>();
                constrainerPoly.Add(new Term<TVar>(1, ItemVariables[itemID][dim]));
                constrainerPoly.Add(new Term<TVar>(-1 * demand[dim]));
                solver.AddEqZeroConstraint(constrainerPoly);
            }
        }

        private void EnsureItemEquality(
            ISolver<TVar, TSolution> solver,
            IDictionary<int, List<double>> constrainedItemDimensions)
        {
            if (constrainedItemDimensions == null)
            {
                return;
            }
            foreach (var (itemID, itemDimensions) in constrainedItemDimensions)
            {
                AddSingleItemEquality(solver, itemID, itemDimensions);
            }
        }
        // TODO-Engineering: is this dead code? should it be removed?
        // TODO: needs comment.
        private Polynomial<TVar> MultiplicationTwoBinaryPoly(
            ISolver<TVar, TSolution> solver,
            Polynomial<TVar> poly1,
            Polynomial<TVar> poly2)
        {
            var output = new Polynomial<TVar>();
            foreach (var term1 in poly1.GetTerms())
            {
                Debug.Assert(term1.Exponent == 1);
                foreach (var term2 in poly2.GetTerms())
                {
                    Debug.Assert(term2.Exponent == 1);
                    // replace multiplication of binary variables with z = xy.
                    var multBinary = solver.CreateVariable("multi_", type: GRB.BINARY);
                    output.Add(new Term<TVar>(term1.Coefficient * term2.Coefficient, multBinary));
                    // z <= x.
                    // z <= y.
                    solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                        new Term<TVar>(1, multBinary),
                        new Term<TVar>(-1, term1.Variable.Value)));
                    solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                        new Term<TVar>(1, multBinary),
                        new Term<TVar>(-1, term2.Variable.Value)));
                    // z >= x + y - 1
                    solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                        new Term<TVar>(-1),
                        new Term<TVar>(-1, multBinary),
                        new Term<TVar>(1, term1.Variable.Value),
                        new Term<TVar>(1, term2.Variable.Value)));
                }
            }
            return output;
        }

        // TODO: needs comment.
        // TODO -- engineering: should this and the other two related functions be moved into Gurobi SOS? and Zen?
        private Polynomial<TVar> MultiplicationBinaryContinuousPoly(
            ISolver<TVar, TSolution> solver,
            Polynomial<TVar> poly1,
            TVar variable2,
            double variableUB)
        {
            var output = new Polynomial<TVar>();
            foreach (var term1 in poly1.GetTerms())
            {
                var newVar = solver.CreateVariable("mult_bin_cont", type: GRB.CONTINUOUS, lb: 0);
                output.Add(new Term<TVar>(term1.Coefficient, newVar));
                // z <= Ux
                solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                    new Term<TVar>(1, newVar),
                    new Term<TVar>(-1 * variableUB, term1.Variable.Value)));
                // z <= y
                solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                    new Term<TVar>(1, newVar),
                    new Term<TVar>(-1, variable2)));
                // z >= y - U(1 - x)
                solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                    new Term<TVar>(-1, newVar),
                    new Term<TVar>(1, variable2),
                    new Term<TVar>(-1 * variableUB),
                    new Term<TVar>(variableUB, term1.Variable.Value)));
            }
            return output;
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two VBP algorithms.
        /// </summary>
        public (VBPOptimizationSolution, VBPOptimizationSolution) MaximizeOptimalityGapFFD(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numBinsUsedOptimal,
            FFDMethodChoice ffdMethod,
            double perItemDimensionUB = -1,
            IList<IList<double>> itemList = null,
            IDictionary<int, List<double>> constrainedItems = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<int, List<double>> perItemUB = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (perItemDimensionUB != -1 & perItemUB != null)
            {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }
            if (numBinsUsedOptimal <= 0 && numBinsUsedOptimal != -1)
            {
                throw new Exception("optimal number of bins should be either -1 or > 0.");
            }

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver)
            {
                solver.CleanAll();
            }

            Logger.Info("creating demand variables.");
            this.ItemVariables = CreateItemVariables(solver);
            CreateBinaryDemandLevels(solver, itemList, verbose);

            Logger.Info("generating optimal encoding.");
            // var optBins = this.Bins.GetFirstKBins(numBinsUsedOptimal);
            var optimalEncoding = optimalEncoder.Encoding(Bins, preInputVariables: this.ItemVariables, verbose: verbose);
            Logger.Info("generating heuristic encoding.");
            var heuristicEncoding = heuristicEncoder.Encoding(Bins, preInputVariables: this.ItemVariables, verbose: verbose);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Logger.Info("adding constraints for upper bound on demands.");
            if (perItemUB != null)
            {
                EnsureItemDimensionUB(solver, perItemUB);
            }
            else
            {
                EnsureItemDimensionUB(solver, perItemDimensionUB);
            }
            Logger.Info("adding equality constraints for specified demands.");
            EnsureItemEquality(solver, constrainedItems);
            AddFFDWeightConstraints(solver, ffdMethod, verbose);

            if (numBinsUsedOptimal > 0) {
                var optimalBinsPoly = new Polynomial<TVar>();
                optimalBinsPoly.Add(new Term<TVar>(-1 * numBinsUsedOptimal));
                optimalBinsPoly.Add(new Term<TVar>(1, optimalEncoding.GlobalObjective));
                solver.AddEqZeroConstraint(optimalBinsPoly);
            }

            Logger.Info("setting the objective.");
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(1, heuristicEncoding.GlobalObjective));
            var solution = solver.Maximize(objective, reset: true);

            return ((VBPOptimizationSolution)optimalEncoder.GetSolution(solution), (VBPOptimizationSolution)heuristicEncoder.GetSolution(solution));
        }

        private void AddFFDWeightConstraints(ISolver<TVar, TSolution> solver, FFDMethodChoice ffdMethod, bool verbose)
        {
            switch (ffdMethod)
            {
                case FFDMethodChoice.FF:
                    Logger.Info("Using FF Heuristic.");
                    break;
                case FFDMethodChoice.FFDSum:
                    Logger.Info("Using FFDSum Heuristic.");
                    for (int itemID = 0; itemID < this.NumItems - 1; itemID++)
                    {
                        var poly = new Polynomial<TVar>();
                        for (int dim = 0; dim < this.NumDimensions; dim++)
                        {
                            poly.Add(new Term<TVar>(1, this.ItemVariables[itemID + 1][dim]));
                            poly.Add(new Term<TVar>(-1, this.ItemVariables[itemID][dim]));
                        }
                        solver.AddLeqZeroConstraint(poly);
                    }
                    break;
                case FFDMethodChoice.FFDProd:
                    Logger.Info("Using FFDProd Heuristic.");
                    var itemIDToProd = new Dictionary<int, Polynomial<TVar>>();
                    for (int itemID = 0; itemID < this.NumItems; itemID++)
                    {
                        var multPoly = this.ItemToBinaryPoly[itemID][0].Copy();
                        // var multPoly = this.DemandVariables[itemID][0];
                        for (int dim = 1; dim < this.NumDimensions; dim++)
                        {
                            // multPoly = this.MultiplicationTwoBinaryPoly(solver, multPoly, this.DemandToBinaryPoly[itemID][dim]);
                            multPoly = this.MultiplicationBinaryContinuousPoly(solver, multPoly, this.ItemVariables[itemID][dim], this.Bins.MaxCapacity(dim));
                        }
                        itemIDToProd[itemID] = multPoly;
                    }
                    for (int itemID = 0; itemID < this.NumItems - 1; itemID++)
                    {
                        var poly = itemIDToProd[itemID].Negate();
                        poly.Add(itemIDToProd[itemID + 1]);
                        solver.AddLeqZeroConstraint(poly);
                    }
                    break;
                case FFDMethodChoice.FFDDiv:
                    Logger.Info("Using FFDDiv Heuristic.");
                    Debug.Assert(this.NumDimensions == 2);
                    for (int itemID = 0; itemID < this.NumItems; itemID++)
                    {
                        solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                            new Term<TVar>(-1, this.ItemVariables[itemID][0]),
                            new Term<TVar>(this.smallestDemandUnit)));
                        solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                            new Term<TVar>(-1, this.ItemVariables[itemID][1]),
                            new Term<TVar>(this.smallestDemandUnit)));
                    }
                    for (int itemID = 0; itemID < this.NumItems - 1; itemID++)
                    {
                        var poly1 = this.ItemToBinaryPoly[itemID][0].Copy();
                        var poly2 = this.ItemToBinaryPoly[itemID + 1][0].Copy();
                        poly1 = this.MultiplicationBinaryContinuousPoly(solver, poly1, this.ItemVariables[itemID + 1][1], this.Bins.MaxCapacity(1));
                        poly2 = this.MultiplicationBinaryContinuousPoly(solver, poly2, this.ItemVariables[itemID][1], this.Bins.MaxCapacity(0));
                        poly2.Add(poly1.Negate());
                        solver.AddLeqZeroConstraint(poly2);
                    }
                    break;
                default:
                    throw new Exception("invalid FFD Heuristic Method.");
            }
        }

        private void CreateBinaryDemandLevels(ISolver<TVar, TSolution> solver, IList<IList<double>> demandList, bool verbose)
        {
            this.ItemToBinaryPoly = new Dictionary<int, List<Polynomial<TVar>>>();
            if (demandList == null)
            {
                Logger.Info("demand List is null.");
                foreach (var (itemID, demandVar) in this.ItemVariables)
                {
                    this.ItemToBinaryPoly[itemID] = new List<Polynomial<TVar>>();
                    for (int dim = 0; dim < NumDimensions; dim++)
                    {
                        var demandPoly = new Polynomial<TVar>();
                        var sumPoly = new Polynomial<TVar>(new Term<TVar>(-1));
                        for (int i = 1; i <= ((int)Math.Ceiling(this.Bins.MaxCapacity(dim) / smallestDemandUnit)); i++)
                        {
                            var newBinary = solver.CreateVariable("demand_" + itemID + "_" + dim, type: GRB.BINARY);
                            demandPoly.Add(new Term<TVar>(-1 * i * smallestDemandUnit, newBinary));
                            sumPoly.Add(new Term<TVar>(1, newBinary));
                        }
                        this.ItemToBinaryPoly[itemID].Add(demandPoly.Negate());
                        demandPoly.Add(new Term<TVar>(1, demandVar[dim]));
                        solver.AddEqZeroConstraint(demandPoly);
                        solver.AddLeqZeroConstraint(sumPoly);
                    }
                }
            }
            else
            {
                Logger.Info("demand List specified.");
                foreach (var (itemID, demandVar) in this.ItemVariables)
                {
                    this.ItemToBinaryPoly[itemID] = new List<Polynomial<TVar>>();
                    for (int dim = 0; dim < NumDimensions; dim++)
                    {
                        var demandPoly = new Polynomial<TVar>();
                        var sumPoly = new Polynomial<TVar>(new Term<TVar>(1));
                        foreach (var demandlvl in demandList[dim])
                        {
                            var newBinary = solver.CreateVariable("bin_dim_" + itemID + "_" + dim + "_" + demandlvl, type: GRB.BINARY);
                            demandPoly.Add(new Term<TVar>(-1 * demandlvl, newBinary));
                            sumPoly.Add(new Term<TVar>(-1, newBinary));
                        }
                        this.ItemToBinaryPoly[itemID].Add(demandPoly.Negate());
                        demandPoly.Add(new Term<TVar>(1, demandVar[dim]));
                        solver.AddEqZeroConstraint(demandPoly);
                        solver.AddEqZeroConstraint(sumPoly);
                    }
                }
            }
        }

        private bool checkIfDemandIsConstrained(
            IDictionary<int, List<double>> constrainedDemands,
            int itemID)
        {
            return constrainedDemands.ContainsKey(itemID);
        }

        private double DiscoverMatchingDemandLvl(Polynomial<TVar> DemandVar, double demandValue)
        {
            if (demandValue <= 0.0001) {
                return 0;
            }
            foreach (var demandlvl in DemandVar.GetTerms()) {
                if (Math.Abs(demandlvl.Coefficient - demandValue) <= 0.0001) {
                    return demandlvl.Coefficient;
                }
            }
            throw new Exception(String.Format("does not match {0}", demandValue));
        }

        private (double, (VBPOptimizationSolution, VBPOptimizationSolution)) GetGap (
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            Dictionary<int, List<double>> itemSizes,
            bool disableStoreProgress = false)
        {
            // solving the hueristic for the demand
            heuristicEncoder.Solver.CleanAll(disableStoreProgress: disableStoreProgress);
            var demandVariables = CreateItemVariables(heuristicEncoder.Solver);
            var encodingHeuristic = heuristicEncoder.Encoding(Bins, preInputVariables: demandVariables,
                                            inputEqualityConstraints: itemSizes);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
            var optimizationSolutionHeuristic = (VBPOptimizationSolution)heuristicEncoder.GetSolution(solverSolutionHeuristic);

            // solving the optimal for the demand
            optimalEncoder.Solver.CleanAll(disableStoreProgress: disableStoreProgress);
            demandVariables = CreateItemVariables(optimalEncoder.Solver);
            var encodingOptimal = optimalEncoder.Encoding(Bins, preInputVariables: demandVariables,
                                            inputEqualityConstraints: itemSizes);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(encodingOptimal.MaximizationObjective);
            var optimizationSolutionOptimal = (VBPOptimizationSolution)optimalEncoder.GetSolution(solverSolutionOptimal);
            double currGap = optimizationSolutionOptimal.TotalNumBinsUsed - optimizationSolutionHeuristic.TotalNumBinsUsed;
            return (currGap, (optimizationSolutionOptimal, optimizationSolutionHeuristic));
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        public (VBPOptimizationSolution, VBPOptimizationSolution) MaximizeOptimalityGapFFDIterative(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            FFDMethodChoice ffdMethod,
            int numItemsEachIteration,
            int maxNumOptBinEachIteration = -1,
            double demandUB = -1,
            IList<IList<double>> demandList = null,
            IDictionary<int, List<double>> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<int, List<double>> perDemandUB = null)
        {
            constrainedDemands = constrainedDemands ?? new Dictionary<int, List<double>>();
            // only works for online ffd for now
            Debug.Assert(ffdMethod == FFDMethodChoice.FF);
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (demandUB != -1 & perDemandUB != null) {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }
            if (maxNumOptBinEachIteration != -1 & maxNumOptBinEachIteration < 0) {
                throw new Exception("maximum bin in each iteration should be either -1 or > 0.");
            }

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver) {
                solver.CleanAll();
            }

            Logger.Info("creating demand variables.");
            this.ItemVariables = CreateItemVariables(solver);
            CreateBinaryDemandLevels(solver, demandList, verbose);

            Logger.Info("generating optimal encoding.");
            // var optBins = this.Bins.GetFirstKBins(numBinsUsedOptimal);
            var optimalEncoding = optimalEncoder.Encoding(Bins, preInputVariables: this.ItemVariables, verbose: verbose);
            Logger.Info("generating heuristic encoding.");
            var heuristicEncoding = heuristicEncoder.Encoding(Bins, preInputVariables: this.ItemVariables, verbose: verbose);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Logger.Info("adding constraints for upper bound on demands.");
            if (perDemandUB != null)
            {
                EnsureItemDimensionUB(solver, perDemandUB);
            }
            else
            {
                EnsureItemDimensionUB(solver, demandUB);
            }
            Logger.Info("adding equality constraints for specified demands.");
            EnsureItemEquality(solver, constrainedDemands);

            Logger.Info("Initialize all demands with zero!");
            var itemToConstraintMapping = new Dictionary<int, List<string>>();
            foreach (var (itemID, ListDemandVar) in this.ItemVariables) {
                var listConstrNames = new List<string>();
                foreach (var demandVar in ListDemandVar) {
                    if (this.checkIfDemandIsConstrained(constrainedDemands, itemID)) {
                        continue;
                    }
                    var constrName = solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, demandVar)));
                    listConstrNames.Add(constrName);
                }
                itemToConstraintMapping[itemID] = listConstrNames;
            }

            var optimalUBConstraintName = "";
            if (maxNumOptBinEachIteration > 0) {
                Logger.Info("Add Upper bound on the number of bins optimal uses.");
                var optimalBinsPoly = new Polynomial<TVar>();
                optimalBinsPoly.Add(new Term<TVar>(-1 * maxNumOptBinEachIteration));
                optimalBinsPoly.Add(new Term<TVar>(1, optimalEncoding.GlobalObjective));
                optimalUBConstraintName = solver.AddLeqZeroConstraint(optimalBinsPoly);
            }
            solver.ModelUpdate();

            int lastItemPlaced = -1;
            int numOptBinsSoFar = 0;
            var itemSizes = new Dictionary<int, List<double>>();
            while (lastItemPlaced + 1 < this.NumItems) {
                Logger.Info(
                    string.Format("Placing Items {0} - {1}", lastItemPlaced + 1, lastItemPlaced + numItemsEachIteration),
                    verbose);
                var consideredItems = new HashSet<int>();
                for (var numPlaced = 0; numPlaced < numItemsEachIteration; numPlaced++) {
                    lastItemPlaced += 1;
                    if (lastItemPlaced >= this.NumItems) {
                        break;
                    }
                    foreach (var constrName in itemToConstraintMapping[lastItemPlaced]) {
                        solver.RemoveConstraint(constrName);
                    }
                    consideredItems.Add(lastItemPlaced);
                }

                if (maxNumOptBinEachIteration > 0) {
                    solver.ChangeConstraintRHS(optimalUBConstraintName, numOptBinsSoFar + maxNumOptBinEachIteration);
                }

                Logger.Info("setting the objective.");
                var objective = new Polynomial<TVar>(
                            new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                            new Term<TVar>(1, heuristicEncoding.GlobalObjective));
                var solution = solver.Maximize(objective, reset: true);
                var optimalSolution = (VBPOptimizationSolution)optimalEncoder.GetSolution(solution);
                numOptBinsSoFar = optimalSolution.TotalNumBinsUsed;
                // var heuristicSolution = (VBPOptimizationSolution)heuristicEncoder.GetSolution(solution);

                foreach (var itemID in consideredItems) {
                    itemSizes[itemID] = new List<double>();
                    for (var dimID = 0; dimID < this.NumDimensions; dimID++) {
                        var demandlvl = DiscoverMatchingDemandLvl(this.ItemToBinaryPoly[itemID][dimID],
                                            optimalSolution.Items[itemID][dimID]);
                        itemSizes[itemID].Add(demandlvl);
                    }
                    AddSingleItemEquality(solver, itemID, itemSizes[itemID]);
                }
            }
            // Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(itemSizes, Newtonsoft.Json.Formatting.Indented));
            var output = GetGap(optimalEncoder, heuristicEncoder, itemSizes);
            Logger.Info("Final gap: " + output.Item1);
            return output.Item2;
        }
    }
}
