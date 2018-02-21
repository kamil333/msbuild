// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Microsoft.Build.Utilities.FileSystem
{
    /// <summary>
    /// Abstraction over a file system entry in <see cref="IFileStore"/>
    /// </summary>
    public interface IFileNode
    {
        /// <summary>
        /// Whether the node is a directory
        /// </summary>
        bool IsDirectory { get; }

        /// <summary>
        /// File name, including extensions, if any.
        /// </summary>
        string FileName { get; }

        /// <summary>
        /// Full path in file system.
        /// </summary>
        string FullPath { get; }

        /// <summary>
        /// Path between the root <see cref="IFileNode"/> of <see cref="IFileStore"/> and this.
        /// </summary>
        string RootPath { get; }

        /// <summary>
        /// Parent directory
        /// </summary>
        IFileNode Parent { get; }

        /// <summary>
        /// Children.
        /// </summary>
        IReadOnlyCollection<IFileNode> Children { get; }
    }
}
