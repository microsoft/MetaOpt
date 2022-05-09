// <copyright file="AdversarialInputGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using ZenLib;

    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public static class AdversarialInputGenerator
    {
        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        /// <param name="optimalEncoder">The optimal encoder.</param>
        /// <param name="heuristicEncoder">The heuristic encoder.</param>
        /// <param name="relationshipConstraints">Any constraints relating variables in the two problems.</param>
        public static (OptimizationSolution, OptimizationSolution) MaximizeOptimalityGap(
            IEncoder optimalEncoder,
            IEncoder heuristicEncoder,
            Func<OptimizationEncoding, OptimizationEncoding, Zen<bool>> relationshipConstraints)
        {
            var optimalEncoding = optimalEncoder.Encoding();
            var heuristicEncoding = heuristicEncoder.Encoding();

            var constraints = Zen.And(
                optimalEncoding.OptimalConstraints,
                heuristicEncoding.OptimalConstraints,
                Zen.And(relationshipConstraints(optimalEncoding, heuristicEncoding)));

            var objective = optimalEncoding.MaximizationObjective - heuristicEncoding.MaximizationObjective;
            var zenSolution = Zen.Maximize(objective, constraints);
            // var zenSolution = Zen.Solve(constraints);

            return (optimalEncoder.GetSolution(zenSolution), heuristicEncoder.GetSolution(zenSolution));

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
