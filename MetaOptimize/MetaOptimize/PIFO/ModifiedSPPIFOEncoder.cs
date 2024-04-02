namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using Gurobi;

    /// <summary>
    /// the modified SPPIFO.
    /// </summary>
    public class ModifiedSPPIFOEncoder<TVar, TSolution> : SPPIFOEncoder<TVar, TSolution>
    {
        /// <summary>
        /// [0, splitRank) in the first set of queues.
        /// [splitRank, maxRank] in the second set of queues.
        /// </summary>
        protected int SplitRank { get; set; }

        /// <summary>
        /// queue [0, splitQueue) for lower ranks.
        /// queue [splitQueue, maxRank] for higher ranks.
        /// </summary>
        protected int SplitQueue { get; set; }

        /// <summary>
        /// mapping from mapping to coarse-grained priority class.
        /// </summary>
        protected Dictionary<int, TVar> packetClass { get; set; }

        /// <summary>
        /// The constructor.
        /// </summary>
        public ModifiedSPPIFOEncoder(ISolver<TVar, TSolution> solver, int numPackets, int splitQueue, int numQueues, int splitRank, int maxRank)
            : base(solver, numPackets, numQueues, maxRank)
        {
            this.SplitRank = splitRank;
            this.SplitQueue = splitQueue;
        }

        /// <summary>
        /// for the modified versions to create additional variables.
        /// </summary>
        protected override void CreateAdditionalVariables()
        {
            this.packetClass = new Dictionary<int, TVar>();
            for (int pid = 0; pid < this.NumPackets; pid++) {
                this.packetClass[pid] = this.Solver.CreateVariable("packet_class_" + pid, GRB.BINARY);
            }
        }

        /// <summary>
        /// ensure rank of packet \leq priority of the previous queue.
        /// </summary>
        protected override void CheckQueueUB(double epsilon, double miu, int pid, int qid)
        {
            if (qid <= 0 || qid == this.SplitQueue) {
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
        /// place the queue based on the priority class.
        /// </summary>
        protected override void AdditionalConstraintOnPlacement()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    if (qid < this.SplitQueue) {
                        // f <= 1 - packetClass
                        var constr = new Polynomial<TVar>(
                            new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]),
                            new Term<TVar>(-1),
                            new Term<TVar>(1, this.packetClass[pid]));
                        this.Solver.AddLeqZeroConstraint(constr);
                    } else {
                        // f <= packetClass
                        var constr = new Polynomial<TVar>(
                            new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]),
                            new Term<TVar>(-1, this.packetClass[pid]));
                        this.Solver.AddLeqZeroConstraint(constr);
                    }
                }
            }
        }

        /// <summary>
        /// the push down process in SP-PIFO.
        /// </summary>
        protected override void ApplyPushDown()
        {
            double bigM = this.MaxRank;
            for (int pid = 0; pid < this.NumPackets; pid++) {
                // biasH = max(L_{i, N} - r_i - M(1 - class_i), 0)
                var biasPolyH = new Polynomial<TVar>(
                    new Term<TVar>(1, this.queueRankVar[(pid, this.NumQueues - 1)]),
                    new Term<TVar>(-1, this.packetRankVar[pid]),
                    new Term<TVar>(-bigM),
                    new Term<TVar>(bigM, this.packetClass[pid]));

                var pdBiasH = EncodingUtils<TVar, TSolution>.MaxTwoVar(
                    this.Solver, biasPolyH,
                    new Polynomial<TVar>(new Term<TVar>(0)), this.MaxRank + bigM);

                // biasL = max(L_{i, s} - r_i - Mclass_i, 0)
                var biasPolyL = new Polynomial<TVar>(
                    new Term<TVar>(1, this.queueRankVar[(pid, this.SplitQueue - 1)]),
                    new Term<TVar>(-1, this.packetRankVar[pid]),
                    new Term<TVar>(-bigM, this.packetClass[pid]));

                var pdBiasL = EncodingUtils<TVar, TSolution>.MaxTwoVar(
                    this.Solver, biasPolyL,
                    new Polynomial<TVar>(new Term<TVar>(0)), this.MaxRank + bigM);

                // l'_{i, j} = l_{ij} - pdBiasL - pdBiasH
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    var constr = new Polynomial<TVar>(
                        new Term<TVar>(-1, this.queueRankVarAfterPD[(pid, qid)]),
                        new Term<TVar>(1, this.queueRankVar[(pid, qid)]));
                    if (qid < this.SplitQueue) {
                        constr.Add(new Term<TVar>(-1, pdBiasL));
                    } else {
                        constr.Add(new Term<TVar>(-1, pdBiasH));
                    }
                    this.Solver.AddEqZeroConstraint(constr);
                }
            }
        }

        /// <summary>
        /// keep track of packet class.
        /// </summary>
        protected override void AddOtherConstraints()
        {
            double epsilon = 1.0 / (1 + this.MaxRank);
            double miu = epsilon / 2;
            for (int pid = 0; pid < this.NumPackets; pid++) {
                // class <= 1 + epsilon (splitrank - r_i) - miu
                var constr1 = new Polynomial<TVar>(
                    new Term<TVar>(1, this.packetClass[pid]),
                    new Term<TVar>(-1 - epsilon * this.SplitRank + miu),
                    new Term<TVar>(epsilon, this.packetRankVar[pid]));
                this.Solver.AddLeqZeroConstraint(constr1);
                // class >= epsilon (splitrank - r_i)
                var constr2 = new Polynomial<TVar>(
                    new Term<TVar>(-1, this.packetClass[pid]),
                    new Term<TVar>(epsilon * this.SplitRank),
                    new Term<TVar>(-epsilon, this.packetRankVar[pid]));
                this.Solver.AddLeqZeroConstraint(constr2);
            }
        }
    }
}