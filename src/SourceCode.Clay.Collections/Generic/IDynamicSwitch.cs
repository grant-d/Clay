#region License

// Copyright (c) K2 Workflow (SourceCode Technology Holdings Inc.). All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

#endregion

namespace SourceCode.Clay.Collections.Generic
{
    /// <summary>
    /// Interface used for exposing dynamic switch statements.
    /// The members are very similar to those exposed by <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of key used in the switch.</typeparam>
    /// <typeparam name="TValue">The type of value used in the switch.</typeparam>
    /// <seealso cref="Expression.Switch"/>
    public interface IDynamicSwitch<in TKey, TValue>
    {
        #region Properties

        /// <summary>
        /// The number of items in the switch.
        /// </summary>
        int Count { get; }

        #endregion

        #region Indexers

        /// <summary>
        /// Gets the value with the specified key.
        /// </summary>
        /// <param name="key">The key value.</param>
        /// <returns></returns>
        TValue this[TKey key] { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Checks whether the specified key is present in the switch.
        /// </summary>
        /// <param name="key">The key value.</param>
        /// <returns></returns>
        bool ContainsKey(TKey key);

        /// <summary>
        /// Attempts to get the value corresponding to the specified key.
        /// </summary>
        /// <param name="key">The key value.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        bool TryGetValue(TKey key, out TValue value);

        #endregion
    }
}
