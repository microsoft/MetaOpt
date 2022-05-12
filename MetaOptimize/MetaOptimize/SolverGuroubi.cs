using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Gurobi;
using ZenLib;

namespace MetaOptimize
{
    /// <summary>
    /// Gurobi solver.
    /// Todo: may need to rethink the second argument to the class.
    /// Right now assuming we have a dictionary that maps var names to
    /// guroubi variables.
    /// </summary>
    public class SolverGuroubi : ISolver<GRBVar, SolverGuroubi>
    {
        /// <summary>
        /// stashes guroubi environment so it can be reused.
        /// </summary>
        public GRBEnv _env { get; private set; }
        /// <summary>
        /// The solver variables.
        /// </summary>
        public ISet<GRBVar> _variables = new HashSet<GRBVar>();

        /// <summary>
        /// auxilary variables we are using
        /// to encode SOS constraints.
        /// </summary>
        private List<GRBVar> _auxilaryVars = new List<GRBVar>();
        /// <summary>
        /// inequality constraints.
        /// </summary>
        public IList<GRBConstr> _constraintIneq = new List<GRBConstr>();
        /// <summary>
        /// equality constraints.
        /// </summary>
        public IList<GRBConstr> _constraintEq = new List<GRBConstr>();
        /// <summary>
        /// Used to encode disjunctions.
        /// </summary>
        private List<GRBSOS> _SOSConstraints = new List<GRBSOS>();

        private List<GRBVar[]> _SOSauxilaries = new List<GRBVar[]>();

        /// <summary>
        /// The gurobi model.
        /// </summary>
        public GRBModel _model = null;

