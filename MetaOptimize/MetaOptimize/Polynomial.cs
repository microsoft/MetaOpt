// <copyright file="Polynomial.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A polynomial such as 3x + 2y + 1.
    /// </summary>
    public class Polynomial
    {
        /// <summary>
        /// The polynomial terms.
        /// </summary>
        public List<Term> Terms { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial"/> class.
        /// </summary>
        public Polynomial()
        {
            this.Terms = new List<Term>();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial"/> class.
        /// </summary>
        /// <param name="polynomialTerms">The terms.</param>
        public Polynomial(List<Term> polynomialTerms)
        {
            Terms = polynomialTerms;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial"/> class.
        /// </summary>
        /// <param name="polynomialTerms">The terms.</param>
        public Polynomial(params Term[] polynomialTerms)
        {
            Terms = new List<Term>(polynomialTerms);
        }

        /// <summary>
        /// Convert this polynomial to a Zen form.
        /// </summary>
        /// <param name="variables">The variable mapping.</param>
        /// <returns>A real Zen expression.</returns>
        public Zen<Real> AsZen(ISet<Zen<Real>> variables)
        {
            var p = Zen.Constant(new Real(0));
            foreach (var term in this.Terms)
            {
                p = p + term.AsZen(variables);
            }

            return p;
        }

        /// <summary>
        /// Compute the partial derivative of this polynomial with respect a variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <returns>The result as a polynomial.</returns>
        public Real Derivative(Zen<Real> variable)
        {
            return this.Terms.Select(x => x.Derivative(variable)).Aggregate((a, b) => a + b);
        }

        /// <summary>
        /// Convert the polynomial to a string.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return string.Join(" + ", this.Terms.Select(x => x.ToString()));
        }
    }
}
