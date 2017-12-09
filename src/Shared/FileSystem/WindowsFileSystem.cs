// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Build.Shared.FileSystem
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
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.File, searchPattern, searchOption);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return EnumerateFileOrDirectories(path, FileArtifactType.Directory, searchPattern, searchOption);
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

            WindowsNative.Win32FindData findResult;
            using (var findHandle = WindowsNative.FindFirstFileW(path.TrimEnd('\\'), out findResult))
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
            string searchPattern,
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

        struct CachePathEntry
        {
            private string _fullPath;

            public CachePathEntry(string fileName, string directory)
            {
                FileName = fileName;
                Directory = directory;

                _fullPath = null;
            }

            public string FileName { get; }
            public string Directory { get; }

            public string FullPath
            {
                get
                {
                    if (_fullPath == null)
                    {
                        _fullPath = Path.Combine(Directory, FileName);
                    }

                    return _fullPath;
                }
            }
        }

        struct CacheEntry
        {
            public EnumerationResult EnumerationResult { get; }
            public List<CachePathEntry> Directories { get; }
            public List<CachePathEntry> Files { get; }

            private static List<CachePathEntry> _empty = new List<CachePathEntry>();

            public CacheEntry(EnumerationResult enumerationResult, List<CachePathEntry> directories, List<CachePathEntry> files)
            {
                EnumerationResult = enumerationResult;
                Directories = directories ?? _empty;
                Files = files ?? _empty;
            }
        }

        struct EnumerationResult
        {
            public bool Succeeded { get; }
            public WindowsNative.EnumerateDirectoryResult EnumerateDirectoryResult { get; }

            public EnumerationResult(bool succeeded, WindowsNative.EnumerateDirectoryResult enumerateDirectoryResult)
            {
                Succeeded = succeeded;
                EnumerateDirectoryResult = enumerateDirectoryResult;
            }
        }

        private static ConcurrentDictionary<string, CacheEntry> enumerationCache = new ConcurrentDictionary<string, CacheEntry>();

        private static WindowsNative.EnumerateDirectoryResult CustomEnumerateDirectoryEntries(
            string directoryPath,
            FileArtifactType fileArtifactType,
            string pattern,
            SearchOption searchOption,
            ICollection<string> result)
        {
            var cacheEntry = enumerationCache.GetOrAdd(
                directoryPath,
                directory =>
                {
                    List<CachePathEntry> files = null;
                    List<CachePathEntry> directories = null;

                    var enumerationResult = EnumerateSingleDirectory(directoryPath, out directories, out files);

                    return new CacheEntry(enumerationResult, directories, files);
                });

            if (!cacheEntry.EnumerationResult.Succeeded)
            {
                return cacheEntry.EnumerationResult.EnumerateDirectoryResult;
            }

            foreach (var fileEntry in cacheEntry.Files)
            {
                if (fileArtifactType == FileArtifactType.FileOrDirectory ||
                    fileArtifactType == FileArtifactType.File &&
                    (pattern == null || pattern == "*" || WindowsNative.PathMatchSpecExW(fileEntry.FileName, pattern, WindowsNative.DwFlags.PmsfNormal) == WindowsNative.ErrorSuccess))
                {
                    result.Add(fileEntry.FullPath);
                }
            }

            foreach (var directoryEntry in cacheEntry.Directories)
            {
                if (searchOption == SearchOption.AllDirectories)
                {
                    var recurs = CustomEnumerateDirectoryEntries(
                        directoryEntry.FullPath,
                        fileArtifactType,
                        pattern,
                        searchOption,
                        result);

                    if (!recurs.Succeeded)
                    {
                        return recurs;
                    }
                }

                if (fileArtifactType == FileArtifactType.FileOrDirectory ||
                    fileArtifactType == FileArtifactType.Directory &&
                    (pattern == null || pattern == "*" || WindowsNative.PathMatchSpecExW(directoryEntry.FileName, pattern, WindowsNative.DwFlags.PmsfNormal) == WindowsNative.ErrorSuccess))
                {
                    result.Add(directoryEntry.FullPath);
                }
            }

            return cacheEntry.EnumerationResult.EnumerateDirectoryResult;
        }

        private static EnumerationResult EnumerateSingleDirectory(
            string directoryPath,
            out List<CachePathEntry> directories,
            out List<CachePathEntry> files)
        {
            directories = null;
            files = null;

            var searchDirectoryPath = Path.Combine(directoryPath.TrimEnd('\\'), "*");

            WindowsNative.Win32FindData findResult;
            using (var findHandle = WindowsNative.FindFirstFileW(searchDirectoryPath, out findResult))
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

                    return new EnumerationResult(
                        false,
                        new WindowsNative.EnumerateDirectoryResult(directoryPath, findHandleOpenStatus, hr));
                }

                while (true)
                {
                    var isDirectory = (findResult.DwFileAttributes & FileAttributes.Directory) != 0;

                    // There will be entries for the current and parent directories. Ignore those.
                    if (!isDirectory)
                    {
                        if (files == null)
                        {
                            files = new List<CachePathEntry>();
                        }

                        files.Add(
                            new CachePathEntry(
                                findResult.CFileName,
                                directoryPath));

                        //if (pattern == null || WindowsNative.PathMatchSpecW(findResult.CFileName, pattern))
                        //{
                        //    if (fileArtifactType == FileArtifactType.FileOrDirectory || !(fileArtifactType == FileArtifactType.Directory ^ isDirectory))
                        //    {
                        //        result.Add(Path.Combine(directoryPath, findResult.CFileName));
                        //    }
                        //}
                    }

                    if (isDirectory && findResult.CFileName != "." && findResult.CFileName != "..")
                    {
                        if (directories == null)
                            directories = new List<CachePathEntry>();

                        directories.Add(
                            new CachePathEntry(
                                findResult.CFileName,
                                directoryPath));
                    }

                    if (!WindowsNative.FindNextFileW(findHandle, out findResult))
                    {
                        var hr = Marshal.GetLastWin32Error();
                        if (hr == WindowsNative.ErrorNoMoreFiles)
                            return new EnumerationResult(
                                true,
                                new WindowsNative.EnumerateDirectoryResult(
                                    directoryPath,
                                    WindowsNative.EnumerateDirectoryStatus.Success,
                                    hr));

                        Debug.Assert(hr != WindowsNative.ErrorSuccess);
                        return new EnumerationResult(
                            false,
                            new WindowsNative.EnumerateDirectoryResult(
                                directoryPath,
                                WindowsNative.EnumerateDirectoryStatus.UnknownError,
                                hr));
                    }
                }
            }
        }

        private static string NormalizePathToWindowsStyle(string path)
        {
            // We make sure all paths are under max path, in some cases
            // the native functions used are slightly more resilient to
            // max path issues, but we want to mimic the managed implementation
            // at this regard
            if (path?.Length > WindowsNative.MaxPath)
            {
                throw new PathTooLongException(
                    $"The path '${path}' exceeds the length limit of '${WindowsNative.MaxPath}' characters.");
            }

            return path?.Replace("/", "\\");
        }
    }
}
