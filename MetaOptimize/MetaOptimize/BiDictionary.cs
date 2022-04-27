// <copyright file="BiDictionary.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace ZenLib
{
    using System.Collections.Generic;

    /// <summary>
    /// A simple 2-way dictionary.
    /// </summary>
    public class BiDictionary<TKey, TValue>
    {
        /// <summary>
        /// The forwards map.
        /// </summary>
        public Dictionary<TKey, TValue> ForwardMap { get; } = new Dictionary<TKey, TValue>();

        /// <summary>
        /// The backwards map.
        /// </summary>
        public Dictionary<TValue, TKey> BackwardsMap { get; } = new Dictionary<TValue, TKey>();

        /// <summary>
        /// Associate a key and a value.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        public void Associate(TKey key, TValue value)
        {
            this.ForwardMap.Add(key, value);
            this.BackwardsMap.Add(value, key);
        }

        /// <summary>
        /// Get the value associated with a key.
        /// </summary>
        /// <param name="key">The key.</param>
        public TValue GetValue(TKey key)
        {
            return this.ForwardMap[key];
        }

        /// <summary>
        /// Get the key associated with a value.
        /// </summary>
        /// <param name="value">The value.</param>
        public TKey GetKey(TValue value)
        {
            return this.BackwardsMap[value];
        }
    }
}
