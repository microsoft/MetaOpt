// <copyright file="KktOptimizationGenerator.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Gurobi;
    class GurobiCallback : GRBCallback
    {
        private GurobiStoreProgressCallback storeProgressCallback;
        private GurobiTerminationCallback terminationCallback;

        public GurobiCallback(GRBModel model, String dirname, String filename, double terminateNoImprovement_ms) {
            this.storeProgressCallback = new GurobiStoreProgressCallback(model, dirname, filename);
            this.terminationCallback = new GurobiTerminationCallback(model, terminateNoImprovement_ms);
        }

        protected override void Callback()
        {
            try {
                if (where == GRB.Callback.MIP) {
                    var obj = GetDoubleInfo(GRB.Callback.MIP_OBJBST);
                    this.storeProgressCallback.CallCallback(obj);
                    this.terminationCallback.CallCallback(obj);
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
    }
}