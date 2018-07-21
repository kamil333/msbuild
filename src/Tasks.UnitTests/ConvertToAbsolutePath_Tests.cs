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

using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Xunit;

namespace Microsoft.Build.UnitTests
{
    sealed public class ConvertToAbsolutePath_Tests
    {
        /// <summary>
        /// Passing in a relative path (expecting an absolute back)
        /// </summary>
        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        [Trait("Category", "mono-osx-failing")]
        public void RelativePath()
        {
            string fileName = ObjectModelHelpers.CreateFileInTempProjectDirectory("file.temp", "foo");
            FileInfo testFile = new FileInfo(fileName);

            ConvertToAbsolutePath t = new ConvertToAbsolutePath();
            t.BuildEngine = new MockEngine();

            string currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(ObjectModelHelpers.TempProjectDir);
                t.Paths = new ITaskItem[] { new TaskItem(@"file.temp") };
                Assert.True(t.Execute()); // "success"
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }

            Assert.Equal(1, t.AbsolutePaths.Length);
            Assert.Equal(testFile.FullName, t.AbsolutePaths[0].ItemSpec);

            ObjectModelHelpers.DeleteTempProjectDirectory();
        }

        /// <summary>
        /// Passing in a relative path (expecting an absolute back)
        /// </summary>
        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        [Trait("Category", "mono-osx-failing")]
        public void RelativePathWithEscaping()
        {
            string fileName = ObjectModelHelpers.CreateFileInTempProjectDirectory("file%3A.temp", "foo");
            FileInfo testFile = new FileInfo(fileName);

            ConvertToAbsolutePath t = new ConvertToAbsolutePath();
            t.BuildEngine = new MockEngine();

            string currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(ObjectModelHelpers.TempProjectDir);
                t.Paths = new ITaskItem[] { new TaskItem(@"file%253A.temp") };
                Assert.True(t.Execute()); // "success"
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }

            Assert.Equal(1, t.AbsolutePaths.Length);
            Assert.Equal(testFile.FullName, t.AbsolutePaths[0].ItemSpec);

            ObjectModelHelpers.DeleteTempProjectDirectory();
        }

        /// <summary>
        /// Passing in a absolute path (expecting an absolute back)
        /// </summary>
        [Fact]
        public void AbsolutePath()
        {
            string fileName = ObjectModelHelpers.CreateFileInTempProjectDirectory("file.temp", "foo");
            FileInfo testFile = new FileInfo(fileName);

            ConvertToAbsolutePath t = new ConvertToAbsolutePath();
            t.BuildEngine = new MockEngine();

            string currentDirectory = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(ObjectModelHelpers.TempProjectDir);
                t.Paths = new ITaskItem[] { new TaskItem(fileName) };
                Assert.True(t.Execute()); // "success"
            }
            finally
            {
                Directory.SetCurrentDirectory(currentDirectory);
            }

            Assert.Equal(1, t.AbsolutePaths.Length);
            Assert.Equal(testFile.FullName, t.AbsolutePaths[0].ItemSpec);

            ObjectModelHelpers.DeleteTempProjectDirectory();
        }

        /// <summary>
        /// Passing in a relative path that doesn't exist (expecting success)
        /// </summary>
        [Fact]
        public void FakeFile()
        {
            ConvertToAbsolutePath t = new ConvertToAbsolutePath();
            t.BuildEngine = new MockEngine();

            t.Paths = new ITaskItem[] { new TaskItem("RandomFileThatDoesntExist.txt") };

            Assert.True(t.Execute()); // "success"

            Assert.Equal(1, t.AbsolutePaths.Length);
        }
    }
}
