namespace MetaOptimize
{
    /// <summary>
    /// modified SP-PIFO with avg delay as cost.
    /// </summary>
    public class ModifiedSPPIFOAvgDelayEncoder<TVar, TSolution> : ModifiedSPPIFOEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The constructor.
        /// </summary>
        public ModifiedSPPIFOAvgDelayEncoder(ISolver<TVar, TSolution> solver, int numPackets, int splitQueue, int numQueues, int splitRank, int maxRank)
            : base(solver, numPackets, splitQueue, numQueues, splitRank, maxRank)
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