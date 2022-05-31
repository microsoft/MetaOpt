// <copyright file="KktOptimizationTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Some basic optimization tests.
    /// </summary>
    [TestClass]
    public class KktOptimizationTests<TVar, TSol>
    {
        /// <summary>
        /// Function to create a new solver.
        /// </summary>
        internal Func<ISolver<TVar, TSol>> CreateSolver;

        /// <summary>
        /// Test that maximization works via the kkt conditions.
        /// </summary>
        [TestMethod]
        public void TestMaximizeKkt()
        {
            // Choose Solver and initialize variables.

            var solver = CreateSolver();
            var x = solver.CreateVariable("x");
            var y = solver.CreateVariable("y");
            var encoder = new KktOptimizationGenerator<TVar, TSol>(solver, new HashSet<TVar>() { x, y }, new HashSet<TVar>());

            // x + 2y == 10

            encoder.AddEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, x), new Term<TVar>(2, y), new Term<TVar>(-10)));

            // x >= 0, y>= 0
            encoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, x)));
            encoder.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, y)));

            // maximize y - x
            encoder.AddMaximizationConstraints(new Polynomial<TVar>(new Term<TVar>(1, y), new Term<TVar>(-1, x)));

            // doesn't matter what we maximize here.
            /* foreach (var c in solver.ConstraintExprs)
            {
                System.Console.WriteLine(c);
            } */

            var solution = solver.Maximize(solver.CreateVariable("objective"));
            solver.GetVariable(solution, x);

            Assert.IsTrue(TestHelper.IsApproximately(0, solver.GetVariable(solution, x)));
            Assert.IsTrue(TestHelper.IsApproximately(5, solver.GetVariable(solution, y)));
        }
    }
}