using System;

namespace MetaOptimize;

/// <summary>
/// Contains information about the progress of a solver.
/// </summary>
/// <param name="Objective">The current objective.</param>
/// <param name="Bound">The current bound.</param>
/// <param name="Time">The current timestamp.</param>
public sealed record SolverProgress(double Objective, double Bound, TimeSpan Time);