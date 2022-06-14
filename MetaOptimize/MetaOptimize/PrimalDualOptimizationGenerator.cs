// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;
    using System.Linq;
    using ZenLib;

    /// <summary>
    /// An optimization encoder that automatically adds the primal-dual conditions.
    /// </summary>
    public class PrimalDualOptimizationGenerator<TVar, TSolution> : KktOptimizationGenerator<TVar, TSolution>
    {
        /// <summary>
        /// Creates a new instance of the <see cref="KktOptimizationGenerator{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="variables">The encoding variables.</param>
        /// <param name="constVariables">The variables to consider constant for the optimization.</param>
        /// <param name="solver">The solver.</param>
        public PrimalDualOptimizationGenerator(ISolver<TVar, TSolution> solver, ISet<TVar> variables, ISet<TVar> constVariables)
            : base(solver, variables, constVariables)
        {
        }

        /// <summary>
        /// Get the primal dual constraints for minimal solution.
        /// </summary>
        /// <param name="objective">The objective.</param>
        /// <param name="noPrimalDual"> To not solve through Primal-Dual.</param>
        public override void AddMinimizationConstraints(Polynomial<TVar> objective, bool noPrimalDual)
        {
            // adding primal constraints
            this.AddConstraints();

            if (!noPrimalDual) {
                Dictionary<int, TVar> leqDualVariables = new Dictionary<int, TVar>();
                Dictionary<int, TVar> eqDualVariables = new Dictionary<int, TVar>();
                IList<TVar> dualObjectiveTerms = new List<TVar>();
                IList<Polynomial<TVar>> dualObjectiveCoeff = new List<Polynomial<TVar>>();
                for (int i = 0; i < leqZeroConstraints.Count; i++)
                {
                    var leqConstraint = this.leqZeroConstraints[i];
                    if (leqConstraint.isallInSetOrConst(this.constantVariables))
                    {
                        continue;
                    }
                    leqDualVariables[i] = this.solver.CreateVariable("dualleq_" + i);
                    var constraintRHS = leqConstraint.getRHS(this.constantVariables);
                    dualObjectiveTerms.Add(leqDualVariables[i]);
                    dualObjectiveCoeff.Add(constraintRHS.Negate());
                }
                for (int i = 0; i < eqZeroConstraints.Count; i++)
                {
                    var eqConstraint = this.eqZeroConstraints[i];
                    if (eqConstraint.isallInSetOrConst(this.constantVariables))
                    {
                        continue;
                    }
                    eqDualVariables[i] = this.solver.CreateVariable("dualeq_" + i);
                    var constraintRHS = eqConstraint.getRHS(this.constantVariables);
                    dualObjectiveTerms.Add(eqDualVariables[i]);
                    dualObjectiveCoeff.Add(constraintRHS.Negate());
                }
                // adding primal = dual constraint
                var primalObjective = objective.Negate();
                this.solver.AddEqZeroConstraint(dualObjectiveCoeff, dualObjectiveTerms, primalObjective);
                // adding bound on dual variables
                foreach (var (idx, var) in leqDualVariables) {
                    this.solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, var)));
                }
                // adding dual constraints
                foreach (var variable in this.Variables) {
                    if (this.constantVariables.Contains(variable)) {
                        continue;
                    }

                    Polynomial<TVar> dualConstraint = new Polynomial<TVar>();
                    for (int i = 0; i < leqZeroConstraints.Count; i++) {
                        var leqConstraint = leqZeroConstraints[i];
                        var deriv = leqConstraint.Derivative(variable);
                        if (deriv != 0) {
                            dualConstraint.Add(new Term<TVar>(deriv, leqDualVariables[i]));
                        }
                    }
                    for (int i = 0; i < eqZeroConstraints.Count; i++) {
                        var eqConstraint = eqZeroConstraints[i];
                        var deriv = eqConstraint.Derivative(variable);
                        if (deriv != 0) {
                            dualConstraint.Add(new Term<TVar>(deriv, eqDualVariables[i]));
                        }
                    }
                    var objCoeff = objective.Derivative(variable);
                    dualConstraint.Add(new Term<TVar>(-1 * objCoeff));
                    this.solver.AddEqZeroConstraint(dualConstraint);
                }
            }
        }
    }
}