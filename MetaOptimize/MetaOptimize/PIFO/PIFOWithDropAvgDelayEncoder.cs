namespace MetaOptimize
{
    /// <summary>
    /// PIFO with limited size queue that drops packets.
    /// </summary>
    public class PIFOWithDropAvgDelayEncoder<TVar, TSolution> : PIFOWithDropEncoder<TVar, TSolution>
    {
        /// <summary>
        /// create a new instance.
        /// </summary>
        public PIFOWithDropAvgDelayEncoder(ISolver<TVar, TSolution> solver, int numPackets, int maxRank, int maxQueueSize)
            : base(solver, numPackets, maxRank, maxQueueSize)
        {
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected override void ComputeCost()
        {
            PIFOUtils<TVar, TSolution>.ComputeAvgDelayPlacement(this.Solver, this.cost, this.NumPackets, this.MaxRank,
                this.placementVariables, this.rankVariables, this.packetAdmitOrDrop);
        }
    }
}