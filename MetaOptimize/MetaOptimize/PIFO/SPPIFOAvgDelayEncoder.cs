namespace MetaOptimize
{
    /// <summary>
    /// SPPIFO with avg delay as cost.
    /// </summary>
    public class SPPIFOAvgDelayEncoder<TVar, TSolution> : SPPIFOEncoder<TVar, TSolution>
    {
        /// <summary>
        /// create a new instance.
        /// </summary>
        public SPPIFOAvgDelayEncoder(ISolver<TVar, TSolution> solver, int numPackets, int numQueues, int maxRank)
            : base(solver, numPackets, numQueues, maxRank)
        {
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected override void ComputeCost()
        {
            PIFOUtils<TVar, TSolution>.ComputeAvgDelayDequeueAfter(this.Solver, this.cost, this.NumPackets, this.MaxRank,
                this.dequeueAfter, this.packetRankVar);
        }
    }
}