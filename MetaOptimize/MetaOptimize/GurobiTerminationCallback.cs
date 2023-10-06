// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    using Gurobi;
    class GurobiTerminationCallback : GRBCallback
    {
        private GRBModel model;
        private double prevObj;
        private double prevTime_ms;
        private double terminateNoImprovement_ms;

        public GurobiTerminationCallback(GRBModel model, double terminateNoImprovement_ms) {
            this.model = model;
            this.prevObj = double.NaN;
            this.terminateNoImprovement_ms = terminateNoImprovement_ms;
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIPNODE) {
                    var obj = GetDoubleInfo(GRB.Callback.MIPNODE_OBJBST);
                    CallCallback(obj, GetDoubleInfo(GRB.Callback.RUNTIME) * 1000);
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

        public void CallCallback(double obj, double time_ms)
        {
            if (Double.IsNaN(prevObj)) {
                prevObj = obj;
                return;
            }

            if (prevObj * (1 + 1e-6) >= obj)
            {
                if (time_ms - prevTime_ms >= terminateNoImprovement_ms)
                {
                    model.Terminate();
                }
            }
            else
            {
                prevObj = obj;
                prevTime_ms = time_ms;
            }
        }

        public void ResetTermination()
        {
            this.prevObj = double.NaN;
        }
    }
}