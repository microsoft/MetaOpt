// <copyright file="ISolver.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ZenLib;

    /// <summary>
    /// An interface for an optimization solver.
    /// </summary>
    public class SolverZen : ISolver<Zen<Real>, ZenSolution>
    {
        /// <summary>
        /// The solver constraints.
        /// </summary>
        public IList<Zen<bool>> ConstraintExprs = new List<Zen<bool>>();

        /// <summary>
        /// The solver variables.
        /// </summary>
        public ISet<Zen<Real>> Variables = new HashSet<Zen<Real>>();

        /// <summary>
        /// Static constructor.
        /// </summary>
        static SolverZen()
        {
            ZenLib.Settings.UseLargeStack = true;
        }

        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The solver variable.</returns>
        public Zen<Real> CreateVariable(string name)
        {
            var variable = Zen.Symbolic<Real>(name);
            this.Variables.Add(variable);
            return variable;
        }

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(ZenSolution solution, Zen<Real> variable)
        {
            var value = solution.Get(variable).ToString();
            var result = value.Split('/');

            if (result.Length == 1)
            {
                return double.Parse(result[0]);
            }
            else
            {
                return double.Parse(result[0]) / double.Parse(result[1]);
            }
        }

        /// <summary>
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(Polynomial<Zen<Real>> polynomial)
        {
            this.ConstraintExprs.Add(polynomial.AsZen() <= (Real)0);
        }

        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(Polynomial<Zen<Real>> polynomial)
        {
            this.ConstraintExprs.Add(polynomial.AsZen() == (Real)0);
        }

        /// <summary>
        /// Add or equals zero.
        /// </summary>
        /// <param name="polynomial1">The first polynomial.</param>
        /// <param name="polynomial2">The second polynomial.</param>
        public void AddOrEqZeroConstraint(Polynomial<Zen<Real>> polynomial1, Polynomial<Zen<Real>> polynomial2)
        {
            var p1 = polynomial1.AsZen();
            var p2 = polynomial2.AsZen();
            this.ConstraintExprs.Add(Zen.Or(p1 == (Real)0, p2 == (Real)0));
        }

        /// <summary>
        /// Combine the constraints and variables of another solver into this one.
        /// </summary>
        /// <param name="otherSolver">The other solver.</param>
        public void CombineWith(ISolver<Zen<Real>, ZenSolution> otherSolver)
        {
            if (otherSolver is SolverZen s)
            {
                foreach (var variable in s.Variables)
                {
                    this.Variables.Add(variable);
                }

                foreach (var constraint in s.ConstraintExprs)
                {
                    this.ConstraintExprs.Add(constraint);
                }
            }
            else
            {
                throw new System.Exception("Can not mix solvers");
            }
        }

        /// <summary>
        /// Maximize the objective.
        /// </summary>
        /// <param name="objectiveVariable">The objective variable.</param>
        /// <returns>A solution.</returns>
        public ZenSolution Maximize(Zen<Real> objectiveVariable)
        {
            return Zen.Maximize(objectiveVariable, subjectTo: Zen.And(this.ConstraintExprs.ToArray()));
        }
    }
}
