namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    /// <summary>
    /// a helper for encoding PIFO heuristics.
    /// </summary>
    public static class PIFOUtils<TVar, TSolution>
    {
        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="cost"></param>
        /// <param name="numPackets"></param>
        /// <param name="maxRank"></param>
        /// <param name="dequeueAfter">entry [i, j] = 1 if packet i dequeued after packet j.</param>
        /// <param name="packetRankVar"> rank of each packet.</param>
        public static void ComputeAvgDelayDequeueAfter(ISolver<TVar, TSolution> solver, TVar cost, int numPackets, int maxRank,
            IDictionary<(int, int), TVar> dequeueAfter, IDictionary<int, TVar> packetRankVar)
        {
            // cost = sum (queueAfter[i, j] * rank[i]) over all i, j
            var sumCost = new Polynomial<TVar>(new Term<TVar>(-1, cost));
            for (int pid = 0; pid < numPackets; pid++) {
                for (int pid2 = 0; pid2 < numPackets; pid2++) {
                    if (pid == pid2) {
                        continue;
                    }
                    sumCost.Add(new Term<TVar>(maxRank, dequeueAfter[(pid, pid2)]));

                    var newVar = EncodingUtils<TVar, TSolution>.LinearizeMultNonNegContinAndBinary(solver,
                        packetRankVar[pid], dequeueAfter[(pid, pid2)], maxRank);
                    sumCost.Add(new Term<TVar>(-1, newVar));
                }
            }
            solver.AddEqZeroConstraint(sumCost);
        }

        /// <summary>
        /// compute the code of an ordering.
        /// </summary>
        /// <param name="solver"></param>
        /// <param name="cost"></param>
        /// <param name="numPackets"></param>
        /// <param name="maxRank"></param>
        /// <param name="placementVar"> entry [i, j] = 1 if packet i is deqeued j-th.</param>
        /// <param name="packetRankVar"> rank of each packet.</param>
        public static void ComputeAvgDelayPlacement(ISolver<TVar, TSolution> solver, TVar cost, int numPackets, int maxRank,
            IDictionary<(int, int), TVar> placementVar, IDictionary<int, TVar> packetRankVar)
        {
            // cost = sum (j * placement[i, j] * (maxRank - rank[i])) over all i, j
            var sumCost = new Polynomial<TVar>(new Term<TVar>(-1, cost));
            for (int packetID = 0; packetID < numPackets; packetID++) {
                for (int place = 0; place < numPackets; place++) {
                    var newVar = EncodingUtils<TVar, TSolution>.LinearizeMultNonNegContinAndBinary(solver,
                        packetRankVar[packetID], placementVar[(packetID, place)], maxRank);
                    sumCost.Add(new Term<TVar>(place * maxRank, placementVar[(packetID, place)]));
                    sumCost.Add(new Term<TVar>(-place, newVar));
                }
            }
            solver.AddEqZeroConstraint(sumCost);
        }
    }
}