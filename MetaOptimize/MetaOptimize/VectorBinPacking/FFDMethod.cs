namespace MetaOptimize
{
    /// <summary>
    /// The method for FFD.
    /// </summary>
    public enum FFDMethodChoice
    {
        /// <summary>
        /// sequentially place items without sorting.
        /// </summary>
        FF,
        /// <summary>
        /// use the sum over different dimensions to sort items.
        /// </summary>
        FFDSum,
        /// <summary>
        /// use the product of different dimensions to sort items.
        /// </summary>
        FFDProd,
        /// <summary>
        /// use division of first dimension by the second dimension to sort items (only for two dimension).
        /// </summary>
        FFDDiv,
    }
}