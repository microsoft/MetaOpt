namespace MetaOptimize
{
    /// <summary>
    /// SP-PIFO with limited size queue that drops packets.
    /// </summary>
    public class SPPIFOWithDropAvgDelayEncoder<TVar, TSolution> : SPPIFOWithDropEncoder<TVar, TSolution>
    {
        /// <summary>
        /// create a new instance.
        /// </summary>
        public SPPIFOWithDropAvgDelayEncoder(ISolver<TVar, TSolution> solver, int numPackets,
            int numQueues, int maxRank, int totalQueueSize) : base(solver, numPackets, numQueues, maxRank, totalQueueSize)
        {
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected override void ComputeCost()
        {
            PIFOUtils<TVar, TSolution>.ComputeAvgDelayDequeueAfter(this.Solver, this.cost, this.NumPackets, this.MaxRank,
                this.dequeueAfter, this.packetRankVar, this.packetAdmitOrDrop);
        }
    }
}