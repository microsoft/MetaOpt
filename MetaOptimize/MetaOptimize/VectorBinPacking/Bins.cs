// <copyright file="Topology.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    /// <summary>
    /// A simple bin class that contains the bin sizes.
    /// </summary>
    public class Bins
    {
        private List<List<double>> binSizeList;

        /// <summary>
        /// Creates a new instance of the <see cref="Bins"/> class.
        /// </summary>
        public Bins(int numBins, IList<double> binSize)
        {
            this.binSizeList = new List<List<double>>();
            for (int i = 0; i < numBins; i++) {
                this.binSizeList.Add(new List<double>(binSize));
            }
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Bins"/> class.
        /// </summary>
        public Bins(IList<IList<double>> binList)
        {
            this.binSizeList = new List<List<double>>();
            foreach (var binSize in binList) {
                this.binSizeList.Add(binSize.ToList());
            }
        }

        /// <summary>
        /// Get list of bin sizes.
        /// </summary>
        public ReadOnlyCollection<ReadOnlyCollection<double>> getBinSizes()
        {
            var binList = new List<ReadOnlyCollection<double>>();
            foreach (var binSize in this.binSizeList) {
                binList.Add(binSize.AsReadOnly());
            }
            return binList.AsReadOnly();
        }

        /// <summary>
        /// return number of bins.
        /// </summary>
        public int GetNum()
        {
            return this.binSizeList.Count;
        }

        /// <summary>
        /// return max capacity.
        /// </summary>
        public double MaxCapacity(int dim)
        {
            double maxCap = 0;
            foreach (var binSize in this.binSizeList) {
                maxCap = Math.Max(maxCap, binSize[dim]);
            }
            return maxCap;
        }
    }
}