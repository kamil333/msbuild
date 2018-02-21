﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Build.Shared.FileSystem
{
    /// <summary>
    /// Factory for <see cref="IFileSystemAbstraction"/>
    /// </summary>
    public static class FileSystemFactory
    {

        /// <nodoc/>
        public static IFileSystemAbstraction GetFileSystem(IFileStore fileStore = null)
        {
            var fileSystem = GetPlatformSpecificFileSystem();

            return fileStore == null
                ? fileSystem
                : new StoreBasedFileSystem(fileStore, fileSystem);
        }

        private static IFileSystemAbstraction GetPlatformSpecificFileSystem()
        {
            // The windows-specific file system is only available on Windows Vista or higher
            if (IsWinVistaOrHigher())
            {
                return WindowsFileSystem.Singleton();
            }

            // Otherwise we fall back into the standard managed file system API
            return ManagedFileSystem.Singleton();
        }

        private static bool IsWinVistaOrHigher()
        {
            var os = Environment.OSVersion;
            return os.Platform == PlatformID.Win32NT && os.Version.Major >= 6;
        }
    }
}
