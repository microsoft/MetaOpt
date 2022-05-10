using System;
using System.Collections.Generic;
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
    public class SolverGuroubi : ISolver<GRBVar, string>
    {
        /// <summary>
        /// stashes guroubi environment so it can be reused.
        /// </summary>
        public GRBEnv _env { get; private set; }
        /// <summary>
        /// The solver constraints.
        /// </summary>
        public IList<GRBConstr> _constraintExprs = new List<GRBConstr>();
        /// <summary>
        /// The solver variables.
        /// </summary>
        public ISet<GRBVar> _variables = new HashSet<GRBVar>();

        /// <summary>
        /// auxilary variables we are using
        /// to encode SOS constraints 
        /// </summary>
        private List<GRBVar> _auxilaryVars = new List<GRBVar>();

        /// <summary>
        /// Used to encode disjunctions.
        /// </summary>
        private List<GRBSOS> _SOSConstraints = new List<GRBSOS>();

        /// <summary>
        /// The gurobi model.
        /// </summary>
        public GRBModel _model;

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
            this._env = SetupGurobi();
            this._model = new GRBModel(this._env);
        }
        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The solver variable.</returns>
        public GRBVar CreateVariable(string name)
        {
            var variable = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, name);
            this._variables.Add(variable);
            return variable;
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
        /// Add a less than or equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddLeqZeroConstraint(GRBLinExpr polynomial)
        {
            this._constraintExprs.Add(this._model.AddConstr(polynomial, GRB.LESS_EQUAL,
                (Double)0, "index:" + this._constraintExprs.Count));
        }
        /// <summary>
        /// Add a equal to zero constraint.
        /// </summary>
        /// <param name="polynomial">The polynomial.</param>
        public void AddEqZeroConstraint(GRBLinExpr polynomial)
        {
            this._constraintExprs.Add(this._model.AddConstr(polynomial, GRB.EQUAL,
                (Double)0, "index:" + this._constraintExprs.Count));
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
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "index:" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_1);
            var var_2 = this._model.AddVar(
                    Double.MinValue, Double.MaxValue, 0, GRB.CONTINUOUS, "index:" + this._auxilaryVars.Count);
            this._auxilaryVars.Add(var_2);
            GRBVar[] auxilaries = new GRBVar[] { var_1, var_2 };
            double[] weights = new double[] { 0, 1 };

            // Create constraints that ensure the auxilary variables are equal
            // to the value of the polynomials.
            this._constraintExprs.Add(this._model.AddConstr(polynomial1, GRB.EQUAL,
                var_1, "index:" + this._constraintExprs.Count));
            this._constraintExprs.Add(this._model.AddConstr(polynomial2, GRB.EQUAL,
                var_2, "index:" + this._constraintExprs.Count));

            // Add SOS constraint.
            this._SOSConstraints.Add(this._model.AddSOS(auxilaries,
                weights, GRB.SOS_TYPE1));
        }
    }
}
