// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// An optimization encoder that automatically derives the KKT conditions.
    /// </summary>
    public class KktOptimizationGenerator
    {
        /// <summary>
        /// The constraints for polynomial less than or equal to zero.
        /// </summary>
        private IList<Polynomial> leqZeroConstraints;

        /// <summary>
        /// The constraints for polynomial equals zero.
        /// </summary>
        private IList<Polynomial> eqZeroConstraints;

        /// <summary>
        /// The constructed lambda variables for the KKT conditions.
        /// </summary>
        private IList<Zen<Real>> lambdaVariables;

        /// <summary>
        /// The constructed nu variables for the KKT conditions.
        /// </summary>
        private IList<Zen<Real>> nuVariables;

        /// <summary>
        /// The variables in the encoding.
        /// </summary>
        public ISet<Zen<Real>> Variables;

        /// <summary>
        /// Creates a new instance of the <see cref="KktOptimizationGenerator"/> class.
        /// </summary>
        /// <param name="variables">The encoding variables.</param>
        public KktOptimizationGenerator(ISet<Zen<Real>> variables)
        {
            this.Variables = variables;
            this.leqZeroConstraints = new List<Polynomial>();
            this.eqZeroConstraints = new List<Polynomial>();
            this.lambdaVariables = new List<Zen<Real>>();
            this.nuVariables = new List<Zen<Real>>();
        }

        /// <summary>
        /// Add a constraint that a polynomial is less than or equal to zero.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(Polynomial polynomial)
        {
            this.leqZeroConstraints.Add(polynomial);
        }

        /// <summary>
        /// Add a constraint that a polynomial is equal to zero.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(Polynomial polynomial)
        {
            this.eqZeroConstraints.Add(polynomial);
        }

        /// <summary>
        /// Get the constraints for the encoding.
        /// </summary>
        /// <returns>The result as a Zen boolean expression.</returns>
        public Zen<bool> Constraints()
        {
            var leq = this.leqZeroConstraints.Select(c => c.AsZen(this.Variables) <= (Real)0).ToArray();
            var eq = this.eqZeroConstraints.Select(c => c.AsZen(this.Variables) == (Real)0).ToArray();
            return Zen.And(Zen.And(leq), Zen.And(eq));
        }

        /// <summary>
        /// Get the KKT constraints for maximal solution.
        /// </summary>
        /// <param name="objective">The objective.</param>
        /// <returns>The result as a Zen boolean expression.</returns>
        public Zen<bool> MinimizationConstraints(Polynomial objective)
        {
            var feasibilityConstraints = this.Constraints();

            var constraints = new List<Zen<bool>>();

            foreach (var leqConstraint in this.leqZeroConstraints)
            {
                var lambda = Zen.Symbolic<Real>();
                this.lambdaVariables.Add(lambda);

                constraints.Add(lambda >= (Real)0);
                constraints.Add(Zen.Or(lambda == (Real)0, leqConstraint.AsZen(this.Variables) == (Real)0));
            }

            foreach (var _ in this.eqZeroConstraints)
            {
                this.nuVariables.Add(Zen.Symbolic<Real>());
            }

            foreach (var variable in this.Variables)
            {
                Zen<Real> total = objective.Derivative(variable);

                for (int i = 0; i < this.leqZeroConstraints.Count; i++)
                {
                    var derivative = this.leqZeroConstraints[i].Derivative(variable);
                    total = total + derivative * this.lambdaVariables[i];
                }

                for (int i = 0; i < this.eqZeroConstraints.Count; i++)
                {
                    var derivative = this.eqZeroConstraints[i].Derivative(variable);
                    total = total + derivative * this.nuVariables[i];
                }

                constraints.Add(total == (Real)0);
            }

            return Zen.And(feasibilityConstraints, Zen.And(constraints.ToArray()));
        }
    }
}
