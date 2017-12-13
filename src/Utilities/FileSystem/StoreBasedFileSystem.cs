// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Utilities.FileSystem
{
    /// <summary>
    ///     Implementation of file system operations directly over the dot net managed layer
    /// </summary>
    internal sealed class StoreBasedFileSystem : IFileSystemAbstraction
    {
        private static readonly char OtherSlash = Path.DirectorySeparatorChar == '/' ? '\\' : '/';

        private readonly IFileStore _store;
        private readonly IFileSystemAbstraction _backingFileSystem;

        public StoreBasedFileSystem(IFileStore store, IFileSystemAbstraction backingFileSystem)
        {
            _store = store;
            _backingFileSystem = backingFileSystem;
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return Enumerate(path, searchPattern, searchOption, (p, sp, so, fs) => fs.EnumerateFiles(p, sp, so));
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return Enumerate(path, searchPattern, searchOption, (p, sp, so, fs) => fs.EnumerateDirectories(p, sp, so));
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateFileSystemEntries(
            string path,
            string searchPattern,
            SearchOption searchOption)
        {
            return Enumerate(path, searchPattern, searchOption, (p, sp, so, fs) => fs.EnumerateFileSystemEntries(p, sp, so));
        }

        /// <inheritdoc />
        public bool DirectoryExists(string path)
        {
            return ExistenceCheck(path, (p, fs) => fs.DirectoryExists(p));
        }

        /// <inheritdoc />
        public bool FileExists(string path)
        {
            return ExistenceCheck(path, (p, fs) => fs.FileExists(p));
        }

        /// <inheritdoc />
        public bool DirectoryEntryExists(string path)
        {
            return ExistenceCheck(path, (p, fs) => fs.DirectoryEntryExists(p));
        }

        private bool ExistenceCheck(string path, Func<string, IFileSystemAbstraction, bool> existenceCheck)
        {
            AssertPathNormalized(path);

            var storeResult = _store.TryGetNode(path, out _);

            switch (storeResult)
            {
                case NodeSearchResult.Exists:
                    return true;
                case NodeSearchResult.DoesNotExist:
                    return false;
                case NodeSearchResult.Unknown:
                    return existenceCheck(path, _backingFileSystem);
                default:
                    throw new NotImplementedException();
            }
        }

        private IEnumerable<string> Enumerate(
            string path,
            string searchPattern,
            SearchOption searchOption,
            Func<string, string, SearchOption, IFileSystemAbstraction, IEnumerable<string>> enumerate)
        {
            AssertPathNormalized(path);

            var storeResult = _store.TryGetNode(path, out var node);

            switch (storeResult)
            {
                case NodeSearchResult.Exists:
                    return ChildrenFullPaths(
                        node,
                        searchPattern,
                        searchOption,
                        FileMatcher.FileSystemEntity.FilesAndDirectories);
                case NodeSearchResult.Unknown:
                    return enumerate(path, searchPattern, searchOption, _backingFileSystem);
                case NodeSearchResult.DoesNotExist:
                    throw new DirectoryNotFoundException(path);
                default:
                    throw new NotImplementedException();
            }
        }

        /// <summary>
        ///     Assert input is in form required by <see cref="IFileStore.TryGetNode" />
        /// </summary>
        /// <param name="path"></param>
        private void AssertPathNormalized(string path)
        {
            Debug.Assert(IsPathNormalized(path));
        }

        private static bool IsPathNormalized(string path)
        {
            if (!Path.IsPathRooted(path))
            {
                return false;
            }

            for (var i = 0; i < path.Length - 1; i++)
            {
                if (path[i] == OtherSlash)
                {
                    return false;
                }

                if (path[i] != Path.DirectorySeparatorChar)
                {
                    continue;
                }

                if (path[i + 1] == Path.DirectorySeparatorChar)
                {
                    return false;
                }

                if (i + 2 < path.Length && path[i + 2].IsSlash())
                {
                    if (path[i + 1] == '.')
                    {
                        return false;
                    }
                }

                if (i + 3 < path.Length && path[i + 3].IsSlash())
                {
                    if (path[i + 2] == '.')
                    {
                        return false;
                    }
                }
            }

            if (path[path.Length - 1].IsSlash())
            {
                return false;
            }

            return true;
        }

        private IEnumerable<string> ChildrenFullPaths(
            IFileNode node,
            string searchPattern,
            SearchOption searchOption,
            FileMatcher.FileSystemEntity entityType)
        {
            if (searchOption == SearchOption.AllDirectories)
            {
                throw new NotImplementedException();
            }

            foreach (var child in node.Children)
                if (child.IsDirectory &&
                    (entityType == FileMatcher.FileSystemEntity.FilesAndDirectories ||
                     entityType == FileMatcher.FileSystemEntity.Directories) ||

                    !child.IsDirectory &&
                    (entityType == FileMatcher.FileSystemEntity.FilesAndDirectories ||
                     entityType == FileMatcher.FileSystemEntity.Files))
                {
                    if (FileMatcher.IsMatch(child.FullPath, searchPattern, true))
                    {
                        yield return child.FullPath;
                    }
                }
        }
    }
}
