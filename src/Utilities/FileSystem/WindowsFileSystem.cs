﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Build.Utilities.FileSystem
{
    /// <summary>
    /// The type of file artifact to search for
    /// </summary>
    internal enum FileArtifactType : byte
    {
        /// <nodoc/>
        File,
        /// <nodoc/>
        Directory,
        /// <nodoc/>
        FileOrDirectory
    }

    /// <summary>
    /// Windows-specific implementation of file system operations using Windows native invocations
    /// </summary>
    public class WindowsFileSystem : IFileSystemAbstraction
    {
        private static readonly WindowsFileSystem Instance = new WindowsFileSystem();

        /// <nodoc/>
        public static WindowsFileSystem Singleton() => WindowsFileSystem.Instance;

        private WindowsFileSystem()
        { }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFiles(string path)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.File);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.File, searchPattern);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.File, searchPattern, searchOption);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateDirectories(string path)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.Directory);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.Directory, searchPattern);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.Directory, searchPattern, searchOption);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.FileOrDirectory);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.FileOrDirectory, searchPattern);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.FileOrDirectory, searchPattern, searchOption);
        }

        /// <inheritdoc/>
        public bool DirectoryExists(string path)
        {
            return FileOrDirectoryExists(FileArtifactType.Directory, path);
        }

        /// <inheritdoc/>
        public bool FileExists(string path)
        {
            return FileOrDirectoryExists(FileArtifactType.File, path);
        }

        /// <inheritdoc/>
        public bool DirectoryEntryExists(string path)
        {
            return FileOrDirectoryExists(FileArtifactType.FileOrDirectory, path);
        }

        private static bool FileOrDirectoryExists(FileArtifactType fileArtifactType, string path)
        {
            // The path gets normalized so we always use backslashes
            path = NormalizePathToWindowsStyle(path);

            using (var findHandle = WindowsNative.FindFirstFileW(path.TrimEnd('\\'), out WindowsNative.Win32FindData findResult))
            {
                // Any error is interpreted as a file not found. This matches the managed Directory.Exists and File.Exists behavior
                if (findHandle.IsInvalid)
                {
                    return false;
                }

                if (fileArtifactType == FileArtifactType.FileOrDirectory)
                {
                    return true;
                }

                var isDirectory = (findResult.DwFileAttributes & FileAttributes.Directory) != 0;

                return !(fileArtifactType == FileArtifactType.Directory ^ isDirectory);
            }
        }

        private static IEnumerable<string> EnumerateFileOrDirectories(
            string directoryPath,
            FileArtifactType fileArtifactType,
            string searchPattern = null,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            var enumeration = new List<string>();

            // The search pattern and path gets normalized so we always use backslashes
            searchPattern = NormalizePathToWindowsStyle(searchPattern);
            directoryPath = NormalizePathToWindowsStyle(directoryPath);

            var result = CustomEnumerateDirectoryEntries(
                directoryPath,
                fileArtifactType,
                searchPattern,
                searchOption,
                enumeration);

            // If the result indicates that the enumeration succeeded or the directory does not exist, then the result is considered success.
            // In particular, if the globed directory does not exist, then we want to return the empty file, and track for the anti-dependency.
            if (
                !(result.Status == WindowsNative.EnumerateDirectoryStatus.Success ||
                  result.Status == WindowsNative.EnumerateDirectoryStatus.SearchDirectoryNotFound))
            {
                throw result.CreateExceptionForError();
            }

            return enumeration;
        }

        private static WindowsNative.EnumerateDirectoryResult CustomEnumerateDirectoryEntries(
            string directoryPath,
            FileArtifactType fileArtifactType,
            string pattern,
            SearchOption searchOption,
            ICollection<string> result)
        {
            var searchDirectoryPath = Path.Combine(directoryPath.TrimEnd('\\'), "*");

            using (var findHandle = WindowsNative.FindFirstFileW(searchDirectoryPath, out WindowsNative.Win32FindData findResult))
            {
                if (findHandle.IsInvalid)
                {
                    int hr = Marshal.GetLastWin32Error();
                    Debug.Assert(hr != WindowsNative.ErrorFileNotFound);

                    WindowsNative.EnumerateDirectoryStatus findHandleOpenStatus;
                    switch (hr)
                    {
                        case WindowsNative.ErrorFileNotFound:
                            findHandleOpenStatus = WindowsNative.EnumerateDirectoryStatus.SearchDirectoryNotFound;
                            break;
                        case WindowsNative.ErrorPathNotFound:
                            findHandleOpenStatus = WindowsNative.EnumerateDirectoryStatus.SearchDirectoryNotFound;
                            break;
                        case WindowsNative.ErrorDirectory:
                            findHandleOpenStatus = WindowsNative.EnumerateDirectoryStatus.CannotEnumerateFile;
                            break;
                        case WindowsNative.ErrorAccessDenied:
                            findHandleOpenStatus = WindowsNative.EnumerateDirectoryStatus.AccessDenied;
                            break;
                        default:
                            findHandleOpenStatus = WindowsNative.EnumerateDirectoryStatus.UnknownError;
                            break;
                    }

                    return new WindowsNative.EnumerateDirectoryResult(directoryPath, findHandleOpenStatus, hr);
                }

                while (true)
                {
                    var isDirectory = (findResult.DwFileAttributes & FileAttributes.Directory) != 0;

                    // There will be entries for the current and parent directories. Ignore those.
                    if (!isDirectory || (findResult.CFileName != "." && findResult.CFileName != ".."))
                    {
                        // Make sure pattern and directory/file filters are honored
                        if (pattern == null || WindowsNative.PathMatchSpecW(findResult.CFileName, pattern))
                        {
                            if (fileArtifactType == FileArtifactType.FileOrDirectory || !(fileArtifactType == FileArtifactType.Directory ^ isDirectory))
                            {
                                result.Add(Path.Combine(directoryPath, findResult.CFileName));
                            }
                        }

                        // Recursively go into subfolders if specified
                        if (searchOption == SearchOption.AllDirectories && isDirectory)
                        {
                            var recurs = CustomEnumerateDirectoryEntries(
                                Path.Combine(directoryPath, findResult.CFileName),
                                fileArtifactType,
                                pattern,
                                searchOption,
                                result);

                            if (!recurs.Succeeded)
                            {
                                return recurs;
                            }
                        }
                    }

                    if (!WindowsNative.FindNextFileW(findHandle, out findResult))
                    {
                        int hr = Marshal.GetLastWin32Error();
                        if (hr == WindowsNative.ErrorNoMoreFiles)
                        {
                            // Graceful completion of enumeration.
                            return new WindowsNative.EnumerateDirectoryResult(
                                directoryPath,
                                WindowsNative.EnumerateDirectoryStatus.Success,
                                hr);
                        }

                        Debug.Assert(hr != WindowsNative.ErrorSuccess);
                        return new WindowsNative.EnumerateDirectoryResult(
                            directoryPath,
                            WindowsNative.EnumerateDirectoryStatus.UnknownError,
                            hr);
                    }
                }
            }
        }

        private static string NormalizePathToWindowsStyle(string path)
        {
            return path?.Replace("/", "\\");
        }
    }
}
