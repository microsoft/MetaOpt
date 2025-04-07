namespace MetaOptimize.Test
{
    using Google.OrTools;
    using Google.OrTools.LinearSolver;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Tests for the pop encoder.
    /// </summary>
    [TestClass]
    public class PopEncodingTestsORTools : PopEncodingTests<Variable, Solver>
    {
        /// <summary>
        /// Initialize the test class.
        /// </summary>
        [TestInitialize]
        public void Initialize()
        {
            this.CreateSolver = () => new ORToolsSolver();
        }
    }
}