// <copyright file="ISolver.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    /// <summary>
    /// An interface for an optimization solver.
    /// </summary>
    public interface ISolver<TVar, TSolution>
    {
        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The solver variable.</returns>
        public TVar CreateVariable(string name);

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(TSolution solution, TVar variable);

        /// <summary>
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        /// <returns>name of the constraint.</returns>
        public string AddLeqZeroConstraint(Polynomial<TVar> polynomial);

        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        /// <returns>name of the constraint.</returns>
        public string AddEqZeroConstraint(Polynomial<TVar> polynomial);

        /// <summary>
        /// Add or equals zero.
        /// </summary>
        /// <param name="polynomial1">The first polynomial.</param>
        /// <param name="polynomial2">The second polynomial.</param>
        public void AddOrEqZeroConstraint(Polynomial<TVar> polynomial1, Polynomial<TVar> polynomial2);

        /// <summary>
        /// Remove a constraint.
        /// </summary>
        /// <param name="constraintName">name of the constraint in the string format.</param>
        public void RemoveConstraint(string constraintName);

        /// <summary>
        /// Change constraint's RHS.
        /// </summary>
        /// <param name="constraintName">name of the constraint in the string format.</param>
        /// <param name="newRHS">new RHS of the constraint.</param>
        public void ChangeConstraintRHS(string constraintName, double newRHS);

        /// <summary>
        /// Combine the constraints and variables of another solver into this one.
        /// </summary>
        /// <param name="otherSolver">The other solver.</param>
        public void CombineWith(ISolver<TVar, TSolution> otherSolver);

        /// <summary>
        /// Maximize the objective.
        /// </summary>
        /// <param name="objectiveVariable">The objective variable.</param>
        /// <returns>A solution.</returns>
        public TSolution Maximize(TVar objectiveVariable);

        // <summary>
        // Check feasibility.
        // </summary>
        /// <returns>A solution.</returns>
        public TSolution CheckFeasibility();
    }
}
