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

        public GurobiStoreProgressCallback(GRBModel model, String dirname, String filename) {
            this.model = model;
            this.dirname = dirname;
            this.filename = filename;
            Utils.CreateFile(dirname, filename, removeIfExist: false);
            Console.WriteLine("will store the progress in dir: " + this.dirname + " on file " + this.filename);
            this.timer = Stopwatch.StartNew();
            Utils.AppendToFile(this.dirname, this.filename, timer.ElapsedMilliseconds + ", " + 0);
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIP) {
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    CallCallback(obj);
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

        public void CallCallback(double objective)
        {
            Utils.AppendToFile(dirname, filename, timer.ElapsedMilliseconds + ", " + objective);
        }
    }
}