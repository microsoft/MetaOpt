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
        private Stopwatch timer;
        private String dirname;
        private String filename;
        private double timeBias = 0.0;
        // private double presolvetime_ms = -1;
        // private Stopwatch presolvetimer;

        public GurobiStoreProgressCallback(GRBModel model, String dirname, String filename) {
            this.model = model;
            this.dirname = dirname;
            this.filename = filename;
            Utils.CreateFile(dirname, filename, removeIfExist: false);
            Console.WriteLine("will store the progress in dir: " + this.dirname + " on file " + this.filename);
            this.timer = null;
            // this.presolvetimer = null;
            Utils.AppendToFile(this.dirname, this.filename, 0 + ", " + 0);
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIP) {
                    // if (presolvetime_ms <= 0) {
                    //     presolvetime_ms = presolvetimer.ElapsedMilliseconds;
                    //     presolvetimer.Stop();
                    // }
                    // if (this.timer == null) {
                    //     this.timer = Stopwatch.StartNew();
                    // }
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    CallCallback(obj);
                }
                // else if (where == GRB.Callback.PRESOLVE) {
                //     if (this.presolvetimer == null) {
                //         presolvetimer = Stopwatch.StartNew();
                //     }
                // }
            } catch (GRBException e) {
                Console.WriteLine("Error code: " + e.ErrorCode);
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            } catch (Exception e) {
                Console.WriteLine("Error during callback");
                Console.WriteLine(e.StackTrace);
            }
        }

        public void CallCallback(double objective)
        {
            if (this.timer == null) {
                this.timer = Stopwatch.StartNew();
            }
            double time = timeBias + timer.ElapsedMilliseconds;
            // if (presolvetime_ms > 0) {
            //     time -= presolvetime_ms;
            // }
            Utils.AppendToFile(dirname, filename, time + ", " + objective);
        }

        public void ResetProgressTimer()
        {
            this.timeBias = Double.Parse(Utils.readLastLineFile(this.dirname, this.filename).Split(", ")[0]);
            this.timer = null;
            // this.presolvetime_ms = -1;
            // this.presolvetimer = null;
        }
    }
}