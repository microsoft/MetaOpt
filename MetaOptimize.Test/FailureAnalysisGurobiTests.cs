namespace MetaOptimize.Test
{
    using Gurobi;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    /// <summary>
    /// Uses Gurobi to do the tests.
    /// </summary>
    [TestClass]
    public class FailureAnalysisGurobiTests : FailureAnalysisBasicTests<GRBVar, GRBModel>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new GurobiSOS();
        }
    }
}