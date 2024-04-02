namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO.Pipes;
    using Gurobi;

    /// <summary>
    /// SP-PIFO Encoder.
    /// </summary>
    public class SPPIFOEncoder<TVar, TSolution> : IEncoder<TVar, TSolution>
    {
        /// <summary>
        /// The underlying solver.
        /// </summary>
        public ISolver<TVar, TSolution> Solver { get; set; }

        /// <summary>
        /// variable showing whether the $i$-th incomming packet is placed in the $j$-th queue.
        /// </summary>
        public Dictionary<(int, int), TVar> queuePlacementVar { get; set; }

        /// <summary>
        /// variable showing rank of packet i.
        /// </summary>
        public Dictionary<int, TVar> packetRankVar { get; set; }

        /// <summary>
        /// variable showing lb on rank of packets admitted to queue j.
        /// </summary>
        public Dictionary<(int, int), TVar> queueRankVar { get; set; }

        /// <summary>
        /// lb on rank of queue j after applying push down.
        /// </summary>
        public Dictionary<(int, int), TVar> queueRankVarAfterPD  { get; set; }

        /// <summary>
        /// weight of packet i.
        /// </summary>
        public Dictionary<int, TVar> packetWeightVar { get; set; }

        /// <summary>
        /// if packet i should be dequeued after packet j.
        /// </summary>
        public Dictionary<(int, int), TVar> dequeueAfter { get; set; }

        // /// <summary>
        // /// = 1 if push down should happen at the time enqueuing i-th packet.
        // /// </summary>
        // public Dictionary<int, TVar> pushdown { get; set; }

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
        /// number of queues.
        /// </summary>
        public int NumQueues;

        /// <summary>
        /// maximum rank of a packets.
        /// </summary>
        public int MaxRank;

        /// <summary>
        /// creating a new instance of SP-PIFO encoder.
        /// </summary>
        public SPPIFOEncoder(ISolver<TVar, TSolution> solver, int numPackets, int numQueues, int maxRank)
        {
            this.Solver = solver;
            this.MaxRank = maxRank;
            this.NumPackets = numPackets;
            this.NumQueues = numQueues;
        }

        /// <summary>
        /// initialize variables.
        /// </summary>
        private void CreateVariables(Dictionary<int, TVar> preRankVariables,
            Dictionary<int, int> rankEqualityConstraints)
        {
            this.packetRankVar = new Dictionary<int, TVar>();
            this.queuePlacementVar = new Dictionary<(int, int), TVar>();
            this.packetWeightVar = new Dictionary<int, TVar>();
            this.queueRankVar = new Dictionary<(int, int), TVar>();
            this.queueRankVarAfterPD = new Dictionary<(int, int), TVar>();
            // this.pushdown = new Dictionary<int, TVar>();
            this.dequeueAfter = new Dictionary<(int, int), TVar>();
            for (int packetID = 0; packetID < this.NumPackets; packetID++) {
                if (preRankVariables == null) {
                    this.packetRankVar[packetID] = this.Solver.CreateVariable("rank_" + packetID, GRB.INTEGER, lb: 0, ub: this.MaxRank);
                } else {
                    this.packetRankVar[packetID] = preRankVariables[packetID];
                }
                for (int queueID = 0; queueID < this.NumQueues; queueID++) {
                    this.queuePlacementVar[(packetID, queueID)] = this.Solver.CreateVariable("place_" + packetID + "_" + queueID, GRB.BINARY);
                    this.queueRankVar[(packetID, queueID)] = this.Solver.CreateVariable("queue_rank_" + packetID + "_" + queueID);
                    this.queueRankVarAfterPD[(packetID, queueID)] = this.Solver.CreateVariable("queue_rank_after_pd_" + packetID + "_" + queueID);
                }
                for (int secondPacket = 0; secondPacket < this.NumPackets; secondPacket++) {
                    if (secondPacket == packetID) {
                        continue;
                    }
                    this.dequeueAfter[(packetID, secondPacket)] = this.Solver.CreateVariable("dequeu_" + packetID + "_after_" + secondPacket, GRB.BINARY);
                }
                this.packetWeightVar[packetID] = this.Solver.CreateVariable("packet_weight_" + packetID);
                // this.pushdown[packetID] = this.Solver.CreateVariable("push_down_" + packetID, type: GRB.BINARY);
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

        /// <summary>
        /// the push down process in SP-PIFO.
        /// </summary>
        protected virtual void ApplyPushDown()
        {
            // double epsilon = 1.0 / (1 + this.MaxRank);
            for (int pid = 0; pid < this.NumPackets; pid++) {
                // // pd <= 1 + \epsilon (lb_{i, n} - r_i)
                // var constr1 = new Polynomial<TVar>(
                //     new Term<TVar>(1, this.pushdown[pid]),
                //     new Term<TVar>(-1),
                //     new Term<TVar>(-epsilon, this.queueRankVar[(pid, this.NumQueues - 1)]),
                //     new Term<TVar>(epsilon, this.packetRankVar[pid]));
                // this.Solver.AddLeqZeroConstraint(constr1);

                // // pd >= \epsilon (lb_{i, n} - r_i)
                // var constr2 = new Polynomial<TVar>(
                //     new Term<TVar>(-1, this.pushdown[pid]),
                //     new Term<TVar>(epsilon, this.queueRankVar[(pid, this.NumQueues - 1)]),
                //     new Term<TVar>(-epsilon, this.packetRankVar[pid]));
                // this.Solver.AddLeqZeroConstraint(constr2);

                // var lin1 = EncodingUtils<TVar, TSolution>.LinearizeMultContinAndBinary(this.Solver,
                //     this.packetRankVar[pid], this.pushdown[pid], this.MaxRank);
                // var lin2 = EncodingUtils<TVar, TSolution>.LinearizeMultContinAndBinary(this.Solver,
                //     this.queueRankVar[(pid, this.NumQueues - 1)], this.pushdown[pid], this.MaxRank);
                // // l'_{ij} = l_{ij} + pd * (r_i - lb_{i, N})
                // for (int qid = 0; qid < this.NumQueues; qid++) {
                //     var constr3 = new Polynomial<TVar>(
                //         new Term<TVar>(-1, this.queueRankVarAfterPD[(pid, qid)]),
                //         new Term<TVar>(1, this.queueRankVar[(pid, qid)]),
                //         new Term<TVar>(1, lin1),
                //         new Term<TVar>(-1, lin2));
                //     this.Solver.AddEqZeroConstraint(constr3);
                // }

                // pdBias = max(lb_{i, N} - r_i, 0)
                var biasPoly = new Polynomial<TVar>(
                    new Term<TVar>(1, this.queueRankVar[(pid, this.NumQueues - 1)]),
                    new Term<TVar>(-1, this.packetRankVar[pid]));

                var pdBias = EncodingUtils<TVar, TSolution>.MaxTwoVar(
                    this.Solver,
                    biasPoly,
                    new Polynomial<TVar>(new Term<TVar>(0)),
                    this.MaxRank);

                // l'_{i, j} = l_{ij} - pdBias
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    var constr = new Polynomial<TVar>(
                        new Term<TVar>(-1, this.queueRankVarAfterPD[(pid, qid)]),
                        new Term<TVar>(1, this.queueRankVar[(pid, qid)]),
                        new Term<TVar>(-1, pdBias));
                    this.Solver.AddEqZeroConstraint(constr);
                }
            }
        }

        /// <summary>
        /// Ensure we admit packets to one of the queues.
        /// </summary>
        protected virtual void EnsureAtLeastOneQueue()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                var sumOverQ = new Polynomial<TVar>(new Term<TVar>(-1));
                for (int qid = 0; qid < this.NumQueues; qid++)
                {
                    sumOverQ.Add(new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]));
                }
                // sum f over j = 1
                this.Solver.AddEqZeroConstraint(sumOverQ);
            }
        }

        /// <summary>
        /// enqueue the i-th packet on the FIFO queue that matches.
        /// </summary>
        protected virtual void ChooseQueue()
        {
            double epsilon = 1.0 / (1 + this.MaxRank);
            double miu = epsilon / 2;
            for (int pid = 0; pid < this.NumPackets; pid++) {
                for (int qid = 0; qid < this.NumQueues; qid++)
                {
                    CheckQueueLB(epsilon, pid, qid);
                    CheckQueueUB(epsilon, miu, pid, qid);
                }
            }
            EnsureAtLeastOneQueue();
            AdditionalConstraintOnPlacement();
        }

        /// <summary>
        /// ensure rank of packet \leq priority of the previous queue.
        /// </summary>
        protected virtual void CheckQueueUB(double epsilon, double miu, int pid, int qid)
        {
            if (qid <= 0) {
                return;
            }
            // f <= 1 + epsilon (l_{i,j-1} - r_i) - miu
            var constr2 = new Polynomial<TVar>(
                new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]),
                new Term<TVar>(-1 + miu),
                new Term<TVar>(-epsilon, this.queueRankVarAfterPD[(pid, qid - 1)]),
                new Term<TVar>(epsilon, this.packetRankVar[pid]));
            this.Solver.AddLeqZeroConstraint(constr2);
        }

        /// <summary>
        /// ensure rank of packet \geq priority of queue.
        /// </summary>
        protected virtual void CheckQueueLB(double epsilon, int pid, int qid)
        {
            // f <= 1 + epsilon (r_i - l_{i,j})
            var constr1 = new Polynomial<TVar>(
                new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]),
                new Term<TVar>(-1),
                new Term<TVar>(-epsilon, this.packetRankVar[pid]),
                new Term<TVar>(epsilon, this.queueRankVarAfterPD[(pid, qid)]));
            this.Solver.AddLeqZeroConstraint(constr1);
        }

        /// <summary>
        /// additional constraint on packet placement (for modified versions).
        /// </summary>
        protected virtual void AdditionalConstraintOnPlacement()
        {
        }

        private void ApplyPushUp()
        {
            for (int pid = 0; pid < this.NumPackets - 1; pid++) {
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    var biasPoly = new Polynomial<TVar>(
                        new Term<TVar>(1, this.packetRankVar[pid]),
                        new Term<TVar>(-1, this.queueRankVarAfterPD[(pid, qid)]));

                    var puBias = EncodingUtils<TVar, TSolution>.LinearizeMultGenContinAndBinary(this.Solver,
                        biasPoly, this.queuePlacementVar[(pid, qid)], this.MaxRank);

                    var constr = new Polynomial<TVar>(
                        new Term<TVar>(-1, this.queueRankVar[(pid + 1, qid)]),
                        new Term<TVar>(1, this.queueRankVarAfterPD[(pid, qid)]),
                        new Term<TVar>(1, puBias));
                    this.Solver.AddEqZeroConstraint(constr);

                    // var lin1 = EncodingUtils<TVar, TSolution>.LinearizeMultNonNegContinAndBinary(this.Solver,
                    //     this.packetRankVar[pid], this.queuePlacementVar[(pid, qid)], this.MaxRank);
                    // var lin2 = EncodingUtils<TVar, TSolution>.LinearizeMultNonNegContinAndBinary(this.Solver,
                    //     this.queueRankVarAfterPD[(pid, qid)], this.queuePlacementVar[(pid, qid)], this.MaxRank);
                    // var constr = new Polynomial<TVar>(
                    //     new Term<TVar>(-1, this.queueRankVar[(pid + 1, qid)]),
                    //     new Term<TVar>(1, this.queueRankVarAfterPD[(pid, qid)]),
                    //     new Term<TVar>(1, lin1),
                    //     new Term<TVar>(-1, lin2));
                    // this.Solver.AddEqZeroConstraint(constr);
                }
            }
        }

        /// <summary>
        /// assign weights to dequeue accordingly.
        /// </summary>
        protected virtual void AssignWeights()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                var sumPoly = new Polynomial<TVar>(
                    new Term<TVar>(-1, this.packetWeightVar[pid]),
                    new Term<TVar>(-1 * pid));
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    sumPoly.Add(new Term<TVar>((qid + 1) * this.NumPackets, this.queuePlacementVar[(pid, qid)]));
                    // var lin1 = EncodingUtils<TVar, TSolution>.LinearizeMultContinAndBinary(this.Solver,
                    //     this.numPacketsInEachQueue[(pid, qid)], this.queuePlacementVar[(pid, qid)], this.NumPackets);
                    // sumPoly.Add(new Term<TVar>(-1, lin1));
                }
                this.Solver.AddEqZeroConstraint(sumPoly);
            }
        }

        /// <summary>
        /// compute the order based on the weights.
        /// </summary>
        protected virtual void ComputeOrder()
        {
            double epsilon = 1.0 / (this.NumPackets * this.NumQueues);
            for (int pid = 0; pid < this.NumPackets; pid++) {
                for (int pid2 = 0; pid2 < this.NumPackets; pid2++) {
                    if (pid2 == pid) {
                        continue;
                    }
                    var constr1 = new Polynomial<TVar>(
                        new Term<TVar>(1, this.dequeueAfter[(pid, pid2)]),
                        new Term<TVar>(-1),
                        new Term<TVar>(-epsilon, this.packetWeightVar[pid2]),
                        new Term<TVar>(epsilon, this.packetWeightVar[pid]));
                    this.Solver.AddLeqZeroConstraint(constr1);

                    var constr2 = new Polynomial<TVar>(
                        new Term<TVar>(-1, this.dequeueAfter[(pid, pid2)]),
                        new Term<TVar>(epsilon, this.packetWeightVar[pid2]),
                        new Term<TVar>(-epsilon, this.packetWeightVar[pid]));
                    this.Solver.AddLeqZeroConstraint(constr2);
                }
            }
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

        private void InitializeVariables() {
            for (int qid = 0; qid < this.NumQueues; qid++) {
                // initial bound for queues.
                this.Solver.AddEqZeroConstraint(new Polynomial<TVar>(
                    new Term<TVar>(1, this.queueRankVar[(0, qid)])));
                // initial num packets in each queue;
                // this.Solver.AddEqZeroConstraint(new Polynomial<TVar>(
                //     new Term<TVar>(1, this.numPacketsInEachQueue[(0, qid)])));
            }
        }

        /// <summary>
        /// additional constraints for the modified variants.
        /// </summary>
        protected virtual void AddOtherConstraints()
        {
        }

        /// <summary>
        /// Encode the optimal.
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

            Utils.logger("apply push down if necessary", verbose);
            this.ApplyPushDown();

            Utils.logger("decide which queue to use", verbose);
            this.ChooseQueue();

            Utils.logger("apply push up", verbose);
            this.ApplyPushUp();

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
        /// compute the cost of an ordering of packets.
        /// </summary>
        protected virtual void ComputeCost()
        {
            throw new Exception("not implemented....");
        }

        /// <summary>
        /// return whether packet is admitted to the queue.
        /// </summary>
        protected virtual int GetAdmitSolution(TSolution solution, int packetID)
        {
            return 1;
        }

        /// <summary>
        /// Get the optimization solution from the solver.
        /// </summary>
        public OptimizationSolution GetSolution(TSolution solution)
        {
            var packetRanks = new Dictionary<int, double>();
            var packetOrder = new Dictionary<int, int>();
            var packetAdmit = new Dictionary<int, int>();

            for (int pid = 0; pid < this.NumPackets; pid++) {
                packetRanks[pid] = this.Solver.GetVariable(solution, this.packetRankVar[pid]);
                packetOrder[pid] = 0;
                for (int pid2 = 0; pid2 < this.NumPackets; pid2++) {
                    if (pid == pid2) {
                        continue;
                    }
                    // if (pid < pid2) {
                    int dequeueAfter = Convert.ToInt32(this.Solver.GetVariable(solution, this.dequeueAfter[(pid, pid2)]));
                    // } else {
                    //     dequeueAfter = 1 - Convert.ToInt32(this.Solver.GetVariable(solution, this.dequeueAfter[(pid2, pid)]));
                    // }
                    Console.WriteLine("packet " + pid + " deque after " + pid2 + " = " + dequeueAfter);
                    packetOrder[pid] += dequeueAfter;
                }

                for (int place = 0; place < this.NumQueues; place++) {
                    var lb = this.Solver.GetVariable(solution, this.queueRankVar[(pid, place)]);
                    Console.WriteLine(" lb of queue " + place + " when entering pkt " + pid + " = " + lb);
                }
                var weight = this.Solver.GetVariable(solution, this.packetWeightVar[pid]);
                Console.WriteLine(" weight of packet " + pid + " = " + weight);
                packetAdmit[pid] = this.GetAdmitSolution(solution, pid);
            }

            return new PIFOOptimizationSolution
            {
                Ranks = packetRanks,
                Order = packetOrder,
                Cost = this.Solver.GetVariable(solution, this.cost),
                Admit = packetAdmit,
            };
        }
    }
}