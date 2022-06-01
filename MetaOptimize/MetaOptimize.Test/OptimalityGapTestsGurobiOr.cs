using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the optimality gap.
    /// </summary>
    [TestClass]
    public class OptimalityGapTestsGurobiOr : OptimalityGapTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new SolverGurobi();
        }
    }
}
