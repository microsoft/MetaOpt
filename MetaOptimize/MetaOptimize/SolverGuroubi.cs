using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Gurobi;
using Microsoft.Z3;
using ZenLib;

namespace MetaOptimize
{
    /// <summary>
    /// Gurobi solver.
    /// Todo: may need to rethink the second argument to the class.
    /// Right now assuming we have a dictionary that maps var names to
    /// guroubi variables.
    /// </summary>
    public class SolverGuroubi : ISolver<GRBVar, GRBModel>
    {
        /// <summary>
        /// stashes guroubi environment so it can be reused.
        /// </summary>
        public GRBEnv _env = null;
        /// <summary>
        /// The solver variables.
        /// </summary>
        public ISet<GRBVar> _variables = new HashSet<GRBVar>();
        /// <summary>
        /// Stores variable names.
        /// </summary>
        public ISet<string> _varNames = new HashSet<string>();

        /// <summary>
        /// auxilary variables we are using
        /// to encode SOS constraints.
        /// </summary>
        private List<GRBVar> _auxilaryVars = new List<GRBVar>();

        private List<string> _auxiliaryVarNames = new List<string>();
        /// <summary>
        /// inequality constraints.
        /// </summary>
        public IList<GRBLinExpr> _constraintIneq = new List<GRBLinExpr>();
        /// <summary>
        /// equality constraints.
        /// </summary>
        public IList<GRBLinExpr> _constraintEq = new List<GRBLinExpr>();
        /// <summary>
        /// Keeps track of sos aux.
        /// </summary>
        private List<GRBVar[]> _SOSauxilaries = new List<GRBVar[]>();
        /// <summary>
        /// The gurobi model.
        /// </summary>
        public GRBModel _model = null;

        private bool _modelRun;

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
            GRBVar variable;
            if (name == "")
            {
                throw new Exception("bug");
            }
            try
            {
                string new_name = name + "_" + this._variables.Count;
                variable = _model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS,
                    new_name);
                this._variables.Add(variable);
                this._varNames.Add(new_name);
                return variable;
            }
            catch (GRBException ex)
            {
                System.Console.WriteLine(ex.Message);
            }
            return null;
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
                if (term.Exponent == 1)
                {
                    obj.AddTerm(term.Coefficient, (dynamic)term.Variable.Value);
                }
                else
                {
                    obj += term.Coefficient;
                }
            }
            return obj;
        }
        /// <summary>
        /// wrapper that does type conversions then calls the original function.
        /// </summary>
        /// <param name="polynomial"></param>
        public void AddLeqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            GRBLinExpr poly = this.convertPolynomialToLinExpr(polynomial);
            this.AddLeqZeroConstraint(poly);
        }
        /// <summary>
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(GRBLinExpr polynomial)
        {
            this._model.AddConstr(polynomial, GRB.LESS_EQUAL,
                (Double)0, "ineq_index_" + this._constraintIneq.Count);
            this._constraintIneq.Add(polynomial);
        }
        /// <summary>
        /// Wrapper for AddEqZeroConstraint that converts types.
        /// </summary>
        /// <param name="polynomial"></param>
        public void AddEqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            GRBLinExpr poly = this.convertPolynomialToLinExpr(polynomial);
            this.AddEqZeroConstraint(poly);
        }
        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(GRBLinExpr polynomial)
        {
            this._model.AddConstr(polynomial, GRB.EQUAL,
                (Double)0, "eq_index_" + this._constraintEq.Count);
            this._constraintEq.Add(polynomial);
        }
        /// <summary>
        /// Wrapper that convers the new types to guroubi types and then
        /// calls the proper function.
        /// </summary>
        /// <param name="polynomial1"></param>
        /// <param name="polynomial2"></param>
        public void AddOrEqZeroConstraint(Polynomial<GRBVar> polynomial1, Polynomial<GRBVar> polynomial2)
        {
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
            // Create an auxilary variable for each polynomial
            // Add it to the list of auxilary variables.
            var var_1 = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "aux_" + this._auxilaryVars.Count);
            this._auxiliaryVarNames.Add("aux_" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_1);
            var var_2 = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "aux_" + this._auxilaryVars.Count);
            this._auxiliaryVarNames.Add("aux_" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_2);
            GRBVar[] auxilaries = new GRBVar[] { var_1, var_2 };

            // Create constraints that ensure the auxilary variables are equal
            // to the value of the polynomials.
            polynomial1 = new GRBLinExpr(polynomial1);
            polynomial1.AddTerm(-1, var_1);
            polynomial2 = new GRBLinExpr(polynomial2);
            polynomial2.AddTerm(-1, var_2);
            this.AddEqZeroConstraint(polynomial1);
            this.AddEqZeroConstraint(polynomial2);
            // Add SOS constraint.
            this._model.AddSOS(auxilaries, auxilaries.Select((x, i) => (Double)i).ToArray(),
                        GRB.SOS_TYPE1);
            this._SOSauxilaries.Add(auxilaries);
        }
        /// <summary>
        /// Combine the constraints and variables of another solver into this one.
        /// </summary>
        /// <param name="otherSolver">The other solver.</param>
        public void CombineWith(ISolver<GRBVar, GRBModel> otherSolver)
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
                foreach (var name in s._varNames)
                {
                    this.CreateVariable(name);
                }
                foreach (var name in s._auxiliaryVarNames)
                {
                    this._auxilaryVars.Add(this._model.AddVar(-1 * Math.Pow(10, 10), Math.Pow(10, 10), 0, GRB.CONTINUOUS,
                        name));
                }
                foreach (var constraint in s._constraintIneq)
                {
                    this.AddLeqZeroConstraint(constraint);
                }
                foreach (var constraint in s._constraintEq)
                {
                    this.AddEqZeroConstraint(constraint);
                }
                foreach (var aux in s._SOSauxilaries)
                {
                    this._model.AddSOS(aux, aux.Select((x, i) => (Double)i).ToArray(),
                        GRB.SOS_TYPE1);
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
        public GRBModel Maximize(GRBVar objectiveVariable)
        {
            GRBLinExpr obj = 0;
            obj.AddTerm(1.0, objectiveVariable);
            this._model.SetObjective(obj, GRB.MAXIMIZE);
            this._model.Optimize();
            this._modelRun = true;
            this._model.Write("model.lp");
            return this._model;
        }
        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(GRBModel solution, GRBVar variable)
        {
            if (!_modelRun)
            {
                GRBLinExpr obj = 0;
                this._model.SetObjective(obj, GRB.MAXIMIZE);
                this._model.Optimize();
                this._modelRun = true;
                this._model.Write("model.lp");
            }
            int status = _model.Status;
            if (status == GRB.Status.INFEASIBLE || status == GRB.Status.INF_OR_UNBD)
            {
                Console.WriteLine("The model cannot be solved because it is "
                    + "infeasible");
                Environment.Exit(1);
            }
            if (status == GRB.Status.UNBOUNDED)
            {
                Console.WriteLine("The model cannot be solved because it is "
                    + "unbounded");
                Environment.Exit(1);
            }
            if (status != GRB.Status.OPTIMAL)
            {
                Console.WriteLine("Optimization was stopped with status " + status);
                Environment.Exit(1);
            }
            return variable.X;
        }
    }
}
