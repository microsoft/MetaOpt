// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;

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

            var objectiveVariable = solver.CreateVariable("objective");
            solver.AddEqZeroConstraint(new Polynomial<TVar>(
                new Term<TVar>(-1, objectiveVariable),
                new Term<TVar>(1, optimalEncoding.MaximizationObjective),
                new Term<TVar>(-1, heuristicEncoding.MaximizationObjective)));

            var solution = solver.Maximize(objectiveVariable);

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

            var objectiveVariable = solver.CreateVariable("objective");
            solver.AddEqZeroConstraint(new Polynomial<TVar>(
                new Term<TVar>(-1, objectiveVariable),
                new Term<TVar>(1, optimalEncoding.MaximizationObjective),
                new Term<TVar>(-1, heuristicEncoding.MaximizationObjective)));

            solver.AddLeqZeroConstraint(new Polynomial<TVar>(
                new Term<TVar>(-1, objectiveVariable), new Term<TVar>(minDifference)));

            // var solution = solver.Maximize(solver.CreateVariable("dummy"));
            var solution = solver.CheckFeasibility();

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
            string nameLBConst = solver.AddLeqZeroConstraint(
                new Polynomial<TVar>(
                    new Term<TVar>(-1, optimalEncoding.MaximizationObjective),
                    new Term<TVar>(1, heuristicEncoding.MaximizationObjective),
                    new Term<TVar>(startGap)));

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
                    solution = solver.CheckFeasibility();
                    lbGap = ubGap;
                    ubGap = ubGap * 2;
                }
                catch (InfeasibleOrUnboundSolution) {
                    found_infeas = true;
                }
                solver.ChangeConstraintRHS(nameLBConst, -1 * ubGap);
            }

            while ((ubGap - lbGap) / lbGap > intervalConf) {
                double midGap = (lbGap + ubGap) / 2;
                Console.WriteLine("************** Current Gap Interval (Phase 2) ****************");
                Console.WriteLine("lb=" + lbGap);
                Console.WriteLine("ub=" + ubGap);
                Console.WriteLine("nxt=" + midGap);
                Console.WriteLine("**************************************************");
                solver.ChangeConstraintRHS(nameLBConst, -1 * midGap);
                try {
                    solution = solver.CheckFeasibility();
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
            solver.ChangeConstraintRHS(nameLBConst, -1 * lbGap);
            solution = solver.CheckFeasibility();
            return (optimalEncoder.GetSolution(solution), heuristicEncoder.GetSolution(solution));
        }
    }
}
