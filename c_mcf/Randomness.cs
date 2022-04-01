using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Linq;

public class Randomness
{
    // sk: commented out because mkl.dll is not available!
    // GaussRandMKL gauss = new GaussRandMKL(0);

    RNGCryptoServiceProvider cryptoRandGen;
    Random rand = null;
    public Randomness()
    {
        cryptoRandGen = new RNGCryptoServiceProvider();
    }
    public Randomness(int seed)
    {
        rand = new Random(seed);
    }
    public double pickRandomDouble()
    {
        if (rand == null)
        {
            byte[] data = new byte[sizeof(UInt32)];
            cryptoRandGen.GetBytes(data);

            double maxPoss = UInt32.MaxValue;
            uint sInt = BitConverter.ToUInt32(data, 0);
            if (sInt == UInt32.MaxValue) sInt -= 1;
            double sample = sInt / maxPoss;

            Debug.Assert(sample <= 1 && sample >= 0);

            return sample;
        }
        else
            return rand.NextDouble();
    }
    public double pickRandomDouble(double minV, double maxV)
    {
        return pickRandomDouble() * (maxV - minV) + minV;
    }
    public int pickRandomInt(int maxRange) // 0 -- maxRange-1
    {
        return pickRandomInt(0, maxRange - 1);
    }
    public int pickRandomInt(int minRange, int maxRange)// both inclusive
    {
        // range has to be the same size as # of ints needed
        int val = (int)Math.Floor(pickRandomDouble() * (maxRange - minRange + 1)) + minRange;

        /*
        if (val == maxRange && maxRange != minRange)
            val -= 1;
         */

        Debug.Assert(val >= minRange && val <= maxRange);
        return val;
    }

    // mean = 0, stdev = 1
    public double GetNormal()
    {
        // Use Box-Muller algorithm
        double u1 = pickRandomDouble();
        double u2 = pickRandomDouble();
        double r = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        return r * Math.Sin(theta);
    }


    public double GetNormalSample(double mean, double stdev)
    {
        return GetNormal() * stdev + mean;
    }

    public double GetExponentialSample(double mean)
    {
        //
        // Let X be U [0, 1]
        // note mean = 1/\lambda for exponential
        // Pr (Y < y ) = Pr (-1/\lambda logX < y ) = Pr ( X > exp(-\lambda y)) = 1 - exp (-\lambda y)
        // 
        // Hence Y = -1 * mean * log (X)
        //
        Debug.Assert(mean > 0);
        return -1 * mean * Math.Log(pickRandomDouble());
    }
    public double GetParetoSample(double shape_alpha, double scale)
    {
        // 
        // Let X be U [0, 1]
        // note scale is x_m. cdf Pr(Y < y ) = 1 - (x_m/y)^\alpha = ... = Pr ( x_m/ X^{1/\alpha}  < y)
        // 
        // Hence Y = x_m/ Pow(X, 1/ \alpha)
        //
        Debug.Assert(shape_alpha > 0 && scale > 0);
        return scale / Math.Pow(pickRandomDouble(), 1.0 / shape_alpha);
    }

    public int[] GetRandomPermutation(int n)
    {
        return GetRandomPermutation(n, n);
    }

    /// <summary>
    /// Random permutation of 0 ... n-1
    /// Second parameter m is optional
    ///   m \in [0, n]
    ///   when specified it yields only m out of the n values
    /// </summary>
    /// <param name="n"></param>
    /// <param name="m"></param>
    /// <returns></returns>
    public int[] GetRandomPermutation(int n, int m)
    {
        Debug.Assert(m >= 0 && m <= n);

        // return an array with a random permutation of integers 0, 1, ... n-1
        int[] retval = new int[m];

        List<int> allIndices = new List<int>();

        for (int i = 0; i < n; i++)
            allIndices.Add(i);

        for (int i = 0; i < m; i++)
        {
            int pick = pickRandomInt(allIndices.Count);
            retval[i] = allIndices[pick];

            allIndices.RemoveAt(pick);
        }

        // Debug, remove once you are sure of this code
        if (m == n)
        {
            int last = -1;
            foreach (int x in retval.OrderBy(i => i))
                Debug.Assert(x == ++last);
        }

        return retval;
    }
}
