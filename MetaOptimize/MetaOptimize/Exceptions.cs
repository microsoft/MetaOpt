using System;

namespace MetaOptimize;

/// <summary>
/// some userdefined exception to throw when solution is not optimal.
/// </summary>
public class InfeasibleOrUnboundSolution : Exception
{
    /// <summary>
    /// the exception.
    /// </summary>
    public void InfeasibleOrUnboundSolutionException()
    {
    }
}
