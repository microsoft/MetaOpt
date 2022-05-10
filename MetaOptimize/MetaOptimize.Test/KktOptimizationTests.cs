// <copyright file="KktOptimizationTests.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System.Collections.Generic;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using ZenLib;

    /// <summary>
    /// Some basic optimization tests.
    /// </summary>
    [TestClass]
    public class KktOptimizationTests
    {
        /// <summary>
        /// Test that maximization works via the kkt conditions.
        /// </summary>
        [TestMethod]
        public void TestMaximizeKktUsingZen1()
        {
            // Choose Solver and set variables.
            var solver = new SolverZen();
            var x = solver.CreateVariable("x");
            var y = solver.CreateVariable("y");
            var encoder = new KktOptimizationGenerator<Zen<Real>, ZenSolution>(solver, new HashSet<Zen<Real>>() { x, y }, new HashSet<Zen<Real>>());

            // x + 2y == 10

            encoder.AddEqZeroConstraint(new Polynomial<Zen<Real>>(new Term<Zen<Real>>(1, x), new Term<Zen<Real>>(2, y), new Term<Zen<Real>>(-10)));

            // x >= 0, y>= 0
            encoder.AddLeqZeroConstraint(new Polynomial<Zen<Real>>(new Term<Zen<Real>>(-1, x)));
            encoder.AddLeqZeroConstraint(new Polynomial<Zen<Real>>(new Term<Zen<Real>>(-1, y)));

            // maximize y - x
            encoder.AddMaximizationConstraints(new Polynomial<Zen<Real>>(new Term<Zen<Real>>(1, y), new Term<Zen<Real>>(-1, x)));

            // doesn't matter what we maximize here.
            /* foreach (var c in solver.ConstraintExprs)
            {
                System.Console.WriteLine(c);
            } */

            var solution = solver.Maximize(solver.CreateVariable("objective"));

            Assert.AreEqual(0, solution.Get(x));
            Assert.AreEqual(5, solution.Get(y));
        }
    }
}