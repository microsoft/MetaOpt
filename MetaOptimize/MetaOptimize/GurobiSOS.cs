using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Gurobi;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Z3;
using ZenLib;

namespace MetaOptimize
{
    /// <summary>
    /// Gurobi-based solver which specifies ORs as SOS1 constraints.
    /// </summary>
    public class GurobiSOS : ISolver<GRBVar, GRBModel>
    {
        private GRBEnv _env = null;

        /// <summary>
        /// Gurobi Vars.
        /// </summary>
        protected Dictionary<string, GRBVar> _variables = new Dictionary<string, GRBVar>();
        /// <summary>
        /// ineq constraints.
        /// </summary>
        private int _constraintIneqCount = 0;
        /// <summary>
        /// eq constraints.
        /// </summary>
        protected int _constraintEqCount = 0;
        /// <summary>
        /// Gurobi Aux vars.
        /// </summary>
        protected Dictionary<string, GRBVar> _auxiliaryVars = new Dictionary<string, GRBVar>();

        /// <summary>
        /// Gurobi Model.
        /// </summary>
        protected GRBModel _model = null;

        // SK: TODO: Shouldn't this be protected as well?
        private GRBLinExpr _objective = 0;

        /// <summary>
        /// releases gurobi environment. // sk: not sure about this.
        /// </summary>
        public void Delete()
        {
            this._model.Dispose();
            this._env.Dispose();
            this._env = null;
        }

        /// <summary>
        /// Connects to Gurobi.
        /// </summary>
        /// <returns>an env.</returns>
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
        public GurobiSOS()
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
            if (name == null || name.Length == 0)
            {
                throw new Exception("no name for variable");
            }

            try
            {
                var new_name = $"{name}_{this._variables.Count}";
                var variable = _model.AddVar(Double.NegativeInfinity, Double.PositiveInfinity, 0, GRB.CONTINUOUS, new_name);
                this._variables.Add(new_name, variable);
                return variable;
            }
            catch (GRBException ex)
            {
                Console.WriteLine(ex.ToString());
                throw (ex);
            }
        }

        /// <summary>
        /// Converts polynomials to linear expressions.
        /// </summary>
        /// <param name="poly"></param>
        /// <returns>Linear expression.</returns>
        public GRBLinExpr Convert(Polynomial<GRBVar> poly)
        {
            GRBLinExpr obj = 0;
            foreach (var term in poly.Terms)
            {
                switch (term.Exponent)
                {
                    case 1:
                        obj.AddTerm(term.Coefficient, term.Variable.Value);
                        break;
                    case 0:
                        obj += (term.Coefficient);
                        break;
                    default:
                        throw new Exception("non 0|1 exponent is not modeled");
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
            this._model.AddConstr(this.Convert(polynomial), GRB.LESS_EQUAL, 0.0, "ineq_index_" + this._constraintIneqCount++);
        }

        /// <summary>
        /// Wrapper for AddEqZeroConstraint that converts types.
        /// </summary>
        /// <param name="polynomial"></param>
        public void AddEqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            this._model.AddConstr(this.Convert(polynomial), GRB.EQUAL, 0.0, "eq_index_" + this._constraintEqCount);
        }
        /// <summary>
        /// Combine the constraints and variables of another solver into this one.
        /// </summary>
        /// <param name="otherSolver">The other solver.</param>
        public void CombineWith(ISolver<GRBVar, GRBModel> otherSolver)
        {
            // removed support for this. Check earlier git commits if you need it.
        }

        /// <summary>
        /// Ensure at least one of these terms is zero.
        /// </summary>
        /// <param name="polynomial1"></param>
        /// <param name="polynomial2"></param>
        public void AddOrEqZeroConstraint(Polynomial<GRBVar> polynomial1, Polynomial<GRBVar> polynomial2)
        {
            this.AddOrEqZeroConstraintV1(this.Convert(polynomial1), this.Convert(polynomial2));
        }

        /// <summary>
        /// Uses SOS constraint to ensure atleast one of the following terms should equal 0.
        /// </summary>
        /// <param name="expr1">The first polynomial.</param>
        /// <param name="expr2">The second polynomial.</param>
        public void AddOrEqZeroConstraintV1(GRBLinExpr expr1, GRBLinExpr expr2)
        {
            // Create auxilary variable for each polynomial
            var var_1 = this._model.AddVar(Double.NegativeInfinity, Double.PositiveInfinity, 0, GRB.CONTINUOUS, "aux_" + this._auxiliaryVars.Count);
            this._auxiliaryVars.Add($"aux_{this._auxiliaryVars.Count}", var_1);

            var var_2 = this._model.AddVar(Double.NegativeInfinity, Double.PositiveInfinity, 0, GRB.CONTINUOUS, "aux_" + this._auxiliaryVars.Count);
            this._auxiliaryVars.Add($"aux_{this._auxiliaryVars.Count}", var_2);

            this._model.AddConstr(expr1, GRB.EQUAL, var_1, "eq_index_" + this._constraintEqCount++);
            this._model.AddConstr(expr2, GRB.EQUAL, var_2, "eq_index_" + this._constraintEqCount++);

            // Add SOS constraint.
            var auxiliaries = new GRBVar[] { var_1, var_2 };
            this._model.AddSOS(auxiliaries, new Double[] { 1, 2 }, GRB.SOS_TYPE1); // note: weights do not matter
        }

        /// <summary>
        /// Maximize the objective.
        /// </summary>
        /// <param name="objectiveVariable">The objective variable.</param>
        /// <returns>A solution.</returns>
        public GRBModel Maximize(GRBVar objectiveVariable)
        {
            Console.WriteLine("in maximize call");
            this._objective.AddTerm(1.0, objectiveVariable);
            this._model.SetObjective(this._objective, GRB.MAXIMIZE);

            string exhaust_dir_name = @"c:\tmp\grbsos_exhaust\rand_" + (new Random()).Next(1000) + @"\";
            Directory.CreateDirectory(exhaust_dir_name);
            this._model.Write($"{exhaust_dir_name}\\model_" + DateTime.Now.Millisecond + ".lp");

            this._model.Optimize();
            if (this._model.Status != GRB.Status.OPTIMAL)
            {
                throw new Exception($"model not optimal {ModelStatusToString(this._model.Status)}");
            }

            return this._model;
        }

        private static string ModelStatusToString(int x)
        {
            switch (x)
            {
                case GRB.Status.INFEASIBLE: return "infeasible";
                case GRB.Status.INF_OR_UNBD: return "inf_or_unbd";
                case GRB.Status.UNBOUNDED: return "unbd";
                default: return "xxx_did_not_parse_status_code";
            }
        }

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="solution">The solver solution.</param>
        /// <param name="variable">The variable.</param>
        /// <returns>The value as a double.</returns>
        public double GetVariable(GRBModel solution, GRBVar variable)
        {
            // Maximize() above is a synchronous call; not sure if this check is needed
            if (solution.Status != GRB.Status.OPTIMAL)
            {
                throw new Exception("can't read status since model is not optimal");
            }

            return variable.X;
        }
    }
}
