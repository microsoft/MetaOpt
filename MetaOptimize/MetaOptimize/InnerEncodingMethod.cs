namespace MetaOptimize
{
    /// <summary>
    /// The method for encoding inner problem.
    /// </summary>
    public enum InnerEncodingMethodChoice
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