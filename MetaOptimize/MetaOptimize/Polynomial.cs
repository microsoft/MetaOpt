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
        public List<PolynomialTerm> PolynomialTerms { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial"/> class.
        /// </summary>
        public Polynomial()
        {
            this.PolynomialTerms = new List<PolynomialTerm>();
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Polynomial"/> class.
        /// </summary>
        /// <param name="polynomialTerms">The terms.</param>
        public Polynomial(List<PolynomialTerm> polynomialTerms)
        {
            PolynomialTerms = polynomialTerms;
        }

        /// <summary>
        /// Convert this polynomial to a Zen form.
        /// </summary>
        /// <param name="variables">The variable mapping.</param>
        /// <returns>A real Zen expression.</returns>
        public Zen<Real> AsZen(BiDictionary<string, Zen<Real>> variables)
        {
            var p = Zen.Constant(new Real(0));
            foreach (var term in this.PolynomialTerms)
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
        public Real Derivative(string variable)
        {
            return this.PolynomialTerms.Select(x => x.Derivative(variable)).Aggregate((a, b) => a + b);
        }

        /// <summary>
        /// Convert the polynomial to a string.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            return string.Join(" + ", this.PolynomialTerms.Select(x => x.ToString()));
        }
    }
}
