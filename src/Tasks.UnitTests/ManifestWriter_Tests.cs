// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// These functions are now guarded with a linkDemand and requires the caller to be signed with a 
// ms pkt.  The test harness does not appear to be signed.
#if never
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
using System.Reflection;

using Microsoft.Build.Framework;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Microsoft.Build.Tasks.Deployment.ManifestUtilities;


namespace Microsoft.Build.UnitTests
{
    [TestFixture]
    sealed public class ManifestWriter_Tests
    {
        /// <summary>
        /// Attempts to write an AssemblyManifest to a temporary file.
        /// </summary>
        [Test]
        public void BasicWriteAssemblyManifestToPath()
        {
            Manifest m = new AssemblyManifest();
            string file = FileUtilities.GetTemporaryFile();
            ManifestWriter.WriteManifest(m, file);
            File.Delete(file);
        }
    }
}
#endif


