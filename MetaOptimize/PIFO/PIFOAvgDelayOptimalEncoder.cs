namespace MetaOptimize
{
    /// <summary>
    /// PIFO Optimal Encoder for avg delay.
    /// </summary>
    public class PIFOAvgDelayOptimalEncoder<TVar, TSolution> : PIFOOptimalEncoder<TVar, TSolution>
    {
        /// <summary>
        /// create a new instance.
        /// </summary>
        public PIFOAvgDelayOptimalEncoder(ISolver<TVar, TSolution> solver, int NumPackets, int maxRank)
            : base(solver, NumPackets, maxRank)
        {
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected override void ComputeCost()
        {
            PIFOUtils<TVar, TSolution>.ComputeAvgDelayPlacement(this.Solver, this.cost, this.NumPackets, this.MaxRank,
                this.placementVariables, this.rankVariables);
        }
    }
}