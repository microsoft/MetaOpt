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
        public override void AddMinimizationConstraints(Polynomial<TVar> objective, bool noPrimalDual, bool verbose = false)
        {
            Utils.logger("using primal dual encoding", verbose, Utils.LogState.WARNING);
            // adding primal constraints
            Utils.logger("adding primal constraints", verbose);
            this.AddConstraints();

            if (!noPrimalDual) {
                Dictionary<int, TVar> leqDualVariables = new Dictionary<int, TVar>();
                Dictionary<int, TVar> eqDualVariables = new Dictionary<int, TVar>();
                IList<TVar> dualObjectiveTerms = new List<TVar>();
                IList<Polynomial<TVar>> dualObjectiveCoeff = new List<Polynomial<TVar>>();
                Utils.logger("computing dual objective coefficients for inequality constraints.", verbose);
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
                Utils.logger("computing dual objective coefficients for equality constraints.", verbose);
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
                Utils.logger("ensuring primal objective == dual objective.", verbose);
                // adding primal = dual constraint
                var primalObjective = objective.Negate();
                this.solver.AddEqZeroConstraint(dualObjectiveCoeff, dualObjectiveTerms, primalObjective);
                // adding bound on dual variables
                Utils.logger("ensuring bound on dual variables.", verbose);
                foreach (var (idx, var) in leqDualVariables) {
                    this.solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, var)));
                }
                // adding dual constraints
                Utils.logger("adding dual constraints.", verbose);
                var variableToDualConstraint = new Dictionary<TVar, Polynomial<TVar>>();
                foreach (var variable in this.Variables) {
                    if (this.constantVariables.Contains(variable)) {
                        continue;
                    }
                    var objCoeff = objective.Derivative(variable);
                    variableToDualConstraint[variable] = new Polynomial<TVar>(new Term<TVar>(-1 * objCoeff));
                }

                for (int i = 0; i < leqZeroConstraints.Count; i++) {
                    var leqConstraint = leqZeroConstraints[i];
                    foreach (Term<TVar> term in leqConstraint.Terms) {
                        if (term.isInSetOrConst(this.constantVariables)) {
                            continue;
                        }
                        TVar variable = term.Variable.Value;
                        var deriv = leqConstraint.Derivative(variable);
                        if (deriv != 0) {
                            variableToDualConstraint[variable].Add(new Term<TVar>(deriv, leqDualVariables[i]));
                        }
                    }
                }

                for (int i = 0; i < eqZeroConstraints.Count; i++) {
                    var eqConstraint = eqZeroConstraints[i];
                    foreach (Term<TVar> term in eqConstraint.Terms) {
                        if (term.isInSetOrConst(this.constantVariables)) {
                            continue;
                        }
                        TVar variable = term.Variable.Value;
                        var deriv = eqConstraint.Derivative(variable);
                        if (deriv != 0) {
                            variableToDualConstraint[variable].Add(new Term<TVar>(deriv, eqDualVariables[i]));
                        }
                    }
                }

                foreach (var (variable, dualConstr) in variableToDualConstraint) {
                    if (dualConstr.isallInSetOrConst(new HashSet<TVar>())) {
                        throw new System.Exception("should not be consant!!!");
                    }
                    this.solver.AddEqZeroConstraint(dualConstr);
                }

                // foreach (var variable in this.Variables) {
                //     if (this.constantVariables.Contains(variable)) {
                //         continue;
                //     }
                //     Polynomial<TVar> dualConstraint = new Polynomial<TVar>();
                //     for (int i = 0; i < leqZeroConstraints.Count; i++) {
                //         var leqConstraint = leqZeroConstraints[i];
                //         var deriv = leqConstraint.Derivative(variable);
                //         if (deriv != 0) {
                //             dualConstraint.Add(new Term<TVar>(deriv, leqDualVariables[i]));
                //         }
                //     }
                //     for (int i = 0; i < eqZeroConstraints.Count; i++) {
                //         var eqConstraint = eqZeroConstraints[i];
                //         var deriv = eqConstraint.Derivative(variable);
                //         if (deriv != 0) {
                //             dualConstraint.Add(new Term<TVar>(deriv, eqDualVariables[i]));
                //         }
                //     }
                //     var objCoeff = objective.Derivative(variable);
                //     dualConstraint.Add(new Term<TVar>(-1 * objCoeff));
                //     this.solver.AddEqZeroConstraint(dualConstraint);
                // }
            }
        }
    }
}