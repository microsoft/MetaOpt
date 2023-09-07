namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Gurobi;

    /// <summary>
    /// PIFO Optimal Encoder.
    /// </summary>
    public class PIFOOptimalEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The underlying solver.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// variable showing whether the $i$-th incomming packet is the $j$-th dequeued packet.
        /// </summary>
        public Dictionary<(int, int), TVar> placementVariables { get; set; }

        /// <summary>
        /// variable showing rank of packet i.
        /// </summary>
        public Dictionary<int, TVar> rankVariables { get; set; }

        /// <summary>
        /// Cost of an ordering of packets.
        /// </summary>
        public TVar cost { get; set; }

        /// <summary>
        /// rank equality constraints.
        /// </summary>
        public IDictionary<int, int> rankEqualityConstraints { get; set; }

        /// <summary>
        /// number of packets.
        /// </summary>
        public int NumPackets;

        /// <summary>
        /// maximum rank of a packets.
        /// </summary>
        public int MaxRank;

        /// <summary>
        /// Create a new instance of the encoder.
        /// </summary>
        public PIFOOptimalEncoder(ISolver<TVar, TSolution> solver, int NumPackets, int maxRank)
        {
            this.Solver = solver;
            this.NumPackets = NumPackets;
            this.MaxRank = maxRank;
        }

        /// <summary>
        /// initialize variables.
        /// </summary>
        private void InitializeVariables(Dictionary<int, TVar> preRankVariables,
            Dictionary<int, int> rankEqualityConstraints)
        {
            this.rankVariables = new Dictionary<int, TVar>();
            this.placementVariables = new Dictionary<(int, int), TVar>();
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                if (preRankVariables == null) {
                    this.rankVariables[packetID] = this.Solver.CreateVariable("rank_" + packetID, GRB.INTEGER, lb: 0, ub: this.MaxRank);
                } else {
                    this.rankVariables[packetID] = preRankVariables[packetID];
                }
                for (int place = 0; place < this.NumPackets; place++) {
                    this.placementVariables[(packetID, place)] = this.Solver.CreateVariable("place_" + packetID + "_" + place, GRB.BINARY);
                }
            }
            this.rankEqualityConstraints = rankEqualityConstraints;
            this.cost = this.Solver.CreateVariable("total_cost_optimal");
        }

        private void EnsureRankEquality() {
            if (this.rankEqualityConstraints == null) {
                return;
            }

            for (int pid = 0; pid < this.NumPackets; pid++) {
                var constr = new Polynomial<TVar>(
                    new Term<TVar>(-1 * this.rankEqualityConstraints[pid]),
                    new Term<TVar>(1, this.rankVariables[pid]));
                this.Solver.AddEqZeroConstraint(constr);
            }
        }

        /// <summary>
        /// Encode the optimal.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(
            Dictionary<int, TVar> preRankVariables = null,
            Dictionary<int, int> rankEqualityConstraints = null,
            bool verbose = false)
        {
            Utils.logger("initialize variables", verbose);
            InitializeVariables(preRankVariables, rankEqualityConstraints);

            Utils.logger("ensure ranks are equal to input ranks", verbose);
            this.EnsureRankEquality();

            Utils.logger("ensure each packet is placed once and each place has only one packet.", verbose);
            for (int i = 0; i < this.NumPackets; i++) {
                var sumPerPacket = new Polynomial<TVar>(new Term<TVar>(-1));
                var sumPerPlace = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int j = 0; j < this.NumPackets; j++) {
                    sumPerPacket.Add(new Term<TVar>(1, this.placementVariables[(i, j)]));
                    sumPerPlace.Add(new Term<TVar>(1, this.placementVariables[(j, i)]));
                }
                this.Solver.AddEqZeroConstraint(sumPerPacket);
                this.Solver.AddEqZeroConstraint(sumPerPlace);
            }

            Utils.logger("computing the cost.", verbose);
            this.ComputeCost();
            var objective = new Polynomial<TVar>(new Term<TVar>(-1, this.cost));
            return new PIFOOptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.cost,
                MaximizationObjective = objective,
                RankVariables = this.rankVariables,
            };
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected virtual void ComputeCost()
        {
            throw new Exception("not implemented....");
        }

        /// <summary>
        /// Get the optimization solution from the solver.
        /// </summary>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var packetRanks = new Dictionary<int, double>();
            var packetOrder = new Dictionary<int, int>();
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                packetRanks[packetID] = this.Solver.GetVariable(solution, this.rankVariables[packetID]);
                for (int place = 0; place < this.NumPackets; place++) {
                    var placeOrNot = Convert.ToInt32(this.Solver.GetVariable(solution, this.placementVariables[(packetID, place)]));
                    if (placeOrNot > 0.99) {
                        packetOrder[packetID] = place;
                        break;
                    }
                }
            }

            return new PIFOOptimizationSolution
            {
                Ranks = packetRanks,
                Order = packetOrder,
                Cost = Convert.ToInt32(this.Solver.GetVariable(solution, this.cost)),
            };
        }
    }
}