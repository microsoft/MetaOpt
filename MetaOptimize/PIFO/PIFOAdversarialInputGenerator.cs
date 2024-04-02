namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using Gurobi;

    /// <summary>
    /// Meta-optimization utility functions for maximizing optimality gaps.
    /// </summary>
    public class PIFOAdversarialInputGenerator<TVar, TSolution>
    {
        /// <summary>
        /// number of packets.
        /// </summary>
        protected int NumPackets { get; set; }

        /// <summary>
        ///  maximum rank for a packet.
        /// </summary>
        protected int MaxRank { get; set; }

        /// <summary>
        ///  variables tracking rank of each packet.
        /// </summary>
        protected Dictionary<int, TVar> packetRankVars { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public PIFOAdversarialInputGenerator(int numPackets, int maxRank)
        {
            this.MaxRank = maxRank;
            this.NumPackets = numPackets;
        }

        private Dictionary<int, TVar> CreateRankVariables(ISolver<TVar, TSolution> solver)
        {
            var output = new Dictionary<int, TVar>();
            for (int pid = 0; pid < this.NumPackets; pid++) {
                output[pid] = solver.CreateVariable("rank_" + pid, GRB.INTEGER, lb: 0, ub: this.MaxRank);
            }
            return output;
        }

        /// <summary>
        /// Find an adversarial input that maximizes the optimality gap between two optimizations.
        /// </summary>
        public (PIFOOptimizationSolution, PIFOOptimizationSolution) MaximizeOptimalityGap(
            IEncoder<TVar, TSolution> optimalEncoder,
            IEncoder<TVar, TSolution> heuristicEncoder,
            bool cleanUpSolver = true,
            bool verbose = false)
        {
            if (optimalEncoder.Solver != heuristicEncoder.Solver)
            {
                throw new Exception("Solver mismatch between optimal and heuristic encoders.");
            }

            var solver = optimalEncoder.Solver;
            if (cleanUpSolver) {
                solver.CleanAll();
            }

            Utils.logger("creating rank variables.", verbose);
            this.packetRankVars = CreateRankVariables(solver);

            Utils.logger("generating optimal encoding.", verbose);
            var optimalEncoding = optimalEncoder.Encoding(preRankVariables: this.packetRankVars,
                verbose: verbose);
            Utils.logger("generating heuristic encoding.", verbose);
            var heuristicEncoding = heuristicEncoder.Encoding(preRankVariables: this.packetRankVars,
                verbose: verbose);

            Utils.logger("setting the objective.", verbose);
            var objective = new Polynomial<TVar>(
                        new Term<TVar>(-1, optimalEncoding.GlobalObjective),
                        new Term<TVar>(1, heuristicEncoding.GlobalObjective));
            var solution = solver.Maximize(objective, reset: true);

            return ((PIFOOptimizationSolution)optimalEncoder.GetSolution(solution),
                (PIFOOptimizationSolution)heuristicEncoder.GetSolution(solution));
        }
    }
}