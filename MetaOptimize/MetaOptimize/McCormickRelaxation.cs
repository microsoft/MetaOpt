namespace MetaOptimize
{
    /// <summary>
    /// using McCormick enveloped for relaxing bilinear terms.
    /// </summary>
    public static class McCormickRelaxation<TVar, TSolution>
    {
        /// <summary>
        /// relaxation.
        /// assume x \in [xL, xU].
        /// assume y \ine [yL, yU]
        /// z = xy is replaced with.
        /// 1. z \leq xU y + x yL - xU yL.
        /// 2. z \leq xL y + x yU - xL yU.
        /// 3. z \geq xL y + x yL - xL yL.
        /// 4. z \geq xU y + x yU - xU yU.
        /// </summary>
        public static void Bilinear(ISolver<TVar, TSolution> solver, TVar x, TVar y, TVar output,
            double xLB, double xUB, double yLB, double yUB)
        {
            Bilinear(solver, x, new Polynomial<TVar>(new Term<TVar>(1, y)), output, xLB, xUB, yLB, yUB);
        }

        /// <summary>
        /// mccormick relaxation take polynomial as input.
        /// </summary>
        public static void Bilinear(ISolver<TVar, TSolution> solver, TVar x, Polynomial<TVar> y, TVar output,
            double xLB, double xUB, double yLB, double yUB)
        {
            Bilinear(solver, x, y, new Polynomial<TVar>(new Term<TVar>(1, output)), xLB, xUB, yLB, yUB);
        }

        /// <summary>
        /// mccormick relaxation take polynomial as input.
        /// </summary>
        public static void Bilinear(ISolver<TVar, TSolution> solver, TVar x, Polynomial<TVar> y, Polynomial<TVar> output,
            double xLB, double xUB, double yLB, double yUB)
        {
            Bilinear(solver, new Polynomial<TVar>(new Term<TVar>(1, x)), y, output, xLB, xUB, yLB, yUB);
        }

        /// <summary>
        /// mccormick relaxation take polynomial as input.
        /// </summary>
        public static void Bilinear(ISolver<TVar, TSolution> solver, Polynomial<TVar> x, Polynomial<TVar> y, Polynomial<TVar> output,
            double xLB, double xUB, double yLB, double yUB)
        {
            var constr1 = new Polynomial<TVar>(new Term<TVar>(xUB * yLB));
            constr1.Add(output.Copy());
            constr1.Add(x.Multiply(-yLB));
            constr1.Add(y.Multiply(-xUB));
            solver.AddLeqZeroConstraint(constr1);

            var constr2 = new Polynomial<TVar>(new Term<TVar>(xLB * yUB));
            constr2.Add(output.Copy());
            constr2.Add(x.Multiply(-yUB));
            constr2.Add(y.Multiply(-xLB));
            solver.AddLeqZeroConstraint(constr2);

            var constr3 = new Polynomial<TVar>(new Term<TVar>(-xLB * yLB));
            constr3.Add(output.Negate());
            constr3.Add(x.Multiply(yLB));
            constr3.Add(y.Multiply(xLB));
            solver.AddLeqZeroConstraint(constr3);

            var constr4 = new Polynomial<TVar>(new Term<TVar>(-xUB * yUB));
            constr4.Add(output.Negate());
            constr4.Add(x.Multiply(yUB));
            constr4.Add(y.Multiply(xUB));
            solver.AddLeqZeroConstraint(constr4);
        }
    }
}