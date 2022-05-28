// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public static class AdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        public static (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder)
        {
            var optimalEncoding = optimalEncoder.Encoding();
            var heuristicEncoding = heuristicEncoder.Encoding();

            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new System.Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            var solver = optimalEncoder.Solver;
            // ensures that demand in both problems is the same.
            foreach (var (pair, variable) in optimalEncoding.DemandVariables)
            {
                var heuristicVariable = heuristicEncoding.DemandVariables[pair];
                solver.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, variable), new Term<TVar>(-1, heuristicVariable)));
            }

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
    }
}
