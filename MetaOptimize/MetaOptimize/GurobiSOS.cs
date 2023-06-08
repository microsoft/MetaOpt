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
        private double _bigM = Math.Pow(10, 4);
        private double _tolerance = Math.Pow(10, -8);
        /// <summary>
        /// list of Aux Vars for Q.
        /// </summary>
        protected IList<Polynomial<GRBVar>> auxPolyList = new List<Polynomial<GRBVar>>();
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
        /// if set, will focus on improving the best bound instead of current solution.
        /// </summary>
        protected bool _focusBstBd = false;

        /// <summary>
        /// releases gurobi environment. // sk: not sure about this.
        /// </summary>
        public void Delete()
        {
            this._model.Dispose();
        }

        private GurobiCallback guorbiCallback;
        // private GurobiTerminationCallback gurobiTerminationCallback;
        // private GurobiStoreProgressCallback gurobiStoreProgressCallback;
        // private GurobiTimeoutCallback gurobiTimeoutCallback;
        private void SetCallbacks(bool disableStoreProgress = false) {
            var fileExtension = Path.GetExtension(this._logFileFilename);
            var filename = Path.GetFileNameWithoutExtension(this._logFileFilename);
            var progress = this._storeProgress;
            if (disableStoreProgress) {
                progress = false;
            }
            this.guorbiCallback = new GurobiCallback(this._model, storeProgress: progress,
                dirname: this._logFileDirname, filename: filename + "_" + Utils.GetFID() + fileExtension,
                this._timeToTerminateIfNoImprovement * 1000, this._timeout * 1000);
            this._model.SetCallback(this.guorbiCallback);
            // Debug.Assert(this._timeToTerminateIfNoImprovement <= 0 || this._timeout <= 0);
            // if (this._timeToTerminateIfNoImprovement > 0 & this._storeProgress) {
            //     this.guorbiCallback = new GurobiCallback(this._model, this._logFileDirname,
            //             filename + "_" + Utils.GetFID() + fileExtension, this._timeToTerminateIfNoImprovement * 1000);
            //     this._model.SetCallback(this.guorbiCallback);
            // } else if (this._timeToTerminateIfNoImprovement > 0) {
            //     this.gurobiTerminationCallback = new GurobiTerminationCallback(this._model, this._timeToTerminateIfNoImprovement * 1000);
            //     this._model.SetCallback(this.gurobiTerminationCallback);
            // } else if (this._storeProgress) {
            //     this.gurobiStoreProgressCallback = new GurobiStoreProgressCallback(this._model, this._logFileDirname, filename + "_" + Utils.GetFID() + fileExtension);
            //     this._model.SetCallback(this.gurobiStoreProgressCallback);
            // }
        }

        /// <summary>
        /// to reset the timer for termination.
        /// </summary>
        protected void ResetCallbackTimer() {
            this.guorbiCallback.ResetAll();
            // if (this._timeToTerminateIfNoImprovement > 0 & this._storeProgress) {
            //     this.guorbiCallback.ResetTermination();
            //     this.guorbiCallback.ResetProgressTimer();
            //     this._model.SetCallback(this.guorbiCallback);
            // } else if (this._timeToTerminateIfNoImprovement > 0) {
            //     this.gurobiTerminationCallback.ResetTermination();
            //     this._model.SetCallback(this.gurobiTerminationCallback);
            // } else if (this._storeProgress) {
            //     this.gurobiStoreProgressCallback.ResetProgressTimer();
            //     this._model.SetCallback(this.gurobiStoreProgressCallback);
            // }
        }

        /// <summary>
        /// constructor.
        /// </summary>
        public GurobiSOS(double timeout = double.PositiveInfinity, int verbose = 0, int numThreads = 0, double timeToTerminateNoImprovement = -1,
                bool recordProgress = false, string logPath = null, bool focusBstBd = false)
        {
            this._model = new GRBModel(GurobiEnvironment.Instance);
            this._timeout = timeout;
            this._verbose = verbose;
            this._numThreads = numThreads;
            // this._model.Parameters.TimeLimit = timeout;
            this._model.Parameters.Presolve = 2;
            this._focusBstBd = focusBstBd;
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
        public void CleanAll(double timeout = -1, bool disableStoreProgress = false) {
            this._model.Dispose();
            this._model = new GRBModel(GurobiEnvironment.Instance);
            if (timeout > 0) {
                this._timeout = timeout;
            }
            // this._model.Parameters.TimeLimit = this._timeout;
            this._model.Parameters.Presolve = 2;
            this._constraintIneqCount = 0;
            this._constraintEqCount = 0;
            this._variables = new Dictionary<string, GRBVar>();
            this._auxiliaryVars = new Dictionary<string, GRBVar>();
            this._objective = 0;
            this._model.Parameters.Threads = this._numThreads;
            this._model.Parameters.OutputFlag = this._verbose;
            this.auxPolyList = new List<Polynomial<GRBVar>>();
            SetCallbacks(disableStoreProgress);
        }

        /// <summary>
        /// append as the next line of the store progress file.
        /// </summary>
        public void AppendToStoreProgressFile(double time_ms, double gap, bool reset = false) {
            this.guorbiCallback.AppendToStoreProgressFile(time_ms, gap);
            if (reset) {
                ResetCallbackTimer();
            }
        }

        /// <summary>
        /// Reset the solver by removing all the variables and constraints.
        /// </summary>
        public void CleanAll(bool focusBstBd, double timeout = -1) {
            this._focusBstBd = focusBstBd;
            CleanAll(timeout);
        }

        /// <summary>
        /// set the timeout.
        /// </summary>
        /// <param name="timeout">value for timeout.</param>
        public void SetTimeout(double timeout) {
            this._timeout = timeout;
            // this._model.Parameters.TimeLimit = timeout;
        }

        /// <summary>
        /// set the FocusBstBd.
        /// </summary>
        public void SetFocusBstBd(bool focusBstBd) {
            this._focusBstBd = focusBstBd;
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
        /// Create a new continuous variable with a given name.
        /// </summary>
        public GRBVar CreateContinuousVariable(string name, double lb = double.NegativeInfinity, double ub = double.PositiveInfinity)
        {
            return this.CreateVariable(name, type: GRB.CONTINUOUS, lb: lb, ub: ub);
        }

        /// <summary>
        /// Create a new binary variable with a given name.
        /// </summary>
        public GRBVar CreateBinaryVariable(string name)
        {
            return this.CreateVariable(name, type: GRB.BINARY);
        }

        /// <summary>
        /// Create a new integer variable with a given name.
        /// </summary>
        public GRBVar CreateIntegerVariable(string name, double lb = double.NegativeInfinity, double ub = double.PositiveInfinity)
        {
            return this.CreateVariable(name, type: GRB.INTEGER, lb: lb, ub: ub);
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
            foreach (var term in poly.GetTerms())
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
        /// Call the model update to apply new constraints and objectives.
        /// </summary>
        public void ModelUpdate()
        {
            this._model.Update();
        }

        /// <summary>
        /// wrapper that does type conversions then calls the original function.
        /// </summary>
        /// <param name="polynomial"></param>
        public string AddLeqZeroConstraint(Polynomial<GRBVar> polynomial)
        {
            this._constraintIneqCount++;
            string name = "ineq_index_" + this._constraintIneqCount;
            var constr = this._model.AddConstr(this.Convert(polynomial), GRB.LESS_EQUAL, 0.0, name);
            return name;
        }

        /// <summary>
        /// Converts quadratic to linear form for Gurobi.
        /// </summary>
        /// <returns>Linear expression.</returns>
        private GRBLinExpr ConvertQEToLinear(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            Utils.logger("Using big-M QE to Linear Conversion.", this._verbose);
            GRBLinExpr obj = this.Convert(linearPoly);

            for (int i = 0; i < coeffPolyList.Count; i++) {
                Polynomial<GRBVar> coeffPoly = coeffPolyList[i];
                GRBVar variable = variableList[i];
                foreach (var term in coeffPoly.GetTerms()) {
                    switch (term.Exponent) {
                        case 1:
                            GRBVar binary_variable = term.Variable.Value;
                            var auxVar = this.CreateVariable("aux_qe");
                            // if (binary_variable.VType == GRB.BINARY) {
                            obj.AddTerm(term.Coefficient, auxVar);
                            // aux >= y_i - M (1 - x_i)
                            var auxConst = new Polynomial<GRBVar>(new Term<GRBVar>(-1, variable));
                            auxConst.Add(new Term<GRBVar>(1 * this._bigM));
                            auxConst.Add(new Term<GRBVar>(-1 * this._bigM, binary_variable));
                            auxConst.Add(new Term<GRBVar>(1, auxVar));
                            this.AddLeqZeroConstraint(auxConst.Negate());
                            // aux >= -Mx_i
                            var auxConst3 = new Polynomial<GRBVar>(new Term<GRBVar>(-1, auxVar));
                            auxConst3.Add(new Term<GRBVar>(-1 * this._bigM, binary_variable));
                            this.AddLeqZeroConstraint(auxConst3);
                            // aux <= y_i + (1 - x_i)M
                            var auxConst1 = new Polynomial<GRBVar>(new Term<GRBVar>(-1, variable));
                            auxConst1.Add(new Term<GRBVar>(-1 * this._bigM));
                            auxConst1.Add(new Term<GRBVar>(1 * this._bigM, binary_variable));
                            auxConst1.Add(new Term<GRBVar>(1, auxVar));
                            this.AddLeqZeroConstraint(auxConst1);
                            // aux <= Mx_i
                            var auxConst2 = new Polynomial<GRBVar>(new Term<GRBVar>(1, auxVar));
                            auxConst2.Add(new Term<GRBVar>(-1  * this._bigM, binary_variable));
                            this.AddLeqZeroConstraint(auxConst2);
                            // auxQVarList.Add(auxVar);
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
        /// Converts Quadratic to Quadratic for gurobi.
        /// </summary>
        private GRBQuadExpr ConvertQEToQEExp(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            Utils.logger("Using QE expressions as they are.", this._verbose);
            GRBQuadExpr quadConstraint = this.Convert(linearPoly);
            for (int i = 0; i < coeffPolyList.Count; i++) {
                var coeffPoly = this.Convert(coeffPolyList[i]);
                GRBVar variable = variableList[i];
                quadConstraint += coeffPoly * variable;
            }
            return quadConstraint;
        }

        /// <summary>
        /// Converts Quadratic to Linear using SOS.
        /// </summary>
        private GRBLinExpr ConvertQESOS(IList<Polynomial<GRBVar>> coeffPolyList, IList<GRBVar> variableList, Polynomial<GRBVar> linearPoly)
        {
            Utils.logger("Using Indicator for QE Conversion.", this._verbose);
            GRBLinExpr obj = this.Convert(linearPoly);

            for (int i = 0; i < coeffPolyList.Count; i++) {
                Polynomial<GRBVar> coeffPoly = coeffPolyList[i];
                GRBVar variable = variableList[i];
                foreach (var term in coeffPoly.GetTerms()) {
                    switch (term.Exponent) {
                        case 1:
                            GRBVar binary_variable = term.Variable.Value;
                            var auxVar = this.CreateVariable("aux_qe");
                            // if (binary_variable.VType == GRB.BINARY) {
                            obj.AddTerm(term.Coefficient, auxVar);
                            // var auxiliaries = new GRBVar[] { 1 - binary_variable, auxVar };
                            this._model.AddGenConstrIndicator(binary_variable, 1, variable - auxVar, GRB.EQUAL, 0, "general_one_indicator_" + i);
                            this._model.AddGenConstrIndicator(binary_variable, 0, auxVar, GRB.EQUAL, 0, "general_zero_indicator_" + i);
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
            // throw new Exception("not necessary");
            string name = "ineq_index_" + this._constraintIneqCount++;
            GRBLinExpr quadConstraint = this.ConvertQEToLinear(coeffPolyList, variableList, linearPoly);
            this._model.AddConstr(quadConstraint, GRB.LESS_EQUAL, 0.0, name);
            // var quadConstraint = this.ConvertQEToQEExp(coeffPolyList, variableList, linearPoly);
            // this._model.AddQConstr(quadConstraint, GRB.LESS_EQUAL, 0.0, name);
            // GRBLinExpr quadConstraint = this.ConvertQESOS(coeffPolyList, variableList, linearPoly);
            // this._model.AddConstr(quadConstraint, GRB.LESS_EQUAL, 0.0, name);
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
            GRBLinExpr quadConstraint = this.ConvertQEToLinear(coeffPolyList, variableList, linearPoly);
            this._model.AddConstr(quadConstraint, GRB.EQUAL, 0.0, name);
            // GRBLinExpr quadConstraint = this.ConvertQESOS(coeffPolyList, variableList, linearPoly);
            // this._model.AddConstr(quadConstraint, GRB.EQUAL, 0.0, name);
            // var quadConstraint = this.ConvertQEToQEExp(coeffPolyList, variableList, linearPoly);
            // this._model.AddQConstr(quadConstraint, GRB.EQUAL, 0.0, name);
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
        /// Add a = max(b, c) constraint.
        /// </summary>
        public void AddMaxConstraint(GRBVar LHS, Polynomial<GRBVar> maxItem1, Polynomial<GRBVar> maxItem2) {
            // Create auxilary variable for each polynomial
            var var_1 = this._model.AddVar(Double.NegativeInfinity, Double.PositiveInfinity, 0, GRB.CONTINUOUS, "aux_" + this._auxiliaryVars.Count);
            this._auxiliaryVars.Add($"aux_{this._auxiliaryVars.Count}", var_1);

            var var_2 = this._model.AddVar(Double.NegativeInfinity, Double.PositiveInfinity, 0, GRB.CONTINUOUS, "aux_" + this._auxiliaryVars.Count);
            this._auxiliaryVars.Add($"aux_{this._auxiliaryVars.Count}", var_2);

            this._model.AddConstr(this.Convert(maxItem1), GRB.EQUAL, var_1, "eq_index_" + this._constraintEqCount++);
            this._model.AddConstr(this.Convert(maxItem2), GRB.EQUAL, var_2, "eq_index_" + this._constraintEqCount++);
            // Add Max Constraint
            this._model.AddGenConstrMax(LHS, new GRBVar[] { var_2, var_1 }, 0.0, "min_constraint");
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
            foreach (var auxVar in auxPolyList) {
                objective += this.Convert(auxVar);
            }
            this._model.SetObjective(objective + this._objective, GRB.MAXIMIZE);
            if (this._focusBstBd) {
                this._model.Parameters.MIPFocus = 3;
                this._model.Parameters.Heuristics = 0.01;
                this._model.Parameters.Cuts = 0;
            } else {
                this._model.Parameters.MIPFocus = 1;
                this._model.Parameters.Heuristics = 0.99;
                this._model.Parameters.RINS = GRB.MAXINT;
                // this._model.Parameters.ConcurrentMIP = 4;
                // this._model.Parameters.ImproveStartTime = 200;
                // this._model.Parameters.Cuts = 0;
            }
            // this._model = this._model.Relax();
            // this._model.Parameters.DisplayInterval = 10;
            // this._model.Parameters.MIPFocus = 3;
            // this._model.Parameters.Cuts = 3;
            // this._model.Parameters.Heuristics = 0.5;
            // this._model.Parameters.NumericFocus = 3;
            // this._model.Parameters.Quad = 1;
            // this._model.Parameters.QCPDual = 1;
            // this._model.Set(GRB.DoubleParam.IntFeasTol, this._tolerance);
            // this._model.Set(GRB.DoubleParam.FeasibilityTol, this._tolerance);
            // string exhaust_dir_name = @"../logs/grbsos_exhaust/rand_" + (new Random()).Next(1000) + @"/";
            // Directory.CreateDirectory(exhaust_dir_name);
            // this._model.Write($"{exhaust_dir_name}/model_" + DateTime.Now.Millisecond + ".lp");

            this._model.Optimize();
            if (this._model.Status != GRB.Status.TIME_LIMIT & this._model.Status != GRB.Status.OPTIMAL & this._model.Status != GRB.Status.INTERRUPTED)
            {
                this._model.Parameters.DualReductions = 0;
                this._model.Reset();
                this._model.Optimize();
                this._model.ComputeIIS();
                string exhaust_dir_name = @"../logs/grbsos_exhaust/rand_" + (new Random()).Next(1000) + @"/";
                Directory.CreateDirectory(exhaust_dir_name);
                this._model.Write($"{exhaust_dir_name}/model_infeas_reduce_" + DateTime.Now.Millisecond + ".lp");
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

        /// <summary>
        /// Get the resulting value assigned to a variable.
        /// </summary>
        public double GetDualVariable(GRBModel solution, string constraintName) {
            if (solution.Status != GRB.Status.USER_OBJ_LIMIT & solution.Status != GRB.Status.TIME_LIMIT
                & solution.Status != GRB.Status.OPTIMAL & solution.Status != GRB.Status.INTERRUPTED)
            {
                throw new Exception("can't read status since model is not optimal");
            }
            return this._model.GetConstrByName(constraintName).Pi;
        }

        /// <summary>
        /// initialize some of the variables.
        /// </summary>
        public void InitializeVariables(GRBVar variable, int value) {
            variable.Start = value;
        }

        /// <summary>
        /// adding some auxiliary term to be added to the global objective when maximized.
        /// </summary>
        public void AddGlobalTerm(Polynomial<GRBVar> auxObjPoly)
        {
            this.auxPolyList.Add(auxObjPoly);
        }
    }
}
