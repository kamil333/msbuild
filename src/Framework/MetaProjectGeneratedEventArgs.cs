// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
#if NETFRAMEWORK
	using Directory = Microsoft.Internal.IO.Directory;
	using DirectoryInfo = Microsoft.Internal.IO.DirectoryInfo;
	using File = Microsoft.Internal.IO.File;
	using FileInfo = Microsoft.Internal.IO.FileInfo;
	using Path = Microsoft.Internal.IO.Path;
	using EnumerationOptions = Microsoft.Internal.IO.EnumerationOptions;
	using SearchOption = Microsoft.Internal.IO.SearchOption;
	using FileSystemInfo = Microsoft.Internal.IO.FileSystemInfo;
#endif

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Arguments for the metaproject generated event.
    /// </summary>
    [Serializable]
    public class MetaprojectGeneratedEventArgs : BuildMessageEventArgs
    {
        /// <summary>
        /// Raw xml representing the metaproject.
        /// </summary>
        public string metaprojectXml;

        /// <summary>
        /// Initializes a new instance of the MetaprojectGeneratedEventArgs class.
        /// </summary>
        public MetaprojectGeneratedEventArgs(string metaprojectXml, string metaprojectPath, string message)
            : base(message, null, null, MessageImportance.Low, DateTime.UtcNow, metaprojectPath)
        {
            this.metaprojectXml = metaprojectXml;
            this.ProjectFile = metaprojectPath;
        }
    }
}
