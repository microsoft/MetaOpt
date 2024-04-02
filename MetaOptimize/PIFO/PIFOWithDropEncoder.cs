namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using Gurobi;

    /// <summary>
    /// PIFO with limited queue size.
    /// </summary>
    public class PIFOWithDropEncoder<TVar, TSolution> : PIFOOptimalEncoder<TVar, TSolution>
    {
        /// <summary>
        /// queue size.
        /// </summary>
        public int MaxQueueSize { get; set; }

        /// <summary>
        /// = 1 if packet admitted to the queue. = 0 if dropped.
        /// </summary>
        public IDictionary<int, TVar> packetAdmitOrDrop { get; set; }

        /// <summary>
        /// The constructor.
        /// </summary>
        public PIFOWithDropEncoder(ISolver<TVar, TSolution> solver, int NumPackets, int maxRank, int maxQueueSize)
            : base(solver, NumPackets, maxRank)
        {
            this.MaxQueueSize = maxQueueSize;
        }

        /// <summary>
        /// create additional variables.
        /// </summary>
        protected override void CreateAdditionalVariables()
        {
            packetAdmitOrDrop = new Dictionary<int, TVar>();
            for (int pid = 0; pid < this.NumPackets; pid++) {
                packetAdmitOrDrop[pid] = this.Solver.CreateVariable("admit_" + pid, GRB.BINARY);
            }
        }

        private void EnforceQueueSize()
        {
            var capConstr = new Polynomial<TVar>(new Term<TVar>(-this.MaxQueueSize));
            for (int pid = 0; pid < this.NumPackets; pid++) {
                capConstr.Add(new Term<TVar>(1, this.packetAdmitOrDrop[pid]));
            }
            this.Solver.AddLeqZeroConstraint(capConstr);
        }

        /// <summary>
        /// additional constraints for the modified variants.
        /// </summary>
        protected override void AddOtherConstraints()
        {
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