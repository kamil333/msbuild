// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
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

namespace Microsoft.Build.Shared.FileSystem
{
    /// <summary>
    /// Legacy implementation for MSBuildTaskHost which is stuck on net20 APIs
    /// </summary>
    internal class MSBuildTaskHostFileSystem : IFileSystem
    {
        private static readonly MSBuildTaskHostFileSystem Instance = new MSBuildTaskHostFileSystem();

        public static MSBuildTaskHostFileSystem Singleton() => Instance;

        public bool DirectoryEntryExists(string path)
        {
            return NativeMethodsShared.FileOrDirectoryExists(path);
        }

        public bool DirectoryExists(string path)
        {
            return NativeMethodsShared.DirectoryExists(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return Directory.GetDirectories(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return Directory.GetFiles(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            ErrorUtilities.VerifyThrow(searchOption == SearchOption.TopDirectoryOnly, $"In net20 {nameof(Directory.GetFileSystemEntries)} does not take a {nameof(SearchOption)} parameter");

            return Directory.GetFileSystemEntries(path, searchPattern);
        }

        public bool FileExists(string path)
        {
            return NativeMethodsShared.FileExists(path);
        }
    }
}
