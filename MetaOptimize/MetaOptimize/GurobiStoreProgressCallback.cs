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
        private String dirname;
        private String filename;
        private double timeBias = 0.0;
        private double presolvetime_ms = -1;

        public GurobiStoreProgressCallback(GRBModel model, String dirname, String filename) {
            this.model = model;
            this.dirname = dirname;
            this.filename = filename;
            Utils.CreateFile(dirname, filename, removeIfExist: false);
            Console.WriteLine("will store the progress in dir: " + this.dirname + " on file " + this.filename);
            // this.presolvetimer = null;
            Utils.AppendToFile(this.dirname, this.filename, 0 + ", " + 0);
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.PRESOLVE) {
                    this.presolvetime_ms = GetDoubleInfo(GRB.Callback.RUNTIME) * 1000;
                }
                else if (where == GRB.Callback.MIP) {
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    var currtime_ms = GetDoubleInfo(GRB.Callback.RUNTIME);
                    CallCallback(obj, currtime_ms, this.presolvetime_ms);
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

        public void CallCallback(double objective, double currtime_ms, double presolvetime_ms)
        {
            double time = timeBias + currtime_ms - presolvetime_ms;
            Utils.AppendToFile(dirname, filename, time + ", " + objective);
        }

        public void ResetProgressTimer()
        {
            this.timeBias = Double.Parse(Utils.readLastLineFile(this.dirname, this.filename).Split(", ")[0]);
            // Utils.AppendToFile(@"../logs/logs.txt", "time bias = " + timeBias);
        }
    }
}