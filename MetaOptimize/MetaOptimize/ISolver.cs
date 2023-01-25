// <copyright file="ISolver.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;
    using Gurobi;
    /// <summary>
    /// An interface for an optimization solver.
    /// </summary>
    public interface ISolver<TVar, TSolution>
    {
        /// <summary>
        /// Reset the solver by removing all the variables and constraints.
        /// </summary>
        public void CleanAll(double timeout = -1, bool disableStoreProgress = false);

        /// <summary>
        /// Reset the solver by removing all the variables and constraints.
        /// </summary>
        public void CleanAll(bool focusBstBd, double timeout = -1);

        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="type">the type of variable.</param>
        /// <param name="lb">The lb on the variable.</param>
        /// <param name="ub">The ub on the variable.</param>
        /// <returns>The solver variable.</returns>
        public TVar CreateVariable(string name, char type = GRB.CONTINUOUS,
                 double lb = double.NegativeInfinity, double ub = double.PositiveInfinity);

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(TSolution solution, TVar variable);

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        public double GetDualVariable(TSolution solution, string constrName);

        /// <summary>
        /// set the objective.
        /// </summary>
        /// <param name="objective">The solver solution.</param>
        public void SetObjective(Polynomial<TVar> objective);

        /// <summary>
        /// set the objective.
        /// </summary>
        /// <param name="objective">The solver solution.</param>
        public void SetObjective(TVar objective);

        /// <summary>
        /// set the objective.
        /// </summary>
        /// <param name="timeout">value for timeout.</param>
        public void SetTimeout(double timeout);

        /// <summary>
        /// set the FocusBstBd.
        /// </summary>
        public void SetFocusBstBd(bool focusBstBd);

        /// <summary>
        /// get model.
        /// </summary>
        public TSolution GetModel();

        /// <summary>
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        /// <returns>name of the constraint.</returns>
        public string AddLeqZeroConstraint(Polynomial<TVar> polynomial);

        /// <summary>
        /// Add a less than or equal to zero constraint (Quadratic).
        /// Following constraints; A * B + C \leq 0.
        /// </summary>
        /// <param name="coeffPolyList">The coefficent polynomial list (A).</param>
        /// <param name="variableList">The variable list (B).</param>
        /// <param name="linearPoly">The linear term (C).</param>
        /// <returns>name of the constraint.</returns>
        public string AddLeqZeroConstraint(IList<Polynomial<TVar>> coeffPolyList, IList<TVar> variableList, Polynomial<TVar> linearPoly);

        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        /// <returns>name of the constraint.</returns>
        public string AddEqZeroConstraint(Polynomial<TVar> polynomial);

        /// <summary>
        /// Add a equal to zero constraint (Quadratic).
        /// Following constraints; A * B + C == 0.
        /// </summary>
        /// <param name="coeffPolyList">The coefficent polynomial list (A).</param>
        /// <param name="variableList">The variable list (B).</param>
        /// <param name="linearPoly">The linear term (C).</param>
        /// <returns>name of the constraint.</returns>
        public string AddEqZeroConstraint(IList<Polynomial<TVar>> coeffPolyList, IList<TVar> variableList, Polynomial<TVar> linearPoly);

        /// <summary>
        /// Add or equals zero.
        /// </summary>
        /// <param name="polynomial1">The first polynomial.</param>
        /// <param name="polynomial2">The second polynomial.</param>
        public void AddOrEqZeroConstraint(Polynomial<TVar> polynomial1, Polynomial<TVar> polynomial2);

        /// <summary>
        /// Add a = max(b, c) constraint.
        /// </summary>
        public void AddMaxConstraint(TVar LHS, Polynomial<TVar> maxItem1, Polynomial<TVar> maxItem2);

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
        /// <returns>A solution.</returns>
        public TSolution Maximize();

        /// <summary>
        /// Maximize the objective with objective as input.
        /// </summary>
        /// <returns>A solution.</returns>
        public TSolution Maximize(Polynomial<TVar> objective);

        /// <summary>
        /// Maximize the objective with objective as input.
        /// reset the callback timer.
        /// </summary>
        /// <returns>A solution.</returns>
        public TSolution Maximize(Polynomial<TVar> objective, bool reset);

        /// <summary>
        /// Maximize the objective with objective as input.
        /// </summary>
        /// <returns>A solution.</returns>
        public TSolution Maximize(TVar objective);

        /// <summary>
        /// Check feasibility.
        /// </summary>
        /// <returns>A solution.</returns>
        public TSolution CheckFeasibility(double objectiveValue);

        /// <summary>
        /// Call the model update to apply new constraints and objectives.
        /// </summary>
        public void ModelUpdate();

        /// <summary>
        /// initialize some of the variables.
        /// </summary>
        public void InitializeVariables(TVar variable, int value);

        /// <summary>
        /// adding some auxiliary term to be added to the global objective when maximized.
        /// </summary>
        public void AddGlobalTerm(Polynomial<TVar> auxObjPoly);

        /// <summary>
        /// append as the next line of the store progress file.
        /// </summary>
        public void AppendToStoreProgressFile(double time_ms, double gap, bool reset = false);
    }
}
