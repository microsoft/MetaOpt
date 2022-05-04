// <copyright file="PolynomialTerm.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System.Collections.Generic;

    /// <summary>
    /// A polynomial term.
    /// </summary>
    public class Term
    {
        /// <summary>
        /// The variable name.
        /// </summary>
        public Zen<Real> Variable { get; set; }

        /// <summary>
        /// The coefficient for the term.
        /// </summary>
        public Real Coefficient { get; set; }

        /// <summary>
        /// The exponent for the term.
        /// </summary>
        public byte Exponent { get; set; }

        /// <summary>
        /// Creates a new instance of the <see cref="Term"/> class.
        /// </summary>
        /// <param name="coefficient">The constant coefficient.</param>
        public Term(Real coefficient)
        {
            this.Coefficient = coefficient;
            this.Exponent = 0;
            this.Variable = null;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Term"/> class.
        /// </summary>
        /// <param name="coefficient">The constant coefficient.</param>
        /// <param name="variable">The variable name.</param>
        public Term(Real coefficient, Zen<Real> variable)
        {
            this.Coefficient = coefficient;
            this.Variable = variable;
            this.Exponent = 1;
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Term"/> class.
        /// </summary>
        /// <param name="coefficient">The constant coefficient.</param>
        /// <param name="variable">The variable name.</param>
        /// <param name="exponent">The exponent.</param>
        public Term(Real coefficient, Zen<Real> variable, byte exponent)
        {
            this.Coefficient = coefficient;
            this.Variable = variable;
            this.Exponent = exponent;
        }

        /// <summary>
        /// Convert this polynomial term to a Zen form.
        /// </summary>
        /// <param name="variables">The variable mapping.</param>
        /// <returns>A real Zen expression.</returns>
        public Zen<Real> AsZen(ISet<Zen<Real>> variables)
        {
            if (this.Exponent == 0)
            {
                return this.Coefficient;
            }

            if (this.Exponent == 1)
            {
                return this.Coefficient * this.Variable;
            }

            throw new System.Exception($"exponent can only be 0 or 1.");
        }

        /// <summary>
        /// Compute the partial derivative of this polynomial term with respect a variable.
        /// </summary>
        /// <param name="variable">The variable.</param>
        /// <returns>The result as a polynomial.</returns>
        public Real Derivative(Zen<Real> variable)
        {
            if (this.Exponent == 0 || !this.Variable.Equals(variable))
            {
                return 0;
            }
            else
            {
                return this.Coefficient;
            }
        }

        /// <summary>
        /// Negate this polynomial term.
        /// </summary>
        /// <returns></returns>
        public Term Negate()
        {
            return new Term(-1 * this.Coefficient, this.Variable, this.Exponent);
        }

        /// <summary>
        /// Convert this term to a string.
        /// </summary>
        /// <returns>The string representation.</returns>
        public override string ToString()
        {
            if (this.Exponent == 0)
            {
                return this.Coefficient.ToString();
            }
            else
            {
                var prefix = this.Coefficient == new Real(1) ? string.Empty : (this.Coefficient == new Real(-1) ? "-" : this.Coefficient.ToString() + "*");
                return $"{prefix}{this.Variable}";
            }
        }
    }
}
