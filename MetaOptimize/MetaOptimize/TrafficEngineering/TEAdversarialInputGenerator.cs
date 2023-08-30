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
    public class TEAdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        protected Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        protected int maxNumPath { get; set; }

        /// <summary>
        /// number of processors to use for multiprocessing purposes.
        /// </summary>
        protected int NumProcesses { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        protected Dictionary<(string, string), Polynomial<TVar>> DemandEnforcers { get; set; }

        /// <summary>
        /// demnad constrains enforced by locality.
        /// </summary>
        protected Dictionary<(string, string), double> LocalityConstrainedDemands { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public TEAdversarialInputGenerator(Topology topology, int maxNumPaths, int numProcesses = -1)
        {
            this.Topology = topology;
            this.maxNumPath = maxNumPaths;
            this.NumProcesses = numProcesses;
        }

        // TODO: need a comment that describes what this function is doing. Also is this the only way you can simplify?
        // OR are there other ways?
        private TSolution SimplifyAdversarialInputs(bool simplify, IEncoder<TVar, TSolution> optimalEncoder, IEncoder<TVar, TSolution> heuristicEncoder,
            TSolution solution, Polynomial<TVar> objective)
        {
            if (simplify)
            {
                var solver = optimalEncoder.Solver;
                Console.WriteLine("===== Going to simplify the solution....");
                var simplifier = new TEAdversarialInputSimplifier<TVar, TSolution>(Topology, maxNumPath, DemandEnforcers);
                var optimalObj = ((TEOptimizationSolution)optimalEncoder.GetSolution(solution)).MaxObjective;
                var heuristicObj = ((TEOptimizationSolution)heuristicEncoder.GetSolution(solution)).MaxObjective;
                var gap = optimalObj - heuristicObj;
                var simplifyObj = simplifier.AddDirectMinConstraintsAndObjectives(solver, objective, gap);
                solution = solver.Maximize(simplifyObj, reset: true);
            }
            return solution;
        }
        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// TODO: need a better comment here that describes what this function is actually doing.
        /// Is this gap generator really only specific to TE?
        /// TODO: need to describe the inputs.
        public virtual (TEOptimizationSolution, TEOptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            IDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            double density = 1.0,
            double LargeDemandLB = -1,
            int LargeMaxDistance = -1,
            int SmallMaxDistance = -1,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (innerEncoding == InnerRewriteMethodChoice.PrimalDual & demandList == null)
            {
                throw new Exception("should provide the demand list if inner encoding method is primal dual.");
            }
            if (demandUB != -1 & perDemandUB != null)
            {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }
            if (pathType == PathType.Predetermined && selectedPaths == null)
            {
                throw new Exception("if path type is predetermined, the paths should not be null");
            }
            if (pathType != PathType.Predetermined && selectedPaths != null)
            {
                throw new Exception("if path type is not predetermined, the paths should be null");
            }

            // check if the inputs to the function ``make sense''.
            CheckDensityAndLocalityInputs(innerEncoding, density, LargeDemandLB, LargeMaxDistance, SmallMaxDistance);

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver)
            {
                solver.CleanAll();
            }

            Utils.logger("creating demand variables.", verbose);
            Utils.logger("max large demand distance: " + LargeMaxDistance, verbose);
            Utils.logger("max small demand distance: " + SmallMaxDistance, verbose);
            Utils.logger("large demand lb: " + LargeDemandLB, verbose);

            (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, LargeDemandLB, LargeMaxDistance, SmallMaxDistance);

            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: LocalityConstrainedDemands, noAdditionalConstraints: true,
                    pathType: pathType, selectedPaths: selectedPaths, historicDemandConstraints: historicDemandConstraints);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: LocalityConstrainedDemands,
                    pathType: pathType, selectedPaths: selectedPaths, historicDemandConstraints: historicDemandConstraints);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            if (perDemandUB != null)
            {
                EnsureDemandUB(solver, perDemandUB);
            }
            else
            {
                EnsureDemandUB(solver, demandUB);
            }
            // TODO: modify this to only print in debug mode.
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);
            Utils.logger("Adding density constraint: max density = " + density, verbose);
            EnsureDensityConstraint(solver, density);

            Utils.logger("setting the objective.", verbose);

            var objective = new Polynomial<TVar>();
            objective.Add(optimalEncoding.MaximizationObjective.Copy());
            objective.Add(heuristicEncoding.MaximizationObjective.Negate());
            var solution = solver.Maximize(objective, reset: true);

            // TODO: what is the implication of this on scale?
            solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);

            var optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
            var heuSol = (TEOptimizationSolution)heuristicEncoder.GetSolution(solution);
            return (optSol, heuSol);
        }

        /// <summary>
        /// Find an adversarial input that takes the value of the optimal as an input.
        ///  Then, it maximizes the gap of the heuristic given the optimal value.
        /// </summary>
        public virtual List<(TEOptimizationSolution, TEOptimizationSolution)> MaximizeOptimalityGapGivenOpt(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double optimalObj,
            double demandUB = -1,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            IDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            bool cleanUpSolver = true,
            IDictionary<(string, string), double> perDemandUB = null,
            IDictionary<(string, string), double> demandInits = null,
            double density = 1.0,
            double LargeDemandLB = -1,
            int LargeMaxDistance = -1,
            int SmallMaxDistance = -1,
            PathType pathType = PathType.KSP,
            Dictionary<(string, string), string[][]> selectedPaths = null,
            Dictionary<(int, string, string), double> historicDemandConstraints = null,
            int solutionCount = 0)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (innerEncoding == InnerRewriteMethodChoice.PrimalDual & demandList == null)
            {
                throw new Exception("should provide the demand list if inner encoding method is primal dual.");
            }
            if (demandUB != -1 & perDemandUB != null)
            {
                throw new Exception("if global demand ub is enabled, then perDemandUB should be null");
            }
            if (pathType == PathType.Predetermined && selectedPaths == null)
            {
                throw new Exception("if path type is predetermined, the paths should not be null");
            }
            if (pathType != PathType.Predetermined && selectedPaths != null)
            {
                throw new Exception("if path type is not predetermined, the paths should be null");
            }
            if (solutionCount > 1 && simplify == true)
            {
                throw new Exception("simplify is not implemented yet when looking for more than one solution");
            }
            CheckDensityAndLocalityInputs(innerEncoding, density, LargeDemandLB, LargeMaxDistance, SmallMaxDistance);

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver)
            {
                solver.CleanAll();
            }

            Utils.logger("creating demand variables.", verbose);
            Utils.logger("max large demand distance: " + LargeMaxDistance, verbose);
            Utils.logger("max small demand distance: " + SmallMaxDistance, verbose);
            Utils.logger("large demand lb: " + LargeDemandLB, verbose);
            (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList, demandInits, LargeDemandLB, LargeMaxDistance, SmallMaxDistance);
            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: LocalityConstrainedDemands, noAdditionalConstraints: true,
                    pathType: pathType, selectedPaths: selectedPaths, historicDemandConstraints: historicDemandConstraints);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: LocalityConstrainedDemands,
                    pathType: pathType, selectedPaths: selectedPaths, historicDemandConstraints: historicDemandConstraints);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            if (perDemandUB != null)
            {
                EnsureDemandUB(solver, perDemandUB);
            }
            else
            {
                EnsureDemandUB(solver, demandUB);
            }
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);
            Utils.logger("Adding density constraint: max density = " + density, verbose);
            EnsureDensityConstraint(solver, density);

            Utils.logger("setting the objective of OPT.", verbose);
            var optPoly = optimalEncoding.MaximizationObjective.Negate();
            optPoly.Add(new Term<TVar>(-optimalObj));
            solver.AddEqZeroConstraint(optPoly);

            Utils.logger("setting the objective of MetaOpt.", verbose);
            var objective = heuristicEncoding.MaximizationObjective.Negate();
            var solution = solver.Maximize(objective, reset: true, solutionCount: solutionCount);

            solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);

            var solList = new List<(TEOptimizationSolution, TEOptimizationSolution)>();
            for (int sNumber = 0; sNumber < solutionCount; sNumber++)
            {
                var optSol = (TEOptimizationSolution)optimalEncoder.GetSolution(solution, sNumber);
                var heuSol = (TEOptimizationSolution)heuristicEncoder.GetSolution(solution, sNumber);
                solList.Add((optSol, heuSol));
            }
            return solList;
        }

        // TODO: this function is missing a comment.
        // TODO: you have not defined what is small and large flow anywhere? it should be explicit in the comments.
        private static void CheckDensityAndLocalityInputs(
            InnerRewriteMethodChoice innerEncoding,
            double density,
            double LargeDemandLB,
            int LargeMaxDistance,
            int SmallMaxDistance,
            bool randomInitialization = false)
        {
            if (density > 1.0 || density < 0)
            {
                throw new Exception("density should be between 0 an 1 but got " + density);
            }
            if (LargeMaxDistance <= 0 && LargeMaxDistance != -1)
            {
                throw new Exception("Large Flow Max Distance should either -1 [disabled] or >= 1 but got " + LargeMaxDistance);
            }
            if (SmallMaxDistance <= 0 && SmallMaxDistance != -1)
            {
                throw new Exception("Small Flow Max Distance should either -1 [disabled] or >= 1 but got " + SmallMaxDistance);
            }
            if ((LargeMaxDistance >= 1 || SmallMaxDistance >= 1) && LargeDemandLB < 0)
            {
                throw new Exception("The demand value that separates large from small demands should be positive but got " + LargeDemandLB);
            }
            if (LargeMaxDistance >= 1 || SmallMaxDistance >= 1 || density < 1.0)
            {
                if (innerEncoding == InnerRewriteMethodChoice.KKT)
                {
                    // TODO: why? because of the "if" constraints?
                    throw new Exception("to apply locality or sparsity constraints, the encoding should be primal-dual.");
                }
                if (randomInitialization)
                {
                    throw new Exception("Not implemented random initialization yet.");
                }
            }
        }

        private bool checkIfPairIsConstrained(
            IDictionary<(string, string), double> constrainedDemands,
            (string, string) pair)
        {
            return constrainedDemands.ContainsKey(pair) || this.LocalityConstrainedDemands.ContainsKey(pair);
        }

        private double DiscoverMatchingDemandLvl(Polynomial<TVar> DemandVar, double demandValue)
        {
            if (demandValue <= 0.0001)
            {
                return 0;
            }
            foreach (var demandlvl in DemandVar.GetTerms())
            {
                if (Math.Abs(demandlvl.Coefficient - demandValue) <= 0.0001)
                {
                    return demandlvl.Coefficient;
                }
            }
            throw new Exception(String.Format("does not match {0}", demandValue));
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) MaximizeOptimalityGapWithClusteringV2(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false,
            double density = 1.0,
            double LargeDemandLB = -1,
            int LargeMaxDistance = -1,
            int SmallMaxDistance = -1,
            bool randomInitialization = false,
            IEncoder<TVar, TSolution> HeuisticDirectEncoder = null)
        {
            CheckDensityAndLocalityInputs(innerEncoding, density, LargeDemandLB, LargeMaxDistance, SmallMaxDistance, randomInitialization);
            if (density < 1.0)
            {
                throw new Exception("density constraint is not implemented completely for the clustering approach. " +
                                    "Need to think about how to translate to cluster level density.");
            }
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters)
            {
                foreach (var node in cluster.GetAllNodes())
                {
                    if (seenNode.Contains(node))
                    {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count())
            {
                throw new Exception(
                    String.Format("missmatch between number of nodes in original problem {0} and clustered version {1}",
                        this.Topology.GetAllNodes().Count(),
                        seenNode.Count()));
            }
            if (constrainedDemands == null)
            {
                constrainedDemands = new Dictionary<(string, string), double>();
            }
            Dictionary<(string, string), double> rndDemand = null;
            var timer = Stopwatch.StartNew();
            double currGap = 0;
            if (randomInitialization)
            {
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
                var rng = new Random(Seed: 0);
                rndDemand = new Dictionary<(string, string), double>();
                int numTrials = 10;
                for (int i = 0; i < numTrials; i++)
                {
                    bool feasible = true;
                    var currRndDemand = getRandomDemand(rng, demandUB, demandList);
                    double currRndGap = 0.0;
                    do
                    {
                        feasible = true;
                        try
                        {
                            (currRndGap, _) = GetGap(optimalEncoder, HeuisticDirectEncoder, currRndDemand, disableStoreProgress: true);
                            if (currRndGap > currGap)
                            {
                                currGap = currRndGap;
                                rndDemand = currRndDemand;
                            }
                        }
                        catch (DemandPinningLinkNegativeException e)
                        {
                            feasible = false;
                            Console.WriteLine("Infeasible input!");
                            ReduceDemandsOnLink(currRndDemand, e.Edge, e.Threshold, 0);
                        }
                    } while (!feasible);
                }
            }
            timer.Stop();

            var solver = optimalEncoder.Solver;
            solver.CleanAll();
            if (randomInitialization)
            {
                solver.AppendToStoreProgressFile(timer.ElapsedMilliseconds, currGap, reset: false);
            }
            Utils.logger("creating demand variables.", verbose);
            (this.DemandEnforcers, this.LocalityConstrainedDemands) =
                        CreateDemandVariables(solver, innerEncoding, demandList,
                                LargeDemandLB: LargeDemandLB, LargeMaxDistance: LargeMaxDistance, SmallMaxDistance: SmallMaxDistance);
            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: this.LocalityConstrainedDemands, noAdditionalConstraints: true);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                    innerEncoding: innerEncoding, numProcesses: this.NumProcesses, verbose: verbose,
                    inputEqualityConstraints: LocalityConstrainedDemands);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            Utils.logger("adding constraints for upper bound on demands.", verbose);
            EnsureDemandUB(solver, demandUB);
            Utils.logger("adding equality constraints for specified demands.", verbose);
            EnsureDemandEquality(solver, constrainedDemands);
            Utils.logger("Adding density constraint: max density = " + density, verbose);
            EnsureDensityConstraint(solver, density);

            if (demandUB < 0)
            {
                demandUB = this.maxNumPath * this.Topology.MaxCapacity();
            }

            var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
            if (!randomInitialization)
            {
                Utils.logger("Initialize all demands with zero!", verbose);
                foreach (var (pair, demandVar) in this.DemandEnforcers)
                {
                    if (checkIfPairIsConstrained(constrainedDemands, pair))
                    {
                        continue;
                    }
                    var constrName = solver.AddLeqZeroConstraint(demandVar);
                    pairNameToConstraintMapping[pair] = constrName;
                }
            }
            else
            {
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
                Utils.logger("Randomly Initialize Demands!", verbose);
                foreach (var (pair, demandVar) in this.DemandEnforcers)
                {
                    if (checkIfPairIsConstrained(constrainedDemands, pair))
                    {
                        continue;
                    }
                    var foundLvl = false;
                    TVar demandLvlVariable = demandVar.GetTerms()[0].Variable.Value;
                    foreach (var demandlvl in demandVar.GetTerms())
                    {
                        if (Math.Abs(demandlvl.Coefficient - rndDemand[pair]) <= 0.0001)
                        {
                            foundLvl = true;
                            demandLvlVariable = demandlvl.Variable.Value;
                        }
                    }
                    var constrName = "";
                    if (foundLvl)
                    {
                        var poly = new Polynomial<TVar>(new Term<TVar>(1, demandLvlVariable));
                        poly.Add(new Term<TVar>(-1));
                        constrName = solver.AddEqZeroConstraint(poly);
                    }
                    else
                    {
                        constrName = solver.AddLeqZeroConstraint(demandVar);
                    }
                    pairNameToConstraintMapping[pair] = constrName;
                }
            }
            solver.ModelUpdate();

            var demandMatrix = new Dictionary<(string, string), double>();
            // find gap for all the clusters
            foreach (var cluster in clusters)
            {
                var consideredPairs = new HashSet<(string, string)>();
                Utils.logger(
                    string.Format("finding adversarial demand for cluster with {0} nodes and {1} edges", cluster.GetAllNodes().Count(), cluster.GetAllEdges().Count()),
                    verbose);
                foreach (var pair in cluster.GetNodePairs())
                {
                    if (checkIfPairIsConstrained(constrainedDemands, pair))
                    {
                        continue;
                    }
                    if (!randomInitialization)
                    {
                        solver.ChangeConstraintRHS(pairNameToConstraintMapping[pair], demandUB);
                    }
                    else
                    {
                        solver.RemoveConstraint(pairNameToConstraintMapping[pair]);
                    }
                    consideredPairs.Add(pair);
                }
                Utils.logger("setting the objective.", verbose);
                var objective = new Polynomial<TVar>(
                            new Term<TVar>(1, optimalEncoding.GlobalObjective),
                            new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                var solution = solver.Maximize(objective, reset: true);
                var optimalSolution = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                var heuristicSolution = (TEOptimizationSolution)heuristicEncoder.GetSolution(solution);
                foreach (var pair in consideredPairs)
                {
                    var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optimalSolution.Demands[pair]);
                    demandMatrix[pair] = demandlvl;
                    AddSingleDemandEquality(solver, pair, demandlvl);
                    // AddSingleDemandUB(solver, pair, demandMatrix[pair]);
                }

                if (verbose)
                {
                    var numPairs = 0.0;
                    var numNonZeroDemands = 0.0;
                    foreach (var pair in cluster.GetNodePairs())
                    {
                        var demand = demandMatrix[pair];
                        if (demand > 0)
                        {
                            numNonZeroDemands += 1;
                        }
                        numPairs += 1;
                    }
                    Utils.logger(
                        string.Format("fraction of non zero demands {0}", numNonZeroDemands / numPairs),
                        verbose);
                }
            }

            for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
            {
                for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                {
                    var consideredPairs = new HashSet<(string, string)>();
                    Utils.logger(
                        string.Format("inter-cluster adversarial demand between cluster {0} and cluster {1}", cid1, cid2),
                        verbose);
                    var cluster1Nodes = clusters[cid1].GetAllNodes().ToList();
                    var cluster2Nodes = clusters[cid2].GetAllNodes().ToList();
                    bool neighbor = false;
                    foreach (var node1 in cluster1Nodes)
                    {
                        foreach (var node2 in cluster2Nodes)
                        {
                            if (this.Topology.ContaintsEdge(node1, node2))
                            {
                                neighbor = true;
                                break;
                            }
                        }
                        if (neighbor)
                        {
                            break;
                        }
                    }
                    if (!neighbor)
                    {
                        Utils.logger("skipping the cluster pairs since they are not neighbors", verbose);
                        continue;
                    }
                    foreach (var node1 in cluster1Nodes)
                    {
                        foreach (var node2 in cluster2Nodes)
                        {
                            if (checkIfPairIsConstrained(constrainedDemands, (node1, node2)))
                            {
                                continue;
                            }
                            if (!randomInitialization)
                            {
                                solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], demandUB);
                            }
                            else
                            {
                                solver.RemoveConstraint(pairNameToConstraintMapping[(node1, node2)]);
                            }
                            consideredPairs.Add((node1, node2));

                            if (checkIfPairIsConstrained(constrainedDemands, (node2, node1)))
                            {
                                continue;
                            }
                            if (!randomInitialization)
                            {
                                solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node2, node1)], demandUB);
                            }
                            else
                            {
                                solver.RemoveConstraint(pairNameToConstraintMapping[(node1, node2)]);
                            }
                            consideredPairs.Add((node2, node1));
                        }
                    }
                    Utils.logger("setting the objective.", verbose);
                    var objective = new Polynomial<TVar>(
                                new Term<TVar>(1, optimalEncoding.GlobalObjective),
                                new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    var optimalSolution = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    var heuristicSolution = (TEOptimizationSolution)heuristicEncoder.GetSolution(solution);
                    foreach (var pair in consideredPairs)
                    {
                        var demandlvl = DiscoverMatchingDemandLvl(this.DemandEnforcers[pair], optimalSolution.Demands[pair]);
                        demandMatrix[pair] = demandlvl;
                        AddSingleDemandEquality(solver, pair, demandlvl);
                        // AddSingleDemandUB(solver, pair, demandMatrix[pair]);
                    }
                }
            }

            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!demandMatrix.ContainsKey(pair))
                {
                    demandMatrix[pair] = 0;
                }
            }
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix, innerEncoding, demandList);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// First Optimizes all the clusters. Then, uses random sampling for.
        /// inter-cluster traffic.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) MaximizeOptimalityGapWithClusteringV1(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericDemandList demandList = null,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false)
        {
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters)
            {
                foreach (var node in cluster.GetAllNodes())
                {
                    if (seenNode.Contains(node))
                    {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count())
            {
                throw new Exception("missmatch between number of nodes in original problem and clustered version");
            }

            if (constrainedDemands != null)
            {
                throw new Exception("the constrained demand option is not implemented yet!!!");
            }

            var demandMatrix = new Dictionary<(string, string), double>();
            foreach (var cluster in clusters)
            {
                optimalEncoder.Solver.CleanAll();
                Utils.logger("Cluster with " + cluster.GetAllNodes().Count() + " nodes and " + cluster.GetAllEdges().Count() + " edges", verbose);
                var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSolution>(cluster, this.maxNumPath, this.NumProcesses);
                var clusterResult = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB, innerEncoding, demandList: demandList,
                        simplify: simplify, verbose: verbose);
                foreach (var pair in cluster.GetNodePairs())
                {
                    if (demandMatrix.ContainsKey(pair))
                    {
                        throw new Exception("cluster are not independepnt");
                    }
                    demandMatrix[pair] = clusterResult.Item1.Demands[pair];
                }
            }

            if (numInterClusterSamples > 0)
            {
                Debug.Assert(numNodePerCluster > 0);
                if (verbose)
                {
                    var preDemandMatrix = new Dictionary<(string, string), double>();
                    foreach (var pair in this.Topology.GetNodePairs())
                    {
                        if (demandMatrix.ContainsKey(pair))
                        {
                            preDemandMatrix[pair] = demandMatrix[pair];
                        }
                        else
                        {
                            preDemandMatrix[pair] = 0;
                        }
                    }
                    var preInterCluster = GetGap(optimalEncoder, heuristicEncoder, preDemandMatrix, innerEncoding, demandList);
                    Utils.logger("pre inter-cluster gap: " + preInterCluster.Item1, verbose);
                }
                var solver = optimalEncoder.Solver;
                solver.CleanAll();
                Utils.logger("creating demand variables.", verbose);
                (this.DemandEnforcers, this.LocalityConstrainedDemands) = CreateDemandVariables(solver, innerEncoding, demandList);
                Utils.logger("generating optimal encoding.", verbose);
                var optimalEncoding = optimalEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                                        inputEqualityConstraints: demandMatrix, innerEncoding: innerEncoding,
                                        numProcesses: this.NumProcesses, verbose: verbose, noAdditionalConstraints: true);
                Utils.logger("generating heuristic encoding.", verbose);
                var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, preInputVariables: this.DemandEnforcers,
                                        inputEqualityConstraints: demandMatrix, innerEncoding: innerEncoding,
                                        numProcesses: this.NumProcesses, verbose: verbose);

                // ensures that demand in both problems is the same and lower than demand upper bound constraint.
                Utils.logger("adding constraints for upper bound on demands.", verbose);
                EnsureDemandUB(solver, demandUB);
                Utils.logger("adding equality constraints for specified demands.", verbose);
                EnsureDemandEquality(solver, constrainedDemands);

                var pairNameToConstraintMapping = new Dictionary<(string, string), string>();
                foreach (var (pair, demandVar) in this.DemandEnforcers)
                {
                    if (demandMatrix.ContainsKey(pair))
                    {
                        continue;
                    }
                    var constrName = solver.AddLeqZeroConstraint(demandVar);
                    pairNameToConstraintMapping[pair] = constrName;
                }

                // Console.WriteLine("adding eq = 0 for {0}", string.Join(",", pairNameToConstraintMapping.Keys));
                // var objectiveVariable = solver.CreateVariable("objective");
                var rng = new Random();
                for (int l = 0; l < numInterClusterSamples; l++)
                {
                    Utils.logger(
                        string.Format("trying the {0}-th set of inter-cluster nodes each of size {1}", l, numNodePerCluster), verbose);
                    solver.ModelUpdate();
                    var interClusterNodes = new List<List<string>>();
                    foreach (var cluster in clusters)
                    {
                        var nodeNames = cluster.GetAllNodes().ToList();
                        var repNodes = new List<string>();
                        for (int i = 0; i < numNodePerCluster; i++)
                        {
                            var idx = rng.Next(nodeNames.Count());
                            repNodes.Add(nodeNames[idx]);
                        }
                        Console.WriteLine(String.Format("cluster rep nodes; {0}", string.Join("_", repNodes)));
                        interClusterNodes.Add(repNodes);
                    }

                    if (demandUB < 0)
                    {
                        demandUB = this.maxNumPath * this.Topology.MaxCapacity();
                    }

                    var currPairs = new HashSet<(string, string)>();
                    for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
                    {
                        for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                        {
                            var cluster1Nodes = interClusterNodes[cid1];
                            var cluster2Nodes = interClusterNodes[cid2];
                            foreach (var node1 in cluster1Nodes)
                            {
                                foreach (var node2 in cluster2Nodes)
                                {
                                    // Console.WriteLine(string.Format("node 1 {0} cluster {1} node 2 {2} cluster {3}",
                                    //         node1, cid1, node2, cid2));
                                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node1, node2)], demandUB);
                                    solver.ChangeConstraintRHS(pairNameToConstraintMapping[(node2, node1)], demandUB);
                                    currPairs.Add((node1, node2));
                                    currPairs.Add((node2, node1));
                                }
                            }
                        }
                    }

                    Utils.logger("setting the objective.", verbose);
                    var objective = new Polynomial<TVar>(
                                new Term<TVar>(1, optimalEncoding.GlobalObjective),
                                new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
                    var solution = solver.Maximize(objective, reset: true);
                    var optimalSolution = (TEOptimizationSolution)optimalEncoder.GetSolution(solution);
                    foreach (var pair in this.Topology.GetNodePairs())
                    {
                        if (demandMatrix.ContainsKey(pair))
                        {
                            // Console.WriteLine(demandMatrix[pair].ToString() + " " + optimalSolution.Demands[pair].ToString());
                            if (optimalSolution.Demands.ContainsKey(pair))
                            {
                                Debug.Assert(Math.Abs(demandMatrix[pair] - optimalSolution.Demands[pair]) <= 0.001);
                            }
                            else
                            {
                                Debug.Assert(demandMatrix[pair] <= 0.001);
                            }
                        }
                        else if (currPairs.Contains(pair))
                        {
                            demandMatrix[pair] = optimalSolution.Demands[pair];
                            var ratePoly = this.DemandEnforcers[pair].Copy();
                            ratePoly.Add(new Term<TVar>(-1 * demandMatrix[pair]));
                            solver.AddEqZeroConstraint(ratePoly);
                            // Utils.logger(
                            //     string.Format("demand from {0} to {1} is: {2}", pair.Item1, pair.Item2, demandMatrix[pair]),
                            //     verbose);
                        }
                    }
                }
            }

            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!demandMatrix.ContainsKey(pair))
                {
                    demandMatrix[pair] = 0;
                }
            }

            var completeOpt = new TEAdversarialInputGenerator<TVar, TSolution>(this.Topology, this.maxNumPath, this.NumProcesses);
            optimalEncoder.Solver.CleanAll(timeout: double.PositiveInfinity);
            completeOpt.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB, innerEncoding, demandList, constrainedDemands, simplify,
                    verbose, demandInits: demandMatrix);
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix, innerEncoding, demandList);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Maximize optimality gap with clustering method used for scale up.
        /// First optimizes each cluster. Then, finds the optimal input for the
        /// inter-cluster traffic on abstracted topology and randomly assings the
        /// flows to each cluster.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) MaximizeOptimalityGapWithClusteringV3(
            List<Topology> clusters,
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1,
            int numInterClusterSamples = 0,
            int numNodePerCluster = 0,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericDemandList demandList = null,
            int numInterClusterQuantizations = -1,
            IDictionary<(string, string), double> constrainedDemands = null,
            bool simplify = false,
            bool verbose = false)
        {
            var seenNode = new HashSet<string>();
            foreach (var cluster in clusters)
            {
                foreach (var node in cluster.GetAllNodes())
                {
                    if (seenNode.Contains(node))
                    {
                        throw new Exception("duplicate nodes over two clusters");
                    }
                    seenNode.Add(node);
                }
            }
            if (seenNode.Count() != this.Topology.GetAllNodes().Count())
            {
                throw new Exception("missmatch between number of nodes in original problem and clustered version");
            }

            if (constrainedDemands != null)
            {
                throw new Exception("the constrained demand option is not implemented yet!!!");
            }

            var demandMatrix = new Dictionary<(string, string), double>();
            foreach (var cluster in clusters)
            {
                optimalEncoder.Solver.CleanAll();
                Utils.logger("Cluster with " + cluster.GetAllNodes().Count() + " nodes and " + cluster.GetAllEdges().Count() + " edges", verbose);
                var clusterAdversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSolution>(cluster, this.maxNumPath, this.NumProcesses);
                var clusterResult = clusterAdversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder, demandUB, innerEncoding,
                        demandList: demandList, simplify: simplify, verbose: verbose);
                foreach (var pair in cluster.GetNodePairs())
                {
                    if (demandMatrix.ContainsKey(pair))
                    {
                        throw new Exception("cluster are not independepnt");
                    }
                    demandMatrix[pair] = clusterResult.Item1.Demands[pair];
                }
            }

            if (verbose)
            {
                var preDemandMatrix = new Dictionary<(string, string), double>();
                foreach (var pair in this.Topology.GetNodePairs())
                {
                    if (demandMatrix.ContainsKey(pair))
                    {
                        preDemandMatrix[pair] = demandMatrix[pair];
                    }
                    else
                    {
                        preDemandMatrix[pair] = 0;
                    }
                }
                var preInterCluster = GetGap(optimalEncoder, heuristicEncoder, preDemandMatrix, innerEncoding, demandList);
                Utils.logger("pre inter-cluster gap: " + preInterCluster.Item1, verbose);
            }

            Utils.logger("starting to model abstracted topology", verbose);
            var abstractTopology = new Topology();
            var clusterToNoteID = new Dictionary<Topology, string>();
            var clusterIDToCluster = new Dictionary<string, Topology>();
            var nodeID = 0;
            var clusterNumNodeList = new List<int>();
            Utils.logger("creating abstracted topology", verbose);
            foreach (var cluster in clusters)
            {
                abstractTopology.AddNode(nodeID.ToString());
                clusterToNoteID[cluster] = nodeID.ToString();
                clusterIDToCluster[nodeID.ToString()] = cluster;
                clusterNumNodeList.Add(cluster.GetAllNodes().Count());
                nodeID++;
            }

            var edgeToCapacity = new Dictionary<(string, string), double>();
            foreach (var edge in this.Topology.GetAllEdges())
            {
                edgeToCapacity[(edge.Source, edge.Target)] = edge.Capacity;
            }

            var pairToDemandUB = new Dictionary<(string, string), double>();
            for (var cid1 = 0; cid1 < clusters.Count() - 1; cid1++)
            {
                for (var cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                {
                    var cluster1 = clusters[cid1];
                    var cluster2 = clusters[cid2];
                    var cluster1Nodes = cluster1.GetAllNodes();
                    var cluster2Nodes = cluster2.GetAllNodes();
                    var nodeID1 = clusterToNoteID[cluster1];
                    var nodeID2 = clusterToNoteID[cluster2];
                    var cap1To2 = 0.0;
                    var cap2To1 = 0.0;
                    foreach (var node1 in cluster1Nodes)
                    {
                        foreach (var node2 in cluster2Nodes)
                        {
                            if (edgeToCapacity.ContainsKey((node1, node2)))
                            {
                                cap1To2 += edgeToCapacity[(node1, node2)];
                            }
                            if (edgeToCapacity.ContainsKey((node2, node1)))
                            {
                                cap2To1 += edgeToCapacity[(node2, node1)];
                            }
                        }
                    }
                    if (cap1To2 > 0)
                    {
                        abstractTopology.AddEdge(nodeID1, nodeID2, cap1To2);
                        pairToDemandUB[(nodeID1, nodeID2)] = cluster1Nodes.Count() * cluster2Nodes.Count();
                        Utils.logger(
                            string.Format("abstract topology edge from {0} to {1} with cap {2}", nodeID1, nodeID2, cap1To2),
                            verbose);
                    }
                    if (cap2To1 > 0)
                    {
                        abstractTopology.AddEdge(nodeID2, nodeID1, cap2To1);
                        pairToDemandUB[(nodeID2, nodeID1)] = cluster1Nodes.Count() * cluster2Nodes.Count();
                        Utils.logger(
                            string.Format("abstract topology edge from {1} to {0} with cap {2}", nodeID1, nodeID2, cap2To1),
                            verbose);
                    }
                }
            }

            var clusterPairToNumNodePairs = new Dictionary<(Topology, Topology), int>();
            for (int cid1 = 0; cid1 < clusters.Count(); cid1++)
            {
                for (int cid2 = cid1 + 1; cid2 < clusters.Count(); cid2++)
                {
                    int numPairs = clusters[cid1].GetAllNodes().Count() * clusters[cid2].GetAllNodes().Count();
                    clusterPairToNumNodePairs[(clusters[cid1], clusters[cid2])] = numPairs;
                    clusterPairToNumNodePairs[(clusters[cid2], clusters[cid1])] = numPairs;
                }
            }

            Utils.logger("computing updated demand list", verbose);
            var demandlvls = demandList.demandList;
            demandlvls.Add(0);
            var abstractDemandList = new Dictionary<(string, string), ISet<double>>();
            foreach (var ((cluster1, cluster2), numPairs) in clusterPairToNumNodePairs)
            {
                var perClusterPairAbstractDemandList = new HashSet<double>(demandlvls);
                for (int num1 = 1; num1 < numPairs; num1++)
                {
                    var newDemandsToAdd = new HashSet<double>();
                    foreach (var demand1 in demandlvls)
                    {
                        foreach (var demand2 in perClusterPairAbstractDemandList)
                        {
                            newDemandsToAdd.Add(demand1 + demand2);
                        }
                    }
                    perClusterPairAbstractDemandList = perClusterPairAbstractDemandList.Union(newDemandsToAdd).ToHashSet();
                }

                HashSet<double> finalClusterPairAbstractDemandList = null;
                if (numInterClusterQuantizations > 0)
                {
                    finalClusterPairAbstractDemandList = new HashSet<double>();
                    var numAggDemandlvls = perClusterPairAbstractDemandList.Count();
                    var perClusterDemandlvls = perClusterPairAbstractDemandList.ToList();
                    var alpha = Math.Pow(numAggDemandlvls, 1.0 / numInterClusterQuantizations);
                    for (int q = 0; q < numInterClusterQuantizations + 1; q++)
                    {
                        finalClusterPairAbstractDemandList.Add(perClusterDemandlvls[Convert.ToInt32(Math.Pow(alpha, q))]);
                    }
                }
                else
                {
                    finalClusterPairAbstractDemandList = perClusterPairAbstractDemandList;
                }
                abstractDemandList[(clusterToNoteID[cluster1], clusterToNoteID[cluster2])] = finalClusterPairAbstractDemandList;
                Utils.logger(
                    string.Format(
                        "new demand list between abstract node {0} and {1} = {2}",
                        clusterToNoteID[cluster1],
                        clusterToNoteID[cluster2],
                        string.Join("_", finalClusterPairAbstractDemandList)),
                    verbose);
            }

            Utils.logger("Abstract topology with " + abstractTopology.GetAllNodes().Count() +
                    " nodes and " + abstractTopology.GetAllEdges().Count() + " edges", verbose);
            optimalEncoder.Solver.CleanAll();
            var adversarialInputGenerator = new TEAdversarialInputGenerator<TVar, TSolution>(abstractTopology, this.maxNumPath, this.NumProcesses);
            var abstractResult = adversarialInputGenerator.MaximizeOptimalityGap(optimalEncoder, heuristicEncoder,
                    innerEncoding: innerEncoding, demandList: new PairwiseDemandList(abstractDemandList),
                    simplify: simplify, verbose: verbose, cleanUpSolver: false, perDemandUB: pairToDemandUB);

            Utils.logger("Assigning Demands randomly...", verbose);
            if (demandUB < 0)
            {
                demandUB = this.maxNumPath * this.Topology.MaxCapacity();
            }

            var rng = new Random();
            foreach (var pair in abstractTopology.GetNodePairs())
            {
                var nodeCluster1 = clusterIDToCluster[pair.Item1].GetAllNodes().ToList();
                var nodeCluster2 = clusterIDToCluster[pair.Item2].GetAllNodes().ToList();
                var remDemand = abstractResult.Item1.Demands[pair];
                Utils.logger(
                    string.Format("demand from {0} to {1} in abstract topo = {2}", pair.Item1, pair.Item2, remDemand),
                    verbose);
                while (remDemand > 0.001)
                {
                    var node1 = nodeCluster1[rng.Next(nodeCluster1.Count())];
                    var node2 = nodeCluster2[rng.Next(nodeCluster2.Count())];
                    var demand = demandList.GetRandomNonZeroDemandForPair(rng, node1, node2);
                    if (demandMatrix.ContainsKey((node1, node2)) || remDemand < demand)
                    {
                        continue;
                    }
                    demandMatrix[(node1, node2)] = demand;
                    Utils.logger(
                        string.Format("adding demand from {0} to {1} = {2}", node1, node2, demand),
                        verbose);
                    remDemand -= demand;
                }
            }

            foreach (var pair in this.Topology.GetNodePairs())
            {
                if (!demandMatrix.ContainsKey(pair))
                {
                    demandMatrix[pair] = 0;
                }
            }
            var output = GetGap(optimalEncoder, heuristicEncoder, demandMatrix, innerEncoding, demandList);
            Utils.logger("Final gap: " + output.Item1, verbose);
            return output.Item2;
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        /// <param name="minDifference">The minimum difference.</param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        /// <param name="innerEncoding">The method for encoding the inner problem.</param>
        /// <param name="demandList">the quantized list of demands, will only use if method=PrimalDual.</param>
        /// <param name="simplify">will simplify the final solution if this parameter is true.</param>,
        public (TEOptimizationSolution, TEOptimizationSolution) FindOptimalityGapAtLeast(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double minDifference,
            double demandUB = -1,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericDemandList demandList = null,
            bool simplify = false)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }
            if (innerEncoding == InnerRewriteMethodChoice.PrimalDual & demandList == null)
            {
                throw new Exception("should provide the demand list if inner encoding method is primal dual.");
            }
            var solver = optimalEncoder.Solver;
            solver.CleanAll();

            (this.DemandEnforcers, this.LocalityConstrainedDemands) = CreateDemandVariables(solver, innerEncoding, demandList);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, this.DemandEnforcers, numProcesses: this.NumProcesses, noAdditionalConstraints: true);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, this.DemandEnforcers, numProcesses: this.NumProcesses);

            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            EnsureDemandUB(solver, demandUB);

            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            solver.SetObjective(objective);

            // solver.AddLeqZeroConstraint(new Polynomial<TVar>(
            // new Term<TVar>(-1, objectiveVariable), new Term<TVar>(minDifference)));

            // var solution = solver.Maximize(solver.CreateVariable("dummy"));
            var solution = solver.CheckFeasibility(minDifference);
            solution = SimplifyAdversarialInputs(simplify, optimalEncoder, heuristicEncoder, solution, objective);
            return ((TEOptimizationSolution)optimalEncoder.GetSolution(solution),
                    (TEOptimizationSolution)heuristicEncoder.GetSolution(solution));
            /* if (!solution.IsSatisfiable())
            {
                Console.WriteLine($"No solution found!");
                Environment.Exit(1);
            }

            Console.WriteLine("Optimal (Opt):");
            optimalEncoder.DisplaySolution(solution);
            Console.WriteLine();
            Console.WriteLine("Optimal (Pop):");
            heuristicEncoder.DisplaySolution(solution); */
        }

        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            double demandUB)
        {
            if (demandUB < 0)
            {
                demandUB = double.PositiveInfinity;
            }
            demandUB = Math.Min(this.Topology.MaxCapacity() * this.maxNumPath, demandUB);
            foreach (var (pair, variable) in this.DemandEnforcers)
            {
                if (this.LocalityConstrainedDemands.ContainsKey(pair))
                {
                    if (this.LocalityConstrainedDemands[pair] > demandUB)
                    {
                        throw new Exception("the locality based constrain and the demand upper bound are in conflict.");
                    }
                }
                // var heuristicVariable = heuristicEncoding.DemandVariables[pair];
                // solver.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, heuristicVariable)));
                var poly = variable.Copy();
                poly.Add(new Term<TVar>(-1 * demandUB));
                solver.AddLeqZeroConstraint(poly);
                // solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1 * demandUB), new Term<TVar>(1, heuristicVariable)));
            }
        }

        // TODO: I think somewhere I asked about the Zen requirement that we have to upper bound the demand
        // variables. this addresses that comment, we should remove the commented out code in the other place
        // and leave a comment that describes what is happening here.
        private void EnsureDemandUB(
            ISolver<TVar, TSolution> solver,
            IDictionary<(string, string), double> demandUB)
        {
            foreach (var (pair, perDemandUb) in demandUB)
            {
                var ub = perDemandUb;
                if (ub < 0)
                {
                    ub = double.PositiveInfinity;
                }
                if (this.LocalityConstrainedDemands.ContainsKey(pair))
                {
                    if (this.LocalityConstrainedDemands[pair] > demandUB[pair])
                    {
                        throw new Exception("the locality based constrain and the demand upper bound are in conflict.");
                    }
                }
                ub = Math.Min(this.Topology.MaxCapacity() * this.maxNumPath, ub);
                var poly = DemandEnforcers[pair].Copy();
                poly.Add(new Term<TVar>(-1 * ub));
                solver.AddLeqZeroConstraint(poly);
            }
        }

        // TODO: a question here: so shouldn't this normalize the demand in some way so that the quantized levels are able to reach that value?
        // or in another way doesn't this just dictate the value for certain variables?
        private void AddSingleDemandEquality(
            ISolver<TVar, TSolution> solver,
            (string, string) pair,
            double demand)
        {
            var poly = this.DemandEnforcers[pair].Copy();
            poly.Add(new Term<TVar>(-1 * demand));
            solver.AddEqZeroConstraint(poly);
        }

        private void AddSingleDemandUB(
            ISolver<TVar, TSolution> solver,
            (string, string) pair,
            double demand)
        {
            var poly = this.DemandEnforcers[pair].Copy();
            poly.Add(new Term<TVar>(-1 * demand));
            solver.AddLeqZeroConstraint(poly);
        }

        // TODO: needs a comment describing what it does.
        private void EnsureDemandEquality(
            ISolver<TVar, TSolution> solver,
            IDictionary<(string, string), double> constrainedDemands)
        {
            if (constrainedDemands == null)
            {
                return;
            }
            foreach (var (pair, demand) in constrainedDemands)
            {
                if (this.LocalityConstrainedDemands.ContainsKey(pair))
                {
                    if (this.LocalityConstrainedDemands[pair] != constrainedDemands[pair])
                    {
                        throw new Exception("the constrained demand does not satisfy the locality imposed constraint.");
                    }
                }
                AddSingleDemandEquality(solver, pair, demand);
            }
        }

        // TODO: need a comment that describes what this function does.
        // TODO: seems like it just controls how many demand variables can be non-zero.
        private void EnsureDensityConstraint(
            ISolver<TVar, TSolution> solver,
            double density)
        {
            if (density < 0 || density >= 1.0 - 0.0001)
            {
                return;
            }
            var densityConstraint = new Polynomial<TVar>();
            foreach (var (pair, demandPoly) in this.DemandEnforcers)
            {
                foreach (var term in demandPoly.GetTerms())
                {
                    var variable = term.Variable.Value;
                    densityConstraint.Add(new Term<TVar>(1, variable));
                }
            }
            densityConstraint.Add(new Term<TVar>(-1 * this.Topology.GetNodePairs().Count() * density));
            solver.AddLeqZeroConstraint(densityConstraint);
        }

        // TODO: this function requires a comment.
        // Is demandList the list of quantization levels for the demand variable? if yes, we need to rename it to something tht is more clear.
        private (Dictionary<(string, string), Polynomial<TVar>>, Dictionary<(string, string), double>) CreateDemandVariables(
                ISolver<TVar, TSolution> solver,
                InnerRewriteMethodChoice innerEncoding,
                IDemandList demandList,
                IDictionary<(string, string), double> demandInits = null,
                double LargeDemandLB = -1,
                int LargeMaxDistance = -1,
                int SmallMaxDistance = -1)
        {
            if (LargeMaxDistance != -1)
            {
                Debug.Assert(LargeMaxDistance >= 1);
                Debug.Assert(LargeDemandLB > 0);
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
            }
            else
            {
                LargeMaxDistance = int.MaxValue;
            }

            if (SmallMaxDistance != -1)
            {
                Debug.Assert(SmallMaxDistance >= 1);
                Debug.Assert(LargeDemandLB > 0);
                Debug.Assert(innerEncoding == InnerRewriteMethodChoice.PrimalDual);
            }
            else
            {
                SmallMaxDistance = int.MaxValue;
            }

            // TODO: when we do the re-factor you and i should agree on a naming convention: should we use all upper case or all lower case or...?
            var demandEnforcers = new Dictionary<(string, string), Polynomial<TVar>>();
            var LocalityConstrainedDemands = new Dictionary<(string, string), double>();

            // TODO: add a debug, info and other logging levels. dont use things like this.
            Console.WriteLine("[INFO] In total " + this.Topology.GetNodePairs().Count() + " pairs");

            foreach (var pair in this.Topology.GetNodePairs())
            {
                switch (innerEncoding)
                {
                    case InnerRewriteMethodChoice.KKT:
                        demandEnforcers[pair] = new Polynomial<TVar>(new Term<TVar>(1, solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2)));
                        break;
                    case InnerRewriteMethodChoice.PrimalDual:
                        // get demands lvls
                        var demands = demandList.GetDemandsForPair(pair.Item1, pair.Item2);

                        // TODO: why do you remove the first item in the list?
                        demands.Remove(0);
                        // get distance
                        var distance = this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2)[0].Length - 1;
                        // create demand variables
                        var axVariableConstraint = new Polynomial<TVar>(new Term<TVar>(-1));
                        var demandLvlEnforcer = new Polynomial<TVar>();
                        bool found = false;
                        bool atLeastOneValidLvl = false;
                        foreach (double demandlvl in demands)
                        {
                            // Skip if the distance between the pair is larger than what we allow or
                            // if the demand level is larger than the large demand lower bound.
                            if (distance > LargeMaxDistance && demandlvl >= LargeDemandLB)
                            {
                                // Console.WriteLine("===== skipping " + pair.Item1 + " " + pair.Item2 + " " + distance + " " + demandlvl);
                                continue;
                            }
                            if (distance > SmallMaxDistance && demandlvl < LargeDemandLB)
                            {
                                continue;
                            }

                            atLeastOneValidLvl = true;
                            var demandbinaryAuxVar = solver.CreateVariable("aux_demand_" + pair.Item1 + "_" + pair.Item2, type: GRB.BINARY);
                            demandLvlEnforcer.Add(new Term<TVar>(demandlvl, demandbinaryAuxVar));
                            axVariableConstraint.Add(new Term<TVar>(1, demandbinaryAuxVar));
                            if (demandInits != null)
                            {
                                if (Math.Abs(demandInits[pair] - demandlvl) <= 0.0001)
                                {
                                    solver.InitializeVariables(demandbinaryAuxVar, 1);
                                    found = true;
                                }
                                else
                                {
                                    solver.InitializeVariables(demandbinaryAuxVar, 0);
                                }
                            }
                            // sumAllAuxVars.Add(new Term<TVar>(1, demandAuxVar));
                        }
                        if (demandInits != null)
                        {
                            Debug.Assert(found == true || Math.Abs(demandInits[pair]) <= 0.0001);
                        }
                        if (atLeastOneValidLvl)
                        {
                            solver.AddLeqZeroConstraint(axVariableConstraint);
                        }
                        else
                        {
                            LocalityConstrainedDemands[pair] = 0.0;
                        }
                        demandEnforcers[pair] = demandLvlEnforcer;
                        break;
                    default:
                        throw new Exception("wrong method for inner problem encoder!");
                }
            }
            // solver.AddLeqZeroConstraint(sumAllAuxVars);
            return (demandEnforcers, LocalityConstrainedDemands);
        }

        /// <summary>
        /// Finds an adversarial input that is within intervalConf of the maximum gap.
        /// </summary>
        /// <param name="optimalEncoder"> </param>
        /// <param name="heuristicEncoder"> </param>
        /// <param name="intervalConf"></param>
        /// <param name="startGap"></param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        /// <param name="innerEncoding">The method for encoding the inner problem.</param>
        /// <param name="demandList">the quantized list of demands, will only use if method=PrimalDual.</param>
        public (TEOptimizationSolution, TEOptimizationSolution) FindMaximumGapInterval(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double intervalConf,
            double startGap,
            double demandUB = double.PositiveInfinity,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            GenericDemandList demandList = null)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("[ERROR] Solver mismatch....");
            }

            if (startGap <= 0.001)
            {
                throw new System.Exception("[ERROR] Starting Gap too small...");
            }
            var solver = optimalEncoder.Solver;
            solver.CleanAll();

            (this.DemandEnforcers, this.LocalityConstrainedDemands) = CreateDemandVariables(solver, innerEncoding, demandList);
            var optimalEncoding = optimalEncoder.Encoding(this.Topology, this.DemandEnforcers, numProcesses: this.NumProcesses, noAdditionalConstraints: true);
            var heuristicEncoding = heuristicEncoder.Encoding(this.Topology, this.DemandEnforcers, numProcesses: this.NumProcesses);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            EnsureDemandUB(solver, demandUB);

            // setting demand as objective
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            solver.SetObjective(objective);

            double lbGap = 0;
            double ubGap = startGap;
            bool found_infeas = false;
            TSolution solution;
            while (!found_infeas)
            {
                Console.WriteLine("************** Current Gap Interval (Phase 1) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("nxt=" + ubGap);
                Console.WriteLine("**************************************************");
                try
                {
                    solution = solver.CheckFeasibility(ubGap);
                    lbGap = ubGap;
                    ubGap = ubGap * 2;
                }
                catch (InfeasibleOrUnboundSolution)
                {
                    found_infeas = true;
                }
                // solver.ChangeConstraintRHS(nameLBConst, -1 * ubGap);
            }

            while ((ubGap - lbGap) / lbGap > intervalConf)
            {
                double midGap = (lbGap + ubGap) / 2;
                Console.WriteLine("************** Current Gap Interval (Phase 2) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("ub=" + ubGap);
                Console.WriteLine("nxt=" + midGap);
                Console.WriteLine("**************************************************");
                // solver.ChangeConstraintRHS(nameLBConst, -1 * midGap);
                try
                {
                    solution = solver.CheckFeasibility(midGap);
                    lbGap = midGap;
                }
                catch (InfeasibleOrUnboundSolution)
                {
                    ubGap = midGap;
                }
            }
            Console.WriteLine("************** Final Gap Interval ****************");
            Console.WriteLine("lb=" + lbGap);
            Console.WriteLine("ub=" + ubGap);
            Console.WriteLine("**************************************************");
            // solver.ChangeConstraintRHS(nameLBConst, -1 * lbGap);
            solution = solver.CheckFeasibility(lbGap);
            return ((TEOptimizationSolution)optimalEncoder.GetSolution(solution),
                    (TEOptimizationSolution)heuristicEncoder.GetSolution(solution));
        }

        private (double, (TEOptimizationSolution, TEOptimizationSolution)) GetGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            Dictionary<(string, string), double> demands,
            InnerRewriteMethodChoice innerEncoding = InnerRewriteMethodChoice.KKT,
            IDemandList demandList = null,
            bool disableStoreProgress = false)
        {
            // solving the hueristic for the demand
            heuristicEncoder.Solver.CleanAll(disableStoreProgress: disableStoreProgress);
            var (demandVariables, _) = CreateDemandVariables(heuristicEncoder.Solver, innerEncoding, demandList);
            var encodingHeuristic = heuristicEncoder.Encoding(this.Topology, inputEqualityConstraints: demands,
                    noAdditionalConstraints: true, numProcesses: this.NumProcesses, preInputVariables: demandVariables);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
            var optimizationSolutionHeuristic = (TEOptimizationSolution)heuristicEncoder.GetSolution(solverSolutionHeuristic);

            // solving the optimal for the demand
            optimalEncoder.Solver.CleanAll(disableStoreProgress: disableStoreProgress);
            (demandVariables, _) = CreateDemandVariables(optimalEncoder.Solver, innerEncoding, demandList);
            var encodingOptimal = optimalEncoder.Encoding(this.Topology, inputEqualityConstraints: demands,
                    noAdditionalConstraints: true, numProcesses: this.NumProcesses, preInputVariables: demandVariables);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(encodingOptimal.MaximizationObjective);
            var optimizationSolutionOptimal = (TEOptimizationSolution)optimalEncoder.GetSolution(solverSolutionOptimal);
            double currGap = optimizationSolutionOptimal.MaxObjective - optimizationSolutionHeuristic.MaxObjective;
            return (currGap, (optimizationSolutionOptimal, optimizationSolutionHeuristic));
        }

        /// <summary>
        /// Generate some random inputs and takes the max gap as the adversary.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) RandomAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            double demandUB,
            bool grey = false,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            // if (optimalEncoder.Solver == heuristicEncoder.Solver) {
            //     throw new Exception("solvers should be different for random generator!!!");
            // }
            if (numTrials < 1)
            {
                throw new Exception("num trials for random generator should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0)
            {
                demandUB = this.Topology.MaxCapacity() * this.maxNumPath;
            }
            if (storeProgress)
            {
                if (logPath == null)
                {
                    throw new Exception("should specify logPath if storeprogress = true!");
                }
                else
                {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }
            double currMaxGap = 0;
            TEOptimizationSolution zero_solution = new TEOptimizationSolution
            {
                MaxObjective = 0,
                Demands = new Dictionary<(string, string), double> { },
                // Flows = new Dictionary<(string, string), double> { },
                FlowsPaths = new Dictionary<string[], double> { },
            };
            (TEOptimizationSolution, TEOptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);
            double timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            // Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            foreach (int i in Enumerable.Range(0, numTrials))
            {
                // initializing some random demands
                Dictionary<(string, string), double> demands = getRandomDemand(rng, demandUB);
                // finding the gap
                double currGap = 0;
                (TEOptimizationSolution, TEOptimizationSolution) result = (zero_solution, zero_solution);
                bool feasible = true;
                do
                {
                    feasible = true;
                    try
                    {
                        (currGap, result) = GetGap(optimalEncoder, heuristicEncoder, demands);
                    }
                    catch (DemandPinningLinkNegativeException e)
                    {
                        feasible = false;
                        Console.WriteLine("Infeasible input!");
                        if (grey)
                        {
                            ReduceDemandsOnLink(demands, e.Edge, e.Threshold, 0.1);
                        }
                        else
                        {
                            demands = getRandomDemand(rng, demandUB);
                        }
                        Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                    }
                    if (timer.ElapsedMilliseconds > timeout_ms)
                    {
                        break;
                    }
                } while (!feasible);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("======== try " + i + " found a solution with gap " + currGap, verbose);
                if (currGap > currMaxGap)
                {
                    Utils.WriteToConsole("updating the max gap from " + currMaxGap + " to " + currGap, verbose);
                    currMaxGap = currGap;
                    worstResult = result;
                }
                else
                {
                    Utils.WriteToConsole("the max gap remains the same =" + currMaxGap, verbose);
                }
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                if (timer.ElapsedMilliseconds > timeout_ms)
                {
                    break;
                }
            }
            return worstResult;
        }

        private double GaussianRandomNumberGenerator(Random rng, double mean, double stddev)
        {
            // Box–Muller_transform
            double rnd1 = 1.0 - rng.NextDouble();
            double rnd2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(rnd1)) * Math.Sin(2.0 * Math.PI * rnd2);
            return mean + stddev * randStdNormal;
        }

        /// <summary>
        /// Using some hill climbers to generate some adversary inputs.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) HillClimbingAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            int numNeighbors,
            double demandUB,
            double stddev,
            bool grey = false,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            if (numTrials < 1)
            {
                throw new Exception("num trials for hill climber should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0)
            {
                demandUB = this.Topology.MaxCapacity() * this.maxNumPath;
            }
            if (storeProgress)
            {
                if (logPath == null)
                {
                    throw new Exception("should specify logPath if storeprogress = true!");
                }
                else
                {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }

            double currMaxGap = 0;
            TEOptimizationSolution zero_solution = new TEOptimizationSolution
            {
                MaxObjective = 0,
                Demands = new Dictionary<(string, string), double> { },
                // Flows = new Dictionary<(string, string), double> { },
                FlowsPaths = new Dictionary<string[], double> { },
            };
            (TEOptimizationSolution, TEOptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);
            double timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            // Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            bool timeoutReached = false;
            foreach (int i in Enumerable.Range(0, numTrials))
            {
                // initializing some random demands
                var currDemands = getRandomDemand(rng, demandUB);
                double currGap = 0.0;
                (TEOptimizationSolution, TEOptimizationSolution) currResult = (zero_solution, zero_solution);
                bool feasible = true;
                do
                {
                    feasible = true;
                    try
                    {
                        (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                    }
                    catch (DemandPinningLinkNegativeException e)
                    {
                        Console.WriteLine("Infeasible input!");
                        feasible = false;
                        if (grey)
                        {
                            ReduceDemandsOnLink(currDemands, e.Edge, e.Threshold, 0.1);
                        }
                        else
                        {
                            currDemands = getRandomDemand(rng, demandUB);
                        }
                        Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                    }
                    if (timer.ElapsedMilliseconds > timeout_ms)
                    {
                        timeoutReached = true;
                        break;
                    }
                } while (!feasible);
                Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                bool localMax = true;
                do
                {
                    if (timeoutReached)
                    {
                        break;
                    }
                    localMax = true;
                    foreach (int j in Enumerable.Range(0, numNeighbors))
                    {
                        // generating neighbor demands
                        Dictionary<(string, string), double> neighborDemands = new Dictionary<(string, string), double>();
                        double maxNeighborDemand = 0;
                        foreach (var pair in this.Topology.GetNodePairs())
                        {
                            neighborDemands[pair] = Math.Min(Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev)), demandUB);
                            maxNeighborDemand = Math.Max(maxNeighborDemand, neighborDemands[pair]);
                        }
                        Console.WriteLine(maxNeighborDemand);
                        // finding gap for the neighbor
                        double neighborGap = 0.0;
                        (TEOptimizationSolution, TEOptimizationSolution) neighborResult = (zero_solution, zero_solution);
                        feasible = true;
                        do
                        {
                            feasible = true;
                            try
                            {
                                (neighborGap, neighborResult) = GetGap(optimalEncoder, heuristicEncoder, neighborDemands);
                            }
                            catch (DemandPinningLinkNegativeException e)
                            {
                                Console.WriteLine("Infeasible input!");
                                feasible = false;
                                if (grey)
                                {
                                    ReduceDemandsOnLink(neighborDemands, e.Edge, e.Threshold, 0.1);
                                }
                                else
                                {
                                    maxNeighborDemand = 0;
                                    foreach (var pair in this.Topology.GetNodePairs())
                                    {
                                        neighborDemands[pair] = Math.Min(Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev)), demandUB);
                                        maxNeighborDemand = Math.Max(maxNeighborDemand, neighborDemands[pair]);
                                    }
                                    Console.WriteLine(maxNeighborDemand);
                                }
                                Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                            }
                            if (timer.ElapsedMilliseconds > timeout_ms)
                            {
                                timeoutReached = true;
                                break;
                            }
                        } while (!feasible);
                        // check if better advers input
                        if (neighborGap > currGap)
                        {
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("======== try " + i + " neighbor " + j + " found a neighbor with gap " + neighborGap + " higher than " + currGap, verbose);
                            currDemands = neighborDemands;
                            currResult = neighborResult;
                            currGap = neighborGap;
                            localMax = false;
                        }
                        else
                        {
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("===========================================================", verbose);
                            Utils.WriteToConsole("======== try " + i + " neighbor " + j + " has a lower gap " + neighborGap + " than curr gap " + currGap, verbose);
                        }
                        Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + Math.Max(currGap, currMaxGap), storeProgress);
                        if (timer.ElapsedMilliseconds > timeout_ms)
                        {
                            timeoutReached = true;
                            break;
                        }
                    }
                } while (!localMax);

                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("===========================================================", verbose);
                Utils.WriteToConsole("======== try " + i + " found a local maximum with gap " + currGap, verbose);
                if (currGap > currMaxGap)
                {
                    Utils.WriteToConsole("updating the max gap from " + currMaxGap + " to " + currGap, verbose);
                    currMaxGap = currGap;
                    worstResult = currResult;
                }
                else
                {
                    Utils.WriteToConsole("the max gap remains the same =" + currMaxGap, verbose);
                }
                if (timeoutReached)
                {
                    break;
                }
            }
            return worstResult;
        }

        /// <summary>
        /// Generate single random demand.
        /// </summary>
        protected double getSingleRandomDemand(Random rng, (string, string) pair, double demandUB)
        {
            if (this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2).Count() <= 0)
            {
                return 0;
            }
            return rng.NextDouble() * demandUB;
        }

        /// <summary>
        /// Generate single random demand.
        /// </summary>
        protected double getSingleRandomDemand(Random rng, (string, string) pair, double demandUB, GenericDemandList demandList)
        {
            if (this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2).Count() <= 0)
            {
                return 0;
            }
            return demandList.GetRandomDemandForPair(rng, pair.Item1, pair.Item2);
        }

        /// <summary>
        /// Generates random demands.
        /// </summary>
        protected Dictionary<(string, string), double> getRandomDemand(Random rng, double demandUB)
        {
            Dictionary<(string, string), double> currDemands = new Dictionary<(string, string), double>();
            // initializing some random demands
            double maxDemand = 0;
            foreach (var pair in this.Topology.GetNodePairs())
            {
                currDemands[pair] = getSingleRandomDemand(rng, pair, demandUB);
                maxDemand = Math.Max(maxDemand, currDemands[pair]);
            }
            Console.WriteLine(maxDemand);
            return currDemands;
        }

        /// <summary>
        /// Generates random demands.
        /// </summary>
        protected Dictionary<(string, string), double> getRandomDemand(Random rng, double demandUB, GenericDemandList demandList)
        {
            Dictionary<(string, string), double> currDemands = new Dictionary<(string, string), double>();
            // initializing some random demands
            double maxDemand = 0;
            foreach (var pair in this.Topology.GetNodePairs())
            {
                currDemands[pair] = getSingleRandomDemand(rng, pair, demandUB, demandList);
                maxDemand = Math.Max(maxDemand, currDemands[pair]);
            }
            Console.WriteLine(maxDemand);
            return currDemands;
        }

        /// <summary>
        /// Reduce the demands on an specific link.
        /// </summary>
        protected void ReduceDemandsOnLink(Dictionary<(string, string), double> demands, (string, string) edge, double threshold, double split_ratio)
        {
            Debug.Assert(split_ratio <= 1);
            foreach (var pair in Topology.GetNodePairs())
            {
                if (demands[pair] <= 0 || demands[pair] > threshold)
                {
                    continue;
                }
                var paths = this.Topology.ShortestKPaths(1, pair.Item1, pair.Item2);
                if (paths.Count() <= 0)
                {
                    continue;
                }

                for (int i = 0; i < paths[0].Count() - 1; i++)
                {
                    if (paths[0][i] != edge.Item1)
                    {
                        continue;
                    }
                    if (paths[0][i + 1] != edge.Item2)
                    {
                        continue;
                    }
                    demands[pair] *= split_ratio;
                }
            }
        }

        /// <summary>
        /// Using Simulated Annealing to generate adversarial inputs.
        /// </summary>
        public (TEOptimizationSolution, TEOptimizationSolution) SimulatedAnnealing(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTmpSteps,
            int numNeighbors,
            double demandUB,
            double stddev,
            double initialTmp,
            double tmpDecreaseFactor,
            bool grey = false,
            int numNoIncreaseToReset = -1,
            double NoChangeRelThreshold = 0.01,
            int seed = 0,
            bool verbose = false,
            bool storeProgress = false,
            string logPath = null,
            double timeout = Double.PositiveInfinity)
        {
            if (numTmpSteps < 1)
            {
                throw new Exception("num temperature steps should be positive but got " + numTmpSteps + "!!");
            }
            if (initialTmp <= 0)
            {
                throw new Exception("initial temperature should be positive but got " + initialTmp + "!!");
            }
            if (tmpDecreaseFactor >= 1 | tmpDecreaseFactor < 0)
            {
                throw new Exception("temperature decrease factor should be between 0 and 1 but got " + tmpDecreaseFactor + "!!");
            }
            if (demandUB <= 0)
            {
                demandUB = this.Topology.MaxCapacity() * this.maxNumPath;
            }
            if (storeProgress)
            {
                if (logPath == null)
                {
                    throw new Exception("should specify logPath if storeprogress = true!");
                }
                else
                {
                    logPath = Utils.CreateFile(logPath, removeIfExist: true, addFid: true);
                }
            }
            if (numNoIncreaseToReset == -1)
            {
                numNoIncreaseToReset = numNeighbors * 2;
            }

            double currTmp = initialTmp;
            Random rng = new Random(seed);
            bool timeoutReached = false;
            var timeout_ms = timeout * 1000;
            Stopwatch timer = Stopwatch.StartNew();
            TEOptimizationSolution zero_solution = new TEOptimizationSolution
            {
                MaxObjective = 0,
                Demands = new Dictionary<(string, string), double> { },
                // Flows = new Dictionary<(string, string), double> { },
                FlowsPaths = new Dictionary<string[], double> { },
            };
            bool feasible = true;
            Dictionary<(string, string), double> currDemands = getRandomDemand(rng, demandUB);
            double currGap = 0;
            (TEOptimizationSolution, TEOptimizationSolution) currResult = (zero_solution, zero_solution);
            do
            {
                feasible = true;
                try
                {
                    (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                }
                catch (DemandPinningLinkNegativeException e)
                {
                    feasible = false;
                    Console.WriteLine("Infeasible input!");
                    if (grey)
                    {
                        ReduceDemandsOnLink(currDemands, e.Edge, e.Threshold, 0.1);
                    }
                    else
                    {
                        currDemands = getRandomDemand(rng, demandUB);
                    }
                    Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", 0.0", storeProgress);
                }
                if (timer.ElapsedMilliseconds > timeout_ms)
                {
                    timeoutReached = true;
                    break;
                }
            } while (!feasible);
            (TEOptimizationSolution, TEOptimizationSolution) worstResult = currResult;
            double currMaxGap = currGap;
            double restartMaxGap = currGap;
            Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);

            int noIncrease = 0;
            foreach (int p in Enumerable.Range(0, numTmpSteps))
            {
                if (timeoutReached)
                {
                    break;
                }
                foreach (int Mp in Enumerable.Range(0, numNeighbors))
                {
                    // generating neighbor demands
                    Dictionary<(string, string), double> neighborDemands = new Dictionary<(string, string), double>();
                    double maxNeighborDemand = 0;
                    foreach (var pair in this.Topology.GetNodePairs())
                    {
                        neighborDemands[pair] = Math.Min(Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev)), demandUB);
                        maxNeighborDemand = Math.Max(maxNeighborDemand, neighborDemands[pair]);
                    }
                    Console.WriteLine(maxNeighborDemand);
                    // finding gap for the neighbor
                    feasible = true;
                    double neighborGap = 0;
                    (TEOptimizationSolution, TEOptimizationSolution) neighborResult = (zero_solution, zero_solution);
                    do
                    {
                        feasible = true;
                        try
                        {
                            (neighborGap, neighborResult) = GetGap(optimalEncoder, heuristicEncoder, neighborDemands);
                        }
                        catch (DemandPinningLinkNegativeException e)
                        {
                            feasible = false;
                            Console.WriteLine("Infeasible input!");
                            if (grey)
                            {
                                ReduceDemandsOnLink(neighborDemands, e.Edge, e.Threshold, 0.1);
                            }
                            else
                            {
                                maxNeighborDemand = 0;
                                foreach (var pair in this.Topology.GetNodePairs())
                                {
                                    neighborDemands[pair] = Math.Min(Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev)), demandUB);
                                    maxNeighborDemand = Math.Max(maxNeighborDemand, neighborDemands[pair]);
                                }
                                Console.WriteLine(maxNeighborDemand);
                            }
                            Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                        }
                        if (timer.ElapsedMilliseconds > timeout_ms)
                        {
                            timeoutReached = true;
                            break;
                        }
                    } while (!feasible);
                    Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                    if (timeoutReached)
                    {
                        break;
                    }
                    // check if better advers input
                    if (neighborGap > currGap)
                    {
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("======== try " + p + " neighbor " + Mp + " found a neighbor with gap " + neighborGap + " higher than " + currGap +
                            " max gap = " + currMaxGap, verbose);
                        currDemands = neighborDemands;
                        currResult = neighborResult;
                        currGap = neighborGap;
                        if (neighborGap > currMaxGap)
                        {
                            worstResult = currResult;
                            currMaxGap = currGap;
                        }
                    }
                    else
                    {
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("===========================================================", verbose);
                        Utils.WriteToConsole("======== try " + p + " neighbor " + Mp + " has a lower gap " + neighborGap + " than curr gap " + currGap +
                            " max gap = " + currMaxGap, verbose);
                        double currProbability = Math.Exp((neighborGap - currGap) / currTmp);
                        double randomNumber = rng.NextDouble();
                        Utils.WriteToConsole("current temperature is " + currTmp, verbose);
                        Utils.WriteToConsole("current gap difference is " + (neighborGap - currGap), verbose);
                        Utils.WriteToConsole("current probability is " + currProbability + " and the random number is " + randomNumber, verbose);
                        if (randomNumber <= currProbability)
                        {
                            Utils.WriteToConsole("accepting the lower gap", verbose);
                            currDemands = neighborDemands;
                            currResult = neighborResult;
                            currGap = neighborGap;
                        }
                    }
                    Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                    if (timer.ElapsedMilliseconds > timeout_ms)
                    {
                        timeoutReached = true;
                        break;
                    }
                    if ((currGap - restartMaxGap) / restartMaxGap > NoChangeRelThreshold)
                    {
                        noIncrease = 0;
                        restartMaxGap = currGap;
                    }
                    else
                    {
                        noIncrease += 1;
                    }
                }
                if (timeoutReached)
                {
                    break;
                }
                currTmp = currTmp * tmpDecreaseFactor;
                // reset the initial point if no increase in numNoIncreaseToReset iterations
                if (noIncrease > numNoIncreaseToReset)
                {
                    feasible = true;
                    currDemands = getRandomDemand(rng, demandUB);
                    do
                    {
                        feasible = true;
                        try
                        {
                            (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                        }
                        catch (DemandPinningLinkNegativeException e)
                        {
                            feasible = false;
                            Console.WriteLine("Infeasible input!");
                            if (grey)
                            {
                                ReduceDemandsOnLink(currDemands, e.Edge, e.Threshold, 0.1);
                            }
                            else
                            {
                                currDemands = getRandomDemand(rng, demandUB);
                            }
                            Utils.StoreProgress(logPath, timer.ElapsedMilliseconds + ", " + currMaxGap, storeProgress);
                        }
                        if (timer.ElapsedMilliseconds > timeout_ms)
                        {
                            timeoutReached = true;
                            break;
                        }
                    } while (!feasible);
                    if (currGap > currMaxGap)
                    {
                        worstResult = currResult;
                        currMaxGap = currGap;
                    }
                    currTmp = initialTmp;
                    noIncrease = 0;
                    restartMaxGap = currGap;
                }
            }
            Console.WriteLine("final max gap is " + currMaxGap);
            return worstResult;
        }
    }
}
