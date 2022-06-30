// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using Gurobi;
    class GurobiTerminationCallback : GRBCallback
    {
        private GRBModel model;
        private double prevObj;
        private Stopwatch timer;
        private double terminateNoImprovement_ms;

        public GurobiTerminationCallback(GRBModel model, double terminateNoImprovement_ms) {
            this.model = model;
            this.prevObj = double.NaN;
            this.timer = Stopwatch.StartNew();
            this.terminateNoImprovement_ms = terminateNoImprovement_ms;
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIPNODE) {
                    var obj = GetDoubleInfo(GRB.Callback.MIPNODE_OBJBST);
                    CallCallback(obj);
                }
                if (this.timer.ElapsedMilliseconds > terminateNoImprovement_ms) {
                    this.model.Terminate();
                }
            } catch (GRBException e) {
                Console.WriteLine("Error code: " + e.ErrorCode);
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            } catch (Exception e) {
                Console.WriteLine("Error during callback");
                Console.WriteLine(e.StackTrace);
            }
        }

        public void CallCallback(double obj)
        {
            if (Double.IsNaN(prevObj)) {
                prevObj = obj;
                this.timer = Stopwatch.StartNew();
            }
            if (Math.Abs(obj - prevObj) > 0.01) {
                prevObj = obj;
                this.timer = Stopwatch.StartNew();
            }
        }

        public void ResetTermination()
        {
            this.prevObj = double.NaN;
            this.timer = Stopwatch.StartNew();
        }
    }
}