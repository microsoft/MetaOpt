namespace MetaOptimize
{
    /// <summary>
    /// AIFO with average delay as cost.
    /// </summary>
    public class AIFOAvgDelayEncoder<TVar, TSolution> : AIFOEncoder<TVar, TSolution>
    {
        /// <summary>
        /// create a new instance.
        /// </summary>
        public AIFOAvgDelayEncoder(ISolver<TVar, TSolution> solver, int numPackets, int maxRank, int maxQueueSize,
            int windowSize, double burstParam) : base(solver, numPackets, maxRank, maxQueueSize, windowSize, burstParam)
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