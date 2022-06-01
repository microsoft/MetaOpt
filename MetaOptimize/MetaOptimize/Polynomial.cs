// <copyright file="Polynomial.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace MetaOptimize
{
    using System.Collections.Generic;
    using System.Linq;
    using ZenLib;

    /// <summary>
    /// A polynomial such as 3x + 2y + 1.
    /// </summary>
    public class Polynomial<TVar>
    {
        /// <summary>
        /// The polynomial terms.
        /// </summary>
        public List<Term<TVar>> Terms { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial{TVar}"/> class.
        /// </summary>
        public Polynomial()
        {
            this.Terms = new List<Term<TVar>>();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial{TVar}"/> class.
        /// </summary>
        /// <param name="polynomialTerms">The terms.</param>
        public Polynomial(List<Term<TVar>> polynomialTerms)
        {
            Terms = polynomialTerms;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial{TVar}"/> class.
        /// </summary>
        /// <param name="polynomialTerms">The terms.</param>
        public Polynomial(params Term<TVar>[] polynomialTerms)
        {
            Terms = new List<Term<TVar>>(polynomialTerms);
        }

        /// <summary>
        /// Convert this polynomial to a Zen form.
        /// </summary>
        /// <returns>A real Zen expression.</returns>
        public Zen<Real> AsZen()
        {
            var p = Zen.Constant(new Real(0));
            foreach (var term in this.Terms)
            {
                p = p + term.AsZen();
            }

            return p;
        }

        /// <summary>
        /// Compute the partial derivative of this polynomial with respect a variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <returns>The result as a polynomial.</returns>
        public double Derivative(TVar variable)
        {
            return this.Terms.Select(x => x.Derivative(variable)).Aggregate((a, b) => a + b);
        }

        /// <summary>
        /// Negate the polynomial.
        /// </summary>
        /// <returns>The result as a polynomial.</returns>
        public Polynomial<TVar> Negate()
        {
            return new Polynomial<TVar>(this.Terms.Select(x => x.Negate()).ToList());
        }

        /// <summary>
        /// Convert the polynomial to a string.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return string.Join(" + ", this.Terms.Select(x => x.ToString()));
        }
        /// <summary>
        /// Checks if all terms in
        /// this linear polynomial
        /// are in variabls.
        /// </summary>
        /// <param name="variables"></param>
        /// <returns></returns>
        public bool isallInSetOrConst(ISet<TVar> variables)
        {
            // how many terms are not in the set and not const.
            var count = this.Terms.Where(x => !x.isInSetOrConst(variables)).Count();
            if (count == 0)
            {
                return true;
            }
            return false;
        }
    }
}
