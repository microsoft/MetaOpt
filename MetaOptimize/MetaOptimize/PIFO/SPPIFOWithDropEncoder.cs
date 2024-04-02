namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Gurobi;

    /// <summary>
    /// SP-PIFO with packet drop encoder.
    /// </summary>
    public class SPPIFOWithDropEncoder<TVar, TSolution> : SPPIFOEncoder<TVar, TSolution>
    {
        /// <summary>
        /// total queue size.
        /// </summary>
        public int totalQueueSize { get; set; }

        /// <summary>
        /// max queue size.
        /// </summary>
        public double maxQueueSize { get; set; }

        /// <summary>
        /// = 1 if packet admitted to the queue. = 0 if dropped.
        /// </summary>
        public IDictionary<int, TVar> packetAdmitOrDrop { get; set; }

        /// <summary>
        /// number of packets in each queue at the time of enqueuing the i-th packet.
        /// </summary>
        public Dictionary<(int, int), TVar> numPacketsInEachQueue { get; set; }

        /// <summary>
        /// The constructor.
        /// </summary>
        public SPPIFOWithDropEncoder(ISolver<TVar, TSolution> solver, int numPackets,
            int numQueues, int maxRank, int totalQueueSize) : base(solver, numPackets, numQueues, maxRank)
        {
            this.totalQueueSize = totalQueueSize;
            Debug.Assert(this.totalQueueSize % numQueues == 0);
            this.maxQueueSize = totalQueueSize / numQueues;
            Console.WriteLine(this.maxQueueSize);
        }

        /// <summary>
        /// create additional variables.
        /// </summary>
        protected override void CreateAdditionalVariables()
        {
            this.packetAdmitOrDrop = new Dictionary<int, TVar>();
            this.numPacketsInEachQueue = new Dictionary<(int, int), TVar>();
            for (int pid = 0; pid < this.NumPackets; pid++) {
                this.packetAdmitOrDrop[pid] = this.Solver.CreateVariable("admit_" + pid, GRB.BINARY);
                for (int queueID = 0; queueID < this.NumQueues; queueID++) {
                    this.numPacketsInEachQueue[(pid, queueID)] = this.Solver.CreateVariable("num_pkts_in_q_" + pid + "_" + queueID);
                }
            }
        }

        private void InitializeAdditionalVariables()
        {
            // initial num packets in each queue;
            for (int queueID = 0; queueID < this.NumQueues; queueID++) {
                this.Solver.AddEqZeroConstraint(new Polynomial<TVar>(
                    new Term<TVar>(1, this.numPacketsInEachQueue[(0, queueID)])));
            }
        }

        private void NumPacketsPerQueue()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                for (int qid = 0; qid < this.NumQueues; qid++)
                {
                    if (pid < this.NumPackets - 1) {
                        var constr = new Polynomial<TVar>(
                            new Term<TVar>(-1, this.numPacketsInEachQueue[(pid + 1, qid)]),
                            new Term<TVar>(1, this.numPacketsInEachQueue[(pid, qid)]),
                            new Term<TVar>(1, this.queuePlacementVar[(pid, qid)]));
                        this.Solver.AddEqZeroConstraint(constr);
                    }
                }
            }
        }

        private void EnforceQueueSize()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                var capacityConstr = new Polynomial<TVar>(new Term<TVar>(1));
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    var mult = EncodingUtils<TVar, TSolution>.LinearizeMultNonNegContinAndBinary(this.Solver,
                        this.numPacketsInEachQueue[(pid, qid)], this.queuePlacementVar[(pid, qid)], this.NumPackets);
                    capacityConstr.Add(new Term<TVar>(1, mult));
                }
                // TODO: Clean this up later.
                var capSize = new Polynomial<TVar>(new Term<TVar>(this.maxQueueSize));
                EncodingUtils<TVar, TSolution>.IsLeq(this.Solver, this.packetAdmitOrDrop[pid],
                    capacityConstr, capSize, this.totalQueueSize + 1, 0.5);
            }
        }

        /// <summary>
        /// Compute weights considering packet drop.
        /// </summary>
        protected override void AssignWeights()
        {
            for (int pid = 0; pid < this.NumPackets; pid++) {
                var sumPoly = new Polynomial<TVar>(
                    new Term<TVar>(-1, this.packetWeightVar[pid]),
                    new Term<TVar>(-1 * pid));
                for (int qid = 0; qid < this.NumQueues; qid++) {
                    sumPoly.Add(new Term<TVar>((qid + 1) * this.NumPackets, this.queuePlacementVar[(pid, qid)]));
                }
                sumPoly.Add(new Term<TVar>((this.NumQueues + 1) * this.NumPackets, this.packetAdmitOrDrop[pid]));
                this.Solver.AddEqZeroConstraint(sumPoly);
            }
        }

        /// <summary>
        /// compute the order based on the weights.
        /// </summary>
        protected override void ComputeOrder()
        {
            double epsilon = 1.0 / (this.NumPackets * (this.NumQueues * 2 + 1));
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

        /// <summary>
        /// additional constraints for the modified variants.
        /// </summary>
        protected override void AddOtherConstraints()
        {
            this.InitializeAdditionalVariables();
            this.NumPacketsPerQueue();
            this.EnforceQueueSize();
        }

        /// <summary>
        /// return whether packet is admitted to the queue.
        /// </summary>
        protected override int GetAdmitSolution(TSolution solution, int packetID)
        {
            return Convert.ToInt32(this.Solver.GetVariable(solution, this.packetAdmitOrDrop[packetID]));
        }
    }
}