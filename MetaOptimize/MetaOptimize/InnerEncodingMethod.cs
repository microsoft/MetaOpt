namespace MetaOptimize
{
    /// <summary>
    /// The method for encoding inner problem.
    /// </summary>
    public enum InnerRewriteMethodChoice
    {
        /// <summary>
        /// use kkt encoding.
        /// </summary>
        KKT,
        /// <summary>
        /// do the primal dual.
        /// </summary>
        PrimalDual,
    }
}