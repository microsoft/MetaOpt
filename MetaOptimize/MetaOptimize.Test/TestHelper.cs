// <copyright file="TestHelper.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize.Test
{
    using System;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Test helper methods.
    /// </summary>
    [TestClass]
    public static class TestHelper
    {
        /// <summary>
        /// Determines if two values are approximately equal.
        /// </summary>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value.</param>
        /// <param name="threshold">A configurable threshold parameter.</param>
        /// <returns>True if their difference is below the threshold.</returns>
        public static bool IsApproximately(double expected, double actual, double threshold = 0.001)
        {
            if (actual == 0) {
                return expected < threshold;
            }
            return Math.Abs(expected - actual) / actual < threshold;
        }
    }
}