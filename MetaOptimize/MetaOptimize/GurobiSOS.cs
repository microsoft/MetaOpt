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
        private double _bigM = Math.Pow(10, 8);
        private IList<GRBVar> auxQVarList = new List<GRBVar>();
        /// <summary>
        /// Gurobi Vars.
        /// </summary>
        protected Dictionary<string, GRBVar> _variables = new Dictionary<string, GRBVar>();
        /// <summary>
        /// ineq constraints.
        /// </summary>
        protected int _constraintIneqCount = 0;
        /// <summary>
        /// eq constraints.
        /// </summary>
        protected int _constraintEqCount = 0;
        /// <summary>
        /// timeout.
        /// </summary>
        protected double _timeout = 0;
        /// <summary>
        /// verbose.
        /// </summary>
        protected int _verbose = 0;
        /// <summary>
        /// number of threads for gurobi.
        /// </summary>
        protected int _numThreads = 0;

        /// <summary>
        /// Gurobi Aux vars.
        /// </summary>
        protected Dictionary<string, GRBVar> _auxiliaryVars = new Dictionary<string, GRBVar>();

        /// <summary>
        /// Gurobi Model.
        /// </summary>
        protected GRBModel _model = null;

        /// <summary>
        /// This is the objective function.
        /// </summary>
        protected GRBLinExpr _objective = 0;

        /// <summary>
        /// this shows how many seconds should wait before terminating
        /// if best objective does not improve
        /// only applies to MIP.
        /// </summary>
        protected double _timeToTerminateIfNoImprovement = -1;

        /// <summary>
        /// this show whether we want to store the progress in some
        /// file or not.
        /// </summary>
        protected bool _storeProgress = false;

        /// <summary>
        /// file to store the progress (path to the directory).
        /// will consider this only if _storeProgress = true.
        /// </summary>
        protected string _logFileDirname = null;
        /// <summary>
        /// file to store the progress (file name)
        /// will consider this only if _storeProgress = true.
        /// </summary>
        protected string _logFileFilename = null;

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

        private GurobiCallback guorbiCallback;
        private GurobiTerminationCallback gurobiTerminationCallback;
        private GurobiStoreProgressCallback gurobiStoreProgressCallback;
        private void SetCallbacks() {
            var fileExtension = Path.GetExtension(this._logFileFilename);
            var filename = Path.GetFileNameWithoutExtension(this._logFileFilename);
            if (this._timeToTerminateIfNoImprovement > 0 & this._storeProgress) {
                this.guorbiCallback = new GurobiCallback(this._model, this._logFileDirname,
                        filename + "_" + Utils.GetFID() + fileExtension, this._timeToTerminateIfNoImprovement * 1000);
                this._model.SetCallback(this.guorbiCallback);
            } else if (this._timeToTerminateIfNoImprovement > 0) {
                this.gurobiTerminationCallback = new GurobiTerminationCallback(this._model, this._timeToTerminateIfNoImprovement * 1000);
                this._model.SetCallback(this.gurobiTerminationCallback);
            } else if (this._storeProgress) {
                this.gurobiStoreProgressCallback = new GurobiStoreProgressCallback(this._model, this._logFileDirname, filename + "_" + Utils.GetFID() + fileExtension);
                this._model.SetCallback(this.gurobiStoreProgressCallback);
            }
        }

        /// <summary>
        /// to reset the timer for termination.
        /// </summary>
        protected void ResetCallbackTimer() {
            if (this._timeToTerminateIfNoImprovement > 0 & this._storeProgress) {
                this.guorbiCallback.ResetTermination();
                this._model.SetCallback(this.guorbiCallback);
            } else if (this._timeToTerminateIfNoImprovement > 0) {
                this.gurobiTerminationCallback.ResetTermination();
            } else if (this._storeProgress) {
            }
        }
        /// <summary>
        /// constructor.
        /// </summary>
        public GurobiSOS(double timeout = double.PositiveInfinity, int verbose = 0, int numThreads = 0, double timeToTerminateNoImprovement = -1,
                bool recordProgress = false, string logPath = null)
        {
            this._env = SetupGurobi();
            this._model = new GRBModel(this._env);
            this._timeout = timeout;
            this._verbose = verbose;
            this._numThreads = numThreads;
            this._model.Parameters.TimeLimit = timeout;
            this._model.Parameters.Presolve = 2;
            if (numThreads < 0) {
                throw new Exception("num threads should be either 0 (automatic) or positive but got " + numThreads);
            }
            this._model.Parameters.Threads = numThreads;
            this._model.Parameters.OutputFlag = verbose;
            this._timeToTerminateIfNoImprovement = timeToTerminateNoImprovement;
            this._storeProgress = recordProgress;
            if (recordProgress) {
                if (logPath != null) {
                    this._logFileDirname = Path.GetDirectoryName(logPath);
                    this._logFileFilename = Path.GetFileName(logPath);
                } else {
                    throw new Exception("logDirname and logFilename should be specified when recordProgress is true");
                }
            }
            SetCallbacks();
        }

        /// <summary>
        /// Reset the solver by removing all the variables and constraints.
        /// </summary>
        public void CleanAll() {
            this._model.Dispose();
            this._model = new GRBModel(this._env);
            this._model.Parameters.TimeLimit = this._timeout;
            this._model.Parameters.Presolve = 2;
            this._constraintIneqCount = 0;
            this._constraintEqCount = 0;
            this._variables = new Dictionary<string, GRBVar>();
            this._auxiliaryVars = new Dictionary<string, GRBVar>();
            this._objective = 0;
            this._model.Parameters.Threads = this._numThreads;
            this._model.Parameters.OutputFlag = this._verbose;
            this.auxQVarList = new List<GRBVar>();
            SetCallbacks();
        }

        /// <summary>
        /// set the timeout.
        /// </summary>
        /// <param name="timeout">value for timeout.</param>
        public void SetTimeout(double timeout) {
            this._timeout = timeout;
            this._model.Parameters.TimeLimit = timeout;
        }

        /// <summary>
        /// get model.
        /// </summary>
        public GRBModel GetModel() {
            return this._model;
        }

        /// <summary>
        /// Create a new variable with a given name.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="type">the type of variable.</param>
        /// <param name="lb">The lb on the variable.</param>
        /// <param name="ub">The ub on the variable.</param>
        /// <returns>The solver variable.</returns>
        public GRBVar CreateVariable(string name, char type = GRB.CONTINUOUS,
            double lb = double.NegativeInfinity, double ub = double.PositiveInfinity)
        {
            if (name == null || name.Length == 0)
            {
                throw new Exception("no name for variable");
            }

            try
            {
                var new_name = $"{name}_{this._variables.Count}";
                var variable = _model.AddVar(lb, ub, 0, type, new_name);
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
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="objective">The solver solution.</param>
        public void SetObjective(Polynomial<GRBVar> objective) {
            this._objective = Convert(objective);
        }

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        /// <param name="objective">The solver solution.</param>
        public void SetObjective(GRBVar objective) {
            this._objective = objective;
        }

        /// <summary>
        /// Converts polynomials to linear expressions.
        /// </summary>
        /// <param name="poly"></param>
        /// <returns>Linear expression.</returns>
        protected internal GRBLinExpr Convert(Polynomial<GRBVar> poly)
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
        public string AddLeqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            string name = "ineq_index_" + this._constraintIneqCount++;
            this._model.AddConstr(this.Convert(polynomial), GRB.LESS_EQUAL, 0.0, name);
            return name;
        }

        /// <summary>
        /// Converts to quadratic form for Gurobi.
        /// </summary>
        /// <returns>Linear expression.</returns>
        private GRBLinExpr ConvertQE(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            GRBLinExpr obj = 0;
            foreach (var term in linearPoly.Terms) {
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

            for (int i = 0; i < coeffPolyList.Count; i++) {
                Polynomial<GRBVar> coeffPoly = coeffPolyList[i];
                GRBVar variable = variableList[i];
                foreach (var term in coeffPoly.Terms) {
                    switch (term.Exponent) {
                        case 1:
                            GRBVar binary_variable = term.Variable.Value;
                            var auxVar = this.CreateVariable("aux_qe");
                            // if (binary_variable.VType == GRB.BINARY) {
                            obj.AddTerm(term.Coefficient, auxVar);
                            var auxConst = new Polynomial<GRBVar>(new Term<GRBVar>(1, variable));
                            auxConst.Add(new Term<GRBVar>(1 * this._bigM));
                            auxConst.Add(new Term<GRBVar>(-1 * this._bigM, binary_variable));
                            auxConst.Add(new Term<GRBVar>(-1, auxVar));
                            this.AddLeqZeroConstraint(auxConst.Negate());
                            this.AddLeqZeroConstraint(new Polynomial<GRBVar>(new Term<GRBVar>(1, auxVar)));
                            auxQVarList.Add(auxVar);
                            // } else {
                            //     throw new Exception("coefficient should be binary but it is not");
                            // }
                            break;
                        case 0:
                            obj.AddTerm(term.Coefficient, variable);
                            break;
                        default:
                            throw new Exception("non 0|1 exponent is not modeled!!");
                    }
                }
            }
            return obj;
        }

        /// <summary>
        /// Add a less than or equal to zero constraint (Quadratic).
        /// Following constraints; A * B + C \leq 0.
        /// </summary>
        public string AddLeqZeroConstraint(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            string name = "ineq_index_" + this._constraintIneqCount++;
            GRBLinExpr quadConstraint = this.ConvertQE(coeffPolyList, variableList, linearPoly);
            this._model.AddConstr(quadConstraint, GRB.LESS_EQUAL, 0.0, name);
            return name;
        }

        /// <summary>
        /// Wrapper for AddEqZeroConstraint that converts types.
        /// </summary>
        /// <param name="polynomial"></param>
        public string AddEqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            string name = "eq_index_" + this._constraintEqCount++;
            this._model.AddConstr(this.Convert(polynomial), GRB.EQUAL, 0.0, name);
            return name;
        }

        /// <summary>
        /// Add a equal to zero constraint (Quadratic).
        /// Following constraints; A * B + C \leq 0.
        /// </summary>
        public string AddEqZeroConstraint(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            string name = "ineq_index_" + this._constraintIneqCount++;
            GRBLinExpr quadConstraint = this.ConvertQE(coeffPolyList, variableList, linearPoly);
            this._model.AddConstr(quadConstraint, GRB.EQUAL, 0.0, name);
            return name;
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
        public virtual void AddOrEqZeroConstraint(Polynomial<GRBVar> polynomial1, Polynomial<GRBVar> polynomial2)
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
        /// Remove a constraint.
        /// </summary>
        /// <param name="constraintName">name of the constraint in the string format.</param>
        public void RemoveConstraint(string constraintName)
        {
            this._model.Remove(this._model.GetConstrByName(constraintName));
        }

        /// <summary>
        /// Change constraint's RHS.
        /// </summary>
        /// <param name="constraintName">name of the constraint in the string format.</param>
        /// <param name="newRHS">new RHS of the constraint.</param>
        public void ChangeConstraintRHS(string constraintName, double newRHS)
        {
            this._model.GetConstrByName(constraintName).Set(GRB.DoubleAttr.RHS, newRHS);
        }

        /// <summary>
        /// check feasibility of optimization.
        /// </summary>
        public virtual GRBModel CheckFeasibility(double objectiveValue)
        {
            Console.WriteLine("in feasibility call");
            string exhaust_dir_name = @"c:\tmp\grbsos_exhaust\rand_" + (new Random()).Next(1000) + @"\";
            this._model.Parameters.BestObjStop = objectiveValue;
            this._model.Parameters.BestBdStop = objectiveValue - 0.001;
            // this._model.Parameters.MIPFocus = 2;
            this._model.SetObjective(this._objective, GRB.MAXIMIZE);
            Directory.CreateDirectory(exhaust_dir_name);
            this._model.Write($"{exhaust_dir_name}\\model_" + DateTime.Now.Millisecond + ".lp");
            this._model.Update();
            this._model.Optimize();
            if (this._model.Status != GRB.Status.USER_OBJ_LIMIT & this._model.Status != GRB.Status.OPTIMAL)
            {
                // throw new Exception($"model not optimal {ModelStatusToString(this._model.Status)}");
                throw new InfeasibleOrUnboundSolution();
            }
            if (this._objective.Value < objectiveValue) {
                throw new InfeasibleOrUnboundSolution();
            }
            return this._model;
        }

        /// <summary>
        /// Maximize the objective.
        /// </summary>
        /// <returns>A solution.</returns>
        public virtual GRBModel Maximize()
        {
            Console.WriteLine("in maximize call");
            GRBLinExpr objective = 0;
            foreach (var auxVar in auxQVarList) {
                objective += auxVar / this._bigM;
            }
            this._model.SetObjective(objective + this._objective, GRB.MAXIMIZE);
            // this._model.Parameters.MIPFocus = 3;
            // this._model.Parameters.Cuts = 3;
            // this._model.Parameters.Heuristics = 0.5;

            string exhaust_dir_name = @"c:\tmp\grbsos_exhaust\rand_" + (new Random()).Next(1000) + @"\";
            Directory.CreateDirectory(exhaust_dir_name);
            this._model.Write($"{exhaust_dir_name}\\model_" + DateTime.Now.Millisecond + ".lp");

            this._model.Optimize();
            if (this._model.Status != GRB.Status.TIME_LIMIT & this._model.Status != GRB.Status.OPTIMAL & this._model.Status != GRB.Status.INTERRUPTED)
            {
                throw new Exception($"model not optimal {ModelStatusToString(this._model.Status)}");
                // throw new InfeasibleOrUnboundSolution();
            }

            return this._model;
        }

        /// <summary>
        /// Reset the timer and then maximize.
        /// </summary>
        public virtual GRBModel Maximize(Polynomial<GRBVar> objective, bool reset)
        {
            if (reset) {
                this.ResetCallbackTimer();
            }
            return Maximize(objective);
        }

        /// <summary>
        /// Maximize the objective with objective as input.
        /// </summary>
        /// <returns>A solution.</returns>
        public virtual GRBModel Maximize(Polynomial<GRBVar> objective)
        {
            SetObjective(objective);
            return Maximize();
        }

        /// <summary>
        /// Maximize the objective with objective as input.
        /// </summary>
        /// <returns>A solution.</returns>
        public virtual GRBModel Maximize(GRBVar objective)
        {
            SetObjective(objective);
            return Maximize();
        }

        /// <summary>
        /// Returns current status of GRB model.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        protected static string ModelStatusToString(int x)
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
            if (solution.Status != GRB.Status.USER_OBJ_LIMIT & solution.Status != GRB.Status.TIME_LIMIT
                & solution.Status != GRB.Status.OPTIMAL & solution.Status != GRB.Status.INTERRUPTED)
            {
                throw new Exception("can't read status since model is not optimal");
            }

            if (solution.Status != GRB.Status.OPTIMAL) {
                return variable.Xn;
            }
            return variable.X;
        }
    }
}
