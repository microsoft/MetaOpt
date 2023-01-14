// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using Gurobi;
    class GurobiTimeoutCallback : GRBCallback
    {
        private GRBModel model;
        private Stopwatch timer;
        private double timeout;

        public GurobiTimeoutCallback(GRBModel model, double timeout) {
            this.model = model;
            this.timer = null;
            this.timeout = timeout;
            if (this.timeout <= 0) {
                this.timeout = Double.PositiveInfinity;
            }
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIP) {
                    CallCallback();
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
        public void CallCallback()
        {
            if (this.timer == null) {
                this.timer = Stopwatch.StartNew();
            }
            if (this.timer.ElapsedMilliseconds > timeout) {
                this.model.Terminate();
            }
        }
        public void ResetTermination()
        {
            this.timer = null;
        }
    }
}