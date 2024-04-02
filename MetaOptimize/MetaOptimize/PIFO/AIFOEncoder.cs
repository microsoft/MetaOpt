namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Gurobi;

    /// <summary>
    /// AIFO Encoder.
    /// </summary>
    public class AIFOEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The underlying solver.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// variable showing whether packet $i$ is admitted to the queue or dropped.
        /// </summary>
        public Dictionary<int, TVar> packetAdmitOrDrop { get; set; }

        /// <summary>
        /// variable showing rank of packet i.
        /// </summary>
        public Dictionary<int, TVar> packetRankVar { get; set; }

        /// <summary>
        /// weight of packet i.
        /// </summary>
        public Dictionary<int, TVar> packetWeightVar { get; set; }

        /// <summary>
        /// if packet i should be dequeued after packet j.
        /// </summary>
        public Dictionary<(int, int), TVar> dequeueAfter { get; set; }

        /// <summary>
        /// initial window.
        /// </summary>
        public IList<TVar> initialWindow { get; set; }

        /// <summary>
        /// fraction of availabel space.
        /// </summary>
        public IDictionary<int, TVar> availableSpace { get; set; }

        /// <summary>
        /// quantiles of packet ranks.
        /// </summary>
        public IDictionary<int, TVar> quantiles { get; set; }

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
        /// maximum size of the queue.
        /// </summary>
        public int MaxQueueSize;

        /// <summary>
        /// maximum rank of a packets.
        /// </summary>
        public int MaxRank;

        /// <summary>
        /// window size to estimate quantile.
        /// </summary>
        public double WindowSize;

        /// <summary>
        /// burst tolerance parameter.
        /// </summary>
        public double BurstParam;

        /// <summary>
        /// creating a new instance of AIFO.
        /// </summary>
        public AIFOEncoder(ISolver<TVar, TSolution> solver, int numPackets, int maxRank,
            int maxQueueSize, double windowSize, double burstParam)
        {
            this.Solver = solver;
            this.MaxRank = maxRank;
            this.NumPackets = numPackets;
            this.MaxQueueSize = maxQueueSize;
            this.WindowSize = windowSize;
            this.BurstParam = burstParam;
            Debug.Assert(this.BurstParam < 1.0);
        }

        /// <summary>
        /// initialize variables.
        /// </summary>
        private void CreateVariables(Dictionary<int, TVar> preRankVariables,
            Dictionary<int, int> rankEqualityConstraints)
        {
            this.packetRankVar = new Dictionary<int, TVar>();
            this.packetAdmitOrDrop = new Dictionary<int, TVar>();
            this.packetWeightVar = new Dictionary<int, TVar>();
            this.initialWindow = new List<TVar>();
            this.availableSpace = new Dictionary<int, TVar>();
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                if (preRankVariables == null) {
                    this.packetRankVar[packetID] = this.Solver.CreateVariable("rank_" + packetID, GRB.INTEGER, lb: 0, ub: this.MaxRank);
                } else {
                    this.packetRankVar[packetID] = preRankVariables[packetID];
                }
                this.packetAdmitOrDrop[packetID] = this.Solver.CreateVariable("packet_admit_" + packetID, GRB.BINARY);
                this.packetWeightVar[packetID] = this.Solver.CreateVariable("packet_weight_" + packetID);
                this.availableSpace[packetID] = this.Solver.CreateVariable("avail_space_" + packetID, lb: 0, ub: 2.0 / (1 - this.BurstParam));
            }
            for (int wID = 0; wID < this.WindowSize; wID++) {
                this.initialWindow.Add(this.Solver.CreateVariable("init_window_" + wID, lb: 0, ub: this.MaxRank));
            }
            this.rankEqualityConstraints = rankEqualityConstraints;
            this.cost = this.Solver.CreateVariable("total_cost_optimal");
            CreateAdditionalVariables();
        }

        /// <summary>
        /// for the modified versions to create additional variables.
        /// </summary>
        protected virtual void CreateAdditionalVariables()
        {
        }

        private void InitializeVariables()
        {
        }

        private void EnsureRankEquality() {
            if (this.rankEqualityConstraints == null) {
                return;
            }

            for (int pid = 0; pid < this.NumPackets; pid++) {
                var constr = new Polynomial<TVar>(
                    new Term<TVar>(-1 * this.rankEqualityConstraints[pid]),
                    new Term<TVar>(1, this.packetRankVar[pid]));
                this.Solver.AddEqZeroConstraint(constr);
            }
        }

        private void ComputeFractionAvailableSpace()
        {
            double burstCoef = 1.0 / (1 - this.BurstParam);
            double coefficient = burstCoef / this.MaxQueueSize;
            for (int packetID = 0; packetID < NumPackets; packetID++) {
                var constr1 = new Polynomial<TVar>(new Term<TVar>(-1, this.availableSpace[packetID]));
                if (packetID > 0) {
                    constr1.Add(new Term<TVar>(1, this.availableSpace[packetID - 1]));
                    constr1.Add(new Term<TVar>(-coefficient, this.packetAdmitOrDrop[packetID - 1]));
                } else {
                    constr1.Add(new Term<TVar>(burstCoef));
                }
                this.Solver.AddEqZeroConstraint(constr1);
            }
        }

        private void ComputeQuantile()
        {
            this.quantiles = new Dictionary<int, TVar>();
            var windowRanks = this.initialWindow.ToList();
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                this.quantiles[packetID] = EncodingUtils<TVar, TSolution>.ComputeQuantile(
                    this.Solver, this.packetRankVar[packetID], windowRanks, this.MaxRank + 1, 0.5);
                windowRanks.RemoveAt(0);
                windowRanks.Add(this.packetRankVar[packetID]);
                Debug.Assert(windowRanks.Count == this.WindowSize);
            }
        }

        private void AdmitOrDrop()
        {
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                // EncodingUtils<TVar, TSolution>.IsLeq(this.Solver, this.packetAdmitOrDrop[packetID],
                //                 this.quantiles[packetID], this.availableSpace[packetID],
                //                 this.MaxRank + 1, 0.1);
                var varQ = EncodingUtils<TVar, TSolution>.IsLeq(this.Solver,
                                this.quantiles[packetID], this.availableSpace[packetID],
                                this.MaxRank + 1, 0.1);

                var usedCap = new Polynomial<TVar>(new Term<TVar>(1));
                for (int pid2 = 0; pid2 < packetID; pid2++) {
                    usedCap.Add(new Term<TVar>(1, this.packetAdmitOrDrop[pid2]));
                }
                var totalCap = new Polynomial<TVar>(new Term<TVar>(this.MaxQueueSize));
                var varS = EncodingUtils<TVar, TSolution>.IsLeq(this.Solver,
                                usedCap, totalCap, this.NumPackets + 1, 0.5);

                EncodingUtils<TVar, TSolution>.LinearizeMultTwoBinary(this.Solver,
                    varQ, varS, this.packetAdmitOrDrop[packetID]);
            }
        }

        private void AssignWeights()
        {
            // weight = NumPackets - packet id - 1 + numPackets * admirOrDrop
            for (int pid = 0; pid < this.NumPackets; pid++) {
                var sumPoly = new Polynomial<TVar>(
                    new Term<TVar>(-1, this.packetWeightVar[pid]),
                    new Term<TVar>(this.NumPackets - pid - 1),
                    new Term<TVar>(this.NumPackets, this.packetAdmitOrDrop[pid]));
                this.Solver.AddEqZeroConstraint(sumPoly);
            }
        }

        void ComputeOrder()
        {
            double bigM = this.NumPackets + this.MaxRank;
            this.dequeueAfter = new Dictionary<(int, int), TVar>();
            for (int pid = 0; pid < this.NumPackets; pid++) {
                for (int pid2 = 0; pid2 < this.NumPackets; pid2++) {
                    if (pid2 == pid) {
                        continue;
                    }
                    this.dequeueAfter[(pid, pid2)] = EncodingUtils<TVar, TSolution>.IsLeq(
                        this.Solver, this.packetWeightVar[pid], this.packetWeightVar[pid2],
                        bigM, 0);
                }
            }
        }

        /// <summary>
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected virtual void ComputeCost()
        {
            throw new Exception("not implemented....");
        }

        /// <summary>
        /// additional constraints for the modified variants.
        /// </summary>
        protected virtual void AddOtherConstraints()
        {
        }

        /// <summary>
        /// Encode AIFO.
        /// </summary>
        public OptimizationEncoding<TVar, TSolution> Encoding(
            Dictionary<int, TVar> preRankVariables = null,
            Dictionary<int, int> rankEqualityConstraints = null,
            bool verbose = false)
        {
            Utils.logger("create variables", verbose);
            this.CreateVariables(preRankVariables, rankEqualityConstraints);

            Utils.logger("initialize variables", verbose);
            this.InitializeVariables();

            Utils.logger("ensure ranks are equal to input ranks", verbose);
            this.EnsureRankEquality();

            Utils.logger("compute fraction of availabe space in queue", verbose);
            this.ComputeFractionAvailableSpace();

            Utils.logger("compute quantile of the packet rank", verbose);
            this.ComputeQuantile();

            Utils.logger("admit or drop the packet", verbose);
            this.AdmitOrDrop();

            Utils.logger("Assign weights to packets based on order", verbose);
            this.AssignWeights();

            Utils.logger("Compute order of packets", verbose);
            this.ComputeOrder();

            Utils.logger("Adding additional constraints for modified versions", verbose);
            this.AddOtherConstraints();

            Utils.logger("Compute cost", verbose);
            this.ComputeCost();
            var objective = new Polynomial<TVar>(new Term<TVar>(-1, this.cost));
            return new PIFOOptimizationEncoding<TVar, TSolution>
            {
                GlobalObjective = this.cost,
                MaximizationObjective = objective,
                RankVariables = this.packetRankVar,
            };
        }

        /// <summary>
        /// Get the optimization solution from the solver.
        /// </summary>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var packetRanks = new Dictionary<int, double>();
            var packetOrder = new Dictionary<int, int>();
            var packetAdmitOrDrop = new Dictionary<int, int>();

            Console.WriteLine("======== initial weights");
            foreach (var w in initialWindow) {
                Console.WriteLine(this.Solver.GetVariable(solution, w));
            }

            for (int pid = 0; pid < this.NumPackets; pid++) {
                packetRanks[pid] = this.Solver.GetVariable(solution, this.packetRankVar[pid]);
                packetOrder[pid] = 0;
                for (int pid2 = 0; pid2 < this.NumPackets; pid2++) {
                    if (pid == pid2) {
                        continue;
                    }
                    int dequeueAfter = Convert.ToInt32(this.Solver.GetVariable(solution, this.dequeueAfter[(pid, pid2)]));
                    packetOrder[pid] += dequeueAfter;
                }
                var weight = this.Solver.GetVariable(solution, this.packetWeightVar[pid]);
                var quantile = this.Solver.GetVariable(solution, this.quantiles[pid]);
                packetAdmitOrDrop[pid] = Convert.ToInt32(this.Solver.GetVariable(solution, this.packetAdmitOrDrop[pid]));
                var availableSpace = this.Solver.GetVariable(solution, this.availableSpace[pid]);
                Console.WriteLine("packet = " + pid);
                Console.WriteLine("           rank = " + packetRanks[pid]);
                Console.WriteLine("           weight = " + weight);
                Console.WriteLine("           quantile = " + quantile);
                Console.WriteLine("           admit = " + packetAdmitOrDrop[pid]);
                Console.WriteLine("           order = " + packetOrder[pid]);
                Console.WriteLine("           available space = " + availableSpace);
            }

            return new PIFOOptimizationSolution
            {
                Ranks = packetRanks,
                Order = packetOrder,
                Admit = packetAdmitOrDrop,
                Cost = this.Solver.GetVariable(solution, this.cost),
            };
        }
    }
}