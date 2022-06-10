// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public class AdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// The topology for the network.
        /// </summary>
        public Topology Topology { get; set; }

        /// <summary>
        /// The maximum number of paths to use between any two nodes.
        /// </summary>
        public int K { get; set; }

        /// <summary>
        /// The demand variables.
        /// </summary>
        public Dictionary<(string, string), TVar> DemandVariables { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public AdversarialInputGenerator(Topology topology, int k) {
            this.Topology = topology;
            this.K = k;
        }
        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        public (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double demandUB = -1)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            var solver = optimalEncoder.Solver;

            CreateDemandVariables(solver);
            var optimalEncoding = optimalEncoder.Encoding(preDemandVariables: this.DemandVariables);
            var heuristicEncoding = heuristicEncoder.Encoding(preDemandVariables: this.DemandVariables);

            // ensures that demand in both problems is the same and lower than demand upper bound constraint.
            EnsureDemandUB(solver, demandUB);

            // var objectiveVariable = solver.CreateVariable("objective");
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(-1, heuristicEncoding.GlobalObjective));
            var solution = solver.Maximize(objective);

            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));

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

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        /// <param name="minDifference">The minimum difference.</param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        public (OptimizationSolution, OptimizationSolution) FindOptimalityGapAtLeast(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double minDifference,
            double demandUB = -1)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            var solver = optimalEncoder.Solver;

            CreateDemandVariables(solver);
            var optimalEncoding = optimalEncoder.Encoding(this.DemandVariables);
            var heuristicEncoding = heuristicEncoder.Encoding(this.DemandVariables);

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

            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));

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
            double demandUB = -1)
        {
            if (demandUB < 0) {
                demandUB = double.PositiveInfinity;
            }
            demandUB = Math.Min(this.Topology.MaxCapacity() * this.K, demandUB);
            foreach (var (pair, variable) in this.DemandVariables)
            {
                // var heuristicVariable = heuristicEncoding.DemandVariables[pair];
                // solver.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, heuristicVariable)));
                solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1 * demandUB), new Term<TVar>(1, variable)));
                // solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1 * demandUB), new Term<TVar>(1, heuristicVariable)));
            }
        }

        private void CreateDemandVariables(ISolver<TVar, TSolution> solver) {
            this.DemandVariables = new Dictionary<(string, string), TVar>();
            foreach (var pair in this.Topology.GetNodePairs())
            {
                this.DemandVariables[pair] = solver.CreateVariable("demand_" + pair.Item1 + "_" + pair.Item2);
            }
        }

        /// <summary>
        /// Finds an adversarial input that is within intervalConf of the maximum gap.
        /// </summary>
        /// <param name="optimalEncoder"> </param>
        /// <param name="heuristicEncoder"> </param>
        /// <param name="intervalConf"></param>
        /// <param name="startGap"></param>
        /// <param name="demandUB">upper bound on all the demands.</param>
        public (OptimizationSolution, OptimizationSolution) FindMaximumGapInterval(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            double intervalConf,
            double startGap,
            double demandUB = double.PositiveInfinity)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch....");
            }

            if (startGap <= 0.001)
            {
                throw new System.Exception("Starting Gap too small...");
            }
            var solver = optimalEncoder.Solver;

            CreateDemandVariables(solver);
            var optimalEncoding = optimalEncoder.Encoding(this.DemandVariables);
            var heuristicEncoding = heuristicEncoder.Encoding(this.DemandVariables);

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
            while (!found_infeas) {
                Console.WriteLine("************** Current Gap Interval (Phase 1) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("nxt=" + ubGap);
                Console.WriteLine("**************************************************");
                try {
                    solution = solver.CheckFeasibility(ubGap);
                    lbGap = ubGap;
                    ubGap = ubGap * 2;
                }
                catch (InfeasibleOrUnboundSolution) {
                    found_infeas = true;
                }
                // solver.ChangeConstraintRHS(nameLBConst, -1 * ubGap);
            }

            while ((ubGap - lbGap) / lbGap > intervalConf) {
                double midGap = (lbGap + ubGap) / 2;
                Console.WriteLine("************** Current Gap Interval (Phase 2) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("ub=" + ubGap);
                Console.WriteLine("nxt=" + midGap);
                Console.WriteLine("**************************************************");
                // solver.ChangeConstraintRHS(nameLBConst, -1 * midGap);
                try {
                    solution = solver.CheckFeasibility(midGap);
                    lbGap = midGap;
                }
                catch (InfeasibleOrUnboundSolution) {
                    ubGap = midGap;
                }
            }
            Console.WriteLine("************** Final Gap Interval ****************");
            Console.WriteLine("lb=" + lbGap);
            Console.WriteLine("ub=" + ubGap);
            Console.WriteLine("**************************************************");
            // solver.ChangeConstraintRHS(nameLBConst, -1 * lbGap);
            solution = solver.CheckFeasibility(lbGap);
            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));
        }

        private (double, (OptimizationSolution, OptimizationSolution)) GetGap (
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            Dictionary<(string, string), double> demands)
        {
            // solving the hueristic for the demand
            heuristicEncoder.Solver.CleanAll();
            var encodingHeuristic = heuristicEncoder.Encoding(demandEqualityConstraints: demands, noKKT: true);
            var solverSolutionHeuristic = heuristicEncoder.Solver.Maximize(encodingHeuristic.MaximizationObjective);
            var optimizationSolutionHeuristic = heuristicEncoder.GetSolution(solverSolutionHeuristic);

            // solving the optimal for the demand
            optimalEncoder.Solver.CleanAll();
            var encodingOptimal = optimalEncoder.Encoding(demandEqualityConstraints: demands, noKKT: true);
            var solverSolutionOptimal = optimalEncoder.Solver.Maximize(encodingOptimal.MaximizationObjective);
            var optimizationSolutionOptimal = optimalEncoder.GetSolution(solverSolutionOptimal);
            double currGap = optimizationSolutionOptimal.TotalDemandMet - optimizationSolutionHeuristic.TotalDemandMet;
            return (currGap, (optimizationSolutionOptimal, optimizationSolutionHeuristic));
        }

        /// <summary>
        /// Generate some random inputs and takes the max gap as the adversary.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) RandomAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            double demandUB,
            int seed = 0)
        {
            // if (optimalEncoder.Solver == heuristicEncoder.Solver) {
            //     throw new Exception("solvers should be different for random generator!!!");
            // }
            if (numTrials < 1) {
                throw new Exception("num trials for random generator should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0) {
                demandUB = this.Topology.MaxCapacity() * this.K;
            }
            double currMaxGap = 0;
            OptimizationSolution zero_solution = new OptimizationSolution {
                    TotalDemandMet = 0,
                    Demands = new Dictionary<(string, string), double> { },
                    Flows = new Dictionary<(string, string), double> { },
                    FlowsPaths = new Dictionary<string[], double> { },
                };
            (OptimizationSolution, OptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);

            foreach (int i in Enumerable.Range(0, numTrials)) {
                Dictionary<(string, string), double> demands = new Dictionary<(string, string), double>();
                // initializing some random demands
                foreach (var pair in this.Topology.GetNodePairs()) {
                    demands[pair] = rng.NextDouble() * demandUB;
                }
                var (currGap, result) = GetGap(optimalEncoder, heuristicEncoder, demands);
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("======== try " + i + " found a solution with gap " + currGap);
                if (currGap > currMaxGap) {
                    Console.WriteLine("updating the max gap from " + currMaxGap + " to " + currGap);
                    currMaxGap = currGap;
                    worstResult = result;
                } else {
                    Console.WriteLine("the max gap remains the same =" + currMaxGap);
                }
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
            }
            return worstResult;
        }

        private double GaussianRandomNumberGenerator(Random rng, double mean, double stddev) {
            // Box–Muller_transform
            double rnd1 = 1.0 - rng.NextDouble();
            double rnd2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(rnd1)) * Math.Sin(2.0 * Math.PI * rnd2);
            return mean + stddev * randStdNormal;
        }

        /// <summary>
        /// Generate some random inputs and takes the max gap as the adversary.
        /// </summary>
        public (OptimizationSolution, OptimizationSolution) HillClimbingAdversarialGenerator(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            int numTrials,
            int numNeighbors,
            double demandUB,
            double stddev,
            int seed = 0)
        {
            if (numTrials < 1) {
                throw new Exception("num trials for random generator should be positive but got " + numTrials + "!!");
            }
            if (demandUB <= 0) {
                demandUB = this.Topology.MaxCapacity() * this.K;
            }
            double currMaxGap = 0;
            OptimizationSolution zero_solution = new OptimizationSolution {
                    TotalDemandMet = 0,
                    Demands = new Dictionary<(string, string), double> { },
                    Flows = new Dictionary<(string, string), double> { },
                    FlowsPaths = new Dictionary<string[], double> { },
                };
            (OptimizationSolution, OptimizationSolution) worstResult = (zero_solution, zero_solution);
            Random rng = new Random(seed);

            foreach (int i in Enumerable.Range(0, numTrials)) {
                Dictionary<(string, string), double> currDemands = new Dictionary<(string, string), double>();
                // initializing some random demands
                foreach (var pair in this.Topology.GetNodePairs()) {
                    currDemands[pair] = rng.NextDouble() * demandUB;
                }
                var (currGap, currResult) = GetGap(optimalEncoder, heuristicEncoder, currDemands);
                bool localMax = true;
                do {
                    localMax = true;
                    foreach (int j in Enumerable.Range(0, numNeighbors)) {
                        // generating neighbor demands
                        Dictionary<(string, string), double> neighborDemands = new Dictionary<(string, string), double>();
                        foreach (var pair in this.Topology.GetNodePairs()) {
                            neighborDemands[pair] = Math.Max(0, currDemands[pair] + GaussianRandomNumberGenerator(rng, 0, stddev));
                        }
                        // finding gap for the neighbor
                        var (neighborGap, neighborResult) = GetGap(optimalEncoder, heuristicEncoder, neighborDemands);
                        // check if better advers input
                        if (neighborGap > currGap) {
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("======== try " + i + " neighbor " + j + " found a neighbor with gap " + neighborGap + " higher than " + currGap);
                            currDemands = neighborDemands;
                            currResult = neighborResult;
                            currGap = neighborGap;
                            localMax = false;
                        } else {
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("===========================================================");
                            Console.WriteLine("======== try " + i + " neighbor " + j + " has a lower gap " + neighborGap + " than curr gap " + currGap);
                        }
                    }
                } while (!localMax);

                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("======== try " + i + " found a local maximum with gap " + currGap);
                if (currGap > currMaxGap) {
                    Console.WriteLine("updating the max gap from " + currMaxGap + " to " + currGap);
                    currMaxGap = currGap;
                    worstResult = currResult;
                } else {
                    Console.WriteLine("the max gap remains the same =" + currMaxGap);
                }
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
                Console.WriteLine("===========================================================");
            }
            return worstResult;
        }
    }
}
