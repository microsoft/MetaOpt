// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Gurobi;
    class GurobiStoreProgressCallback : GRBCallback
    {
        private GRBModel model;
        private IProgress<MetaOptimize.Explainability.SolverProgress> progress;
        private double presolvetime_ms = -1;
        private double bstObj = Double.NegativeInfinity;
        private double bstBnd = Double.PositiveInfinity;
        private double lastTime = -1;

        public GurobiStoreProgressCallback(GRBModel model, IProgress<MetaOptimize.Explainability.SolverProgress> progress) {
            this.model = model;
            this.progress = progress;
            // this.presolvetimer = null;
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.PRESOLVE) {
                    this.presolvetime_ms = GetDoubleInfo(GRB.Callback.RUNTIME) * 1000;
                }
                else if (where == GRB.Callback.MIP) {
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    var bnd = GetDoubleInfo(GRB.Callback.MIP_OBJBND);
                    var currtime_ms = GetDoubleInfo(GRB.Callback.RUNTIME) * 1000;
                    CallCallback(obj, bnd, currtime_ms, this.presolvetime_ms);
                }
            } catch (GRBException e) {
                Console.WriteLine("Error code: " + e.ErrorCode);
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            } catch (Exception e) {
                Console.WriteLine("Error during callback");
                Console.WriteLine(e.StackTrace);
            }
            throw new Exception("Should not enter this function.");
        }

        public void CallCallback(double objective, double bound, double currtime_ms, double presolvetime_ms)
        {
            this.bstObj = Math.Max(this.bstObj, objective);
            this.bstBnd = Math.Min(this.bstBnd, bound);
            double time = currtime_ms - presolvetime_ms;
            if (time >= lastTime + 100)
            {
                progress.Report(new(bstObj, bound, TimeSpan.FromMilliseconds(time)));
                this.lastTime = time;
            }
        }

        public void WriteLastLineBeforeTermination(double finaltime_ms)
        {
            // Utils.AppendToFile(@"../logs/logs.txt", " last time = " + lastTime + " final time = " + finaltime_ms);
            if (finaltime_ms > lastTime)
            {
                progress.Report(new(bstObj, bstBnd, TimeSpan.FromMilliseconds(finaltime_ms)));
                this.lastTime = finaltime_ms;
            }
        }

        public void AppendToStoreProgressFile(double time_ms, double gap) {
            throw new Exception("Not supported any more");
        }

        public void ResetProgressTimer()
        {
            this.presolvetime_ms = 0;
            // Utils.AppendToFile(@"../logs/logs.txt", "time bias = " + timeBias);
        }
    }
}