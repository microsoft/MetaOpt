// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;

    /// <summary>
    /// An optimization encoder that automatically derives the KKT conditions.
    /// </summary>
    public class KktOptimizationGenerator<TVar, TSolution>
    {
        /// <summary>
        /// The solver being used.
        /// </summary>
        private ISolver<TVar, TSolution> solver;

        /// <summary>
        /// The constraints for polynomial less than or equal to zero.
        /// </summary>
        private IList<Polynomial<TVar>> leqZeroConstraints;

        /// <summary>
        /// The constraints for polynomial equals zero.
        /// </summary>
        private IList<Polynomial<TVar>> eqZeroConstraints;

        /// <summary>
        /// The constructed lambda variables for the KKT conditions.
        /// </summary>
        private IList<TVar> lambdaVariables;

        /// <summary>
        /// The constructed nu variables for the KKT conditions.
        /// </summary>
        private IList<TVar> nuVariables;

        /// <summary>
        /// The variables in the encoding.
        /// </summary>
        public ISet<TVar> Variables;

        /// <summary>
        /// The variables to avoid taking the derivative for.
        /// </summary>
        public ISet<TVar> AvoidDerivativeVariables;

        /// <summary>
        /// Creates a new instance of the <see cref="KktOptimizationGenerator{TVar, TSolution}"/> class.
        /// </summary>
        /// <param name="variables">The encoding variables.</param>
        /// <param name="avoidDerivativeVariables">The variables to avoid the deriviatve for.</param>
        /// <param name="solver">The solver.</param>
        public KktOptimizationGenerator(ISolver<TVar, TSolution>  solver, ISet<TVar> variables, ISet<TVar> avoidDerivativeVariables)
        {
            this.Variables = variables;
            this.solver = solver;
            this.leqZeroConstraints = new List<Polynomial<TVar>>();
            this.eqZeroConstraints = new List<Polynomial<TVar>>();
            this.lambdaVariables = new List<TVar>();
            this.nuVariables = new List<TVar>();
            this.AvoidDerivativeVariables = avoidDerivativeVariables;
        }

        /// <summary>
        /// Add a constraint that a polynomial is less than or equal to zero.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(Polynomial<TVar> polynomial)
        {
            this.leqZeroConstraints.Add(polynomial);
        }

        /// <summary>
        /// Add a constraint that a polynomial is equal to zero.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(Polynomial<TVar> polynomial)
        {
            this.eqZeroConstraints.Add(polynomial);
        }

        /// <summary>
        /// Get the constraints for the encoding.
        /// </summary>
        /// <returns>The result as a Zen boolean expression.</returns>
        public void AddConstraints()
        {
            foreach (var leqZeroConstraint in this.leqZeroConstraints)
            {
                this.solver.AddLeqZeroConstraint(leqZeroConstraint);
            }

            foreach (var eqZeroConstraint in this.eqZeroConstraints)
            {
                this.solver.AddEqZeroConstraint(eqZeroConstraint);
            }
        }

        /// <summary>
        /// Get the KKT constraints for maximal solution.
        /// </summary>
        /// <param name="objective">The objective.</param>
        /// <returns>The result as a Zen boolean expression.</returns>
        public void AddMaximizationConstraints(Polynomial<TVar> objective)
        {
            this.AddMinimizationConstraints(objective.Negate());
        }

        /// <summary>
        /// Get the KKT constraints for minimal solution.
        /// </summary>
        /// <param name="objective">The objective.</param>
        /// <returns>The result as a Zen boolean expression.</returns>
        public void AddMinimizationConstraints(Polynomial<TVar> objective)
        {
            foreach (var leqZeroConstraint in this.leqZeroConstraints)
            {
                this.solver.AddLeqZeroConstraint(leqZeroConstraint);
            }

            foreach (var eqZeroConstraint in this.eqZeroConstraints)
            {
                this.solver.AddEqZeroConstraint(eqZeroConstraint);
            }

            for (int i = 0; i < this.leqZeroConstraints.Count; i++)
            {
                var leqConstraint = this.leqZeroConstraints[i];
                var lambda = this.solver.CreateVariable("lambda_" + i);
                this.lambdaVariables.Add(lambda);

                this.solver.AddLeqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(-1, lambda)));
                this.solver.AddOrEqZeroConstraint(new Polynomial<TVar>(new Term<TVar>(1, lambda)), leqConstraint);
            }

            for (int i = 0; i < this.eqZeroConstraints.Count; i++)
            {
                this.nuVariables.Add(this.solver.CreateVariable("nu_" + i));
            }

            foreach (var variable in this.Variables)
            {
                if (this.AvoidDerivativeVariables.Contains(variable))
                {
                    continue;
                }

                var deriv = objective.Derivative(variable);
                var total = new Polynomial<TVar>(new Term<TVar>(deriv));

                for (int i = 0; i < this.leqZeroConstraints.Count; i++)
                {
                    var derivative = this.leqZeroConstraints[i].Derivative(variable);
                    total.Terms.Add(new Term<TVar>(derivative, this.lambdaVariables[i]));
                }

                for (int i = 0; i < this.eqZeroConstraints.Count; i++)
                {
                    var derivative = this.eqZeroConstraints[i].Derivative(variable);
                    total.Terms.Add(new Term<TVar>(derivative, this.nuVariables[i]));
                }

                this.solver.AddEqZeroConstraint(total);
            }
        }
    }
}