        /// <summary>
        /// Connects to Ishai's guroubi license.
        /// </summary>
        /// <returns></returns>
        public static GRBEnv SetupGurobi()
        {
            // for 8.1 and later
            GRBEnv env = new GRBEnv(true);
            env.Set("LogFile", "maxFlowSolver.log");
            env.TokenServer = "10.137.70.76"; // ishai-z420
            env.Start();
            return env;
        }
        /// <summary>
        /// constructor.
        /// </summary>
        public SolverGuroubi()
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
        }
        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The solver variable.</returns>
        public GRBVar CreateVariable(string name)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            GRBVar variable = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS,
                    name + "_index_" + this._variables.Count);
            this._variables.Add(variable);
            return variable;
        }
        /// <summary>
        /// Converts polynomials to linear expressions.
        /// </summary>
        /// <param name="poly"></param>
        /// <returns></returns>
        public GRBLinExpr convertPolynomialToLinExpr(Polynomial<GRBVar> poly)
        {
            GRBLinExpr obj = 0;
            foreach (var term in poly.Terms)
            {
                obj.AddTerm(term.Coefficient, (dynamic)term.Variable.Value);
            }
            return obj;
        }
        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// todo: wasn't sure how to handle the extra input,
        /// so for now set it as a useless string.
        /// </summary>
        /// <param name="varName">the name of the variable.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(string varName, GRBVar variable)
        {
            return variable.X;
        }
        /// <summary>
        /// wrapper that does type conversions then calls the original function.
        /// </summary>
        /// <param name="polynomial"></param>
        public void AddLeqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            GRBLinExpr poly = this.convertPolynomialToLinExpr(polynomial);
            this.AddLeqZeroConstraint(poly);
        }
        /// <summary>
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(GRBLinExpr polynomial)
        {
            this._constraintIneq.Add(this._model.AddConstr(polynomial, GRB.LESS_EQUAL,
                (Double)0, "ineq_index:" + this._constraintIneq.Count));
        }
        /// <summary>
        /// Wrapper for AddEqZeroConstraint that converts types.
        /// </summary>
        /// <param name="polynomial"></param>
        public void AddEqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            GRBLinExpr poly = this.convertPolynomialToLinExpr(polynomial);
            this.AddEqZeroConstraint(poly);
        }
        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(GRBLinExpr polynomial)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            this._constraintEq.Add(this._model.AddConstr(polynomial, GRB.EQUAL,
                (Double)0, "eq_index:" + this._constraintEq.Count));
        }
        /// <summary>
        /// Wrapper that convers the new types to guroubi types and then
        /// calls the proper function.
        /// </summary>
        /// <param name="polynomial1"></param>
        /// <param name="polynomial2"></param>
        public void AddOrEqZeroConstraint(Polynomial<GRBVar> polynomial1, Polynomial<GRBVar> polynomial2)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            GRBLinExpr poly1 = this.convertPolynomialToLinExpr(polynomial1);
            GRBLinExpr poly2 = this.convertPolynomialToLinExpr(polynomial2);
            this.AddOrEqZeroConstraint(poly1, poly2);
        }
        /// <summary>
        /// Add or equals zero.
        /// We currently are using SOS
        /// constraints to encode this.
        /// todo: explore auxilary vars.
        /// </summary>
        /// <param name="polynomial1">The first polynomial.</param>
        /// <param name="polynomial2">The second polynomial.</param>
        public void AddOrEqZeroConstraint(GRBLinExpr polynomial1, GRBLinExpr polynomial2)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            // Create an auxilary variable for each polynomial
            // Add it to the list of auxilary variables.
            var var_1 = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "index:" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_1);
            var var_2 = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "index:" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_2);
            GRBVar[] auxilaries = new GRBVar[] { var_1, var_2 };
            double[] weights = new double[] { 0, 1 };

            // Create constraints that ensure the auxilary variables are equal
            // to the value of the polynomials.
            polynomial1 = new GRBLinExpr(polynomial1);
            polynomial1.AddTerm(-1, var_1);
            polynomial2 = new GRBLinExpr(polynomial2);
            polynomial2.AddTerm(-1, var_2);
            this._constraintEq.Add(this._model.AddConstr(polynomial1, GRB.EQUAL,
                0, "eq_index:" + this._constraintEq.Count));
            this._constraintEq.Add(this._model.AddConstr(polynomial2, GRB.EQUAL,
                0, "eq_index:" + this._constraintEq.Count));

            // Add SOS constraint.
            this._SOSConstraints.Add(this._model.AddSOS(auxilaries,
                weights, GRB.SOS_TYPE1));
            this._SOSauxilaries.Add(auxilaries);
        }
        /// <summary>
        /// Combine the constraints and variables of another solver into this one.
        /// </summary>
        /// <param name="otherSolver">The other solver.</param>
        public void CombineWith(ISolver<GRBVar, SolverGuroubi> otherSolver)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            if (otherSolver is SolverGuroubi s)
            {
                // Warning: assumes all variables are of the same type.
                foreach (var variable in s._variables)
                {
                    this._variables.Add(this._model.AddVar(variable.LB, variable.UB, 0, variable.VType,
                        variable.VarName));
                }
                foreach (var variable in s._auxilaryVars)
                {
                    this._auxilaryVars.Add(this._model.AddVar(variable.LB, variable.UB, 0, variable.VType,
                        variable.VarName));
                }
                foreach (var constraint in s._constraintIneq)
                {
                    this._constraintIneq.Add(this._model.AddConstr(constraint.RHS, GRB.LESS_EQUAL,
                        0, "ineq_index:" + this._constraintIneq.Count));
                }
                foreach (var constraint in s._constraintEq)
                {
                    this._constraintEq.Add(this._model.AddConstr(constraint.RHS, GRB.EQUAL, 0,
                        "eq_index:" + this._constraintEq.Count));
                }
                foreach (var aux in s._SOSauxilaries)
                {
                    this._SOSConstraints.Add(this._model.AddSOS(aux, aux.Select((x, i) => (Double)i).ToArray(),
                        GRB.SOS_TYPE1));
                    this._SOSauxilaries.Add(aux);
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
        public SolverGuroubi Maximize(GRBVar objectiveVariable)
        {
            if (this._env == null)
            {
                this._env = SetupGurobi();
            }
            if (this._model == null)
            {
                this._model = new GRBModel(this._env);
            }
            GRBLinExpr obj = 0;
            obj.AddTerm(1.0, objectiveVariable);
            this._model.SetObjective(obj, GRB.MAXIMIZE);
            this._model.Optimize();
            return this;
        }
        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(SolverGuroubi solution, GRBVar variable)
        {
            return variable.X;
        }
    }
}
