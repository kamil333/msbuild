// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Win32.SafeHandles;

<<<<<<< HEAD:src/Shared/FileSystem/SafeFileHandle.cs
namespace Microsoft.Build.Shared.FileSystem
=======
namespace Microsoft.Build.Utilities.FileSystem
>>>>>>> 660faf81... fix intellisense:src/Utilities/FileSystem/SafeFileHandle.cs
{
    /// <summary>
    /// Handle for a volume iteration as returned by WindowsNative.FindFirstVolumeW />
    /// </summary>
    internal sealed class SafeFindFileHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        /// Private constructor for the PInvoke marshaller.
        /// </summary>
        private SafeFindFileHandle()
            : base(ownsHandle: true)
        {
        }

        /// <nodoc/>
        protected override bool ReleaseHandle()
        {
            return WindowsNative.FindClose(handle);
        }
    }
}
