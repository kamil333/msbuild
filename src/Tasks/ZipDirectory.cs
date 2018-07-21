// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Framework;
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
using System.IO.Compression;

namespace Microsoft.Build.Tasks
{
    public sealed class ZipDirectory : TaskExtension
    {
        /// <summary>
        /// Gets or sets a <see cref="ITaskItem"/> containing the full path to the destination file to create.
        /// </summary>
        [Required]
        public ITaskItem DestinationFile { get; set; }

        /// <summary>
        /// Gets or sets a value indicating if the destination file should be overwritten.
        /// </summary>
        public bool Overwrite { get; set; }

        /// <summary>
        /// Gets or sets a <see cref="ITaskItem"/> containing the full path to the source directory to create a zip archive from.
        /// </summary>
        [Required]
        public ITaskItem SourceDirectory { get; set; }

        public override bool Execute()
        {
            DirectoryInfo sourceDirectory = new DirectoryInfo(SourceDirectory.ItemSpec);

            if (!sourceDirectory.Exists)
            {
                Log.LogErrorFromResources("ZipDirectory.ErrorDirectoryDoesNotExist", sourceDirectory.FullName);
                return false;
            }

            FileInfo destinationFile = new FileInfo(DestinationFile.ItemSpec);

            BuildEngine3.Yield();

            try
            {
                if (destinationFile.Exists)
                {
                    if (!Overwrite)
                    {
                        Log.LogErrorFromResources("ZipDirectory.ErrorFileExists", destinationFile.FullName);

                        return false;
                    }

                    try
                    {
                        File.Delete(destinationFile.FullName);
                    }
                    catch (Exception e)
                    {
                        Log.LogErrorFromResources("ZipDirectory.ErrorFailed", sourceDirectory.FullName, destinationFile.FullName, e.Message);

                        return false;
                    }
                }

                try
                {
                    Log.LogMessageFromResources(MessageImportance.High, "ZipDirectory.Comment", sourceDirectory.FullName, destinationFile.FullName);
                    ZipFile.CreateFromDirectory(sourceDirectory.FullName, destinationFile.FullName);
                }
                catch (Exception e)
                {
                    Log.LogErrorFromResources("ZipDirectory.ErrorFailed", sourceDirectory.FullName, destinationFile.FullName, e.Message);
                }
            }
            finally
            {
                BuildEngine3.Reacquire();
            }

            return !Log.HasLoggedErrors;
        }
    }
}
