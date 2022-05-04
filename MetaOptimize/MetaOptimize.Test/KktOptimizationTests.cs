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
        public void TestMaximizeKkt1()
        {
            var x = Zen.Symbolic<Real>("x");
            var y = Zen.Symbolic<Real>("y");

            var encoder = new KktOptimizationGenerator(new HashSet<Zen<Real>>() { x, y }, new HashSet<Zen<Real>>());

            // x + 2y == 10
            encoder.AddEqZeroConstraint(new Polynomial(new Term(1, x), new Term(2, y), new Term(-10)));

            // x >= 0, y>= 0
            encoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, x)));
            encoder.AddLeqZeroConstraint(new Polynomial(new Term(-1, y)));

            // maximize y - x
            var constraints = encoder.MaximizationConstraints(new Polynomial(new Term(1, y), new Term(-1, x)));

            var solution = constraints.Solve();

            Assert.AreEqual(0, solution.Get(x));
            Assert.AreEqual(5, solution.Get(y));
        }
    }
}