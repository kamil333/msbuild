// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace Microsoft.Build.Utilities.FileSystem
{
    /// <summary>
    /// Abstraction over the file tree.
    /// </summary>
    public interface IFileStore
    {
        /// <summary>
        /// </summary>
        /// <param name="path">
        ///     - absolute path
        ///     - no relative directory segments
        ///     - no trailing slash
        ///     - directory separator is Path.DirectorySeparatorChar
        ///     - consecutive slashes are collapsed to Path.DirectorySeparatorChar
        /// </param>
        /// <param name="fileNode">
        ///     If a node is returned, then the tree it defines is closed under subdirectory enumeration.
        /// </param>
        /// <returns></returns>
        NodeSearchResult TryGetNode(string path, out IFileNode fileNode);
    }

    /// <summary>
    /// 
    /// </summary>
    public enum NodeSearchResult
    {
        /// <summary>
        /// 
        /// </summary>
        Exists,
        /// <summary>
        /// 
        /// </summary>
        DoesNotExist,
        /// <summary>
        /// 
        /// </summary>
        Unknown
    }
}
