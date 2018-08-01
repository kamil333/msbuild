// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;

namespace Microsoft.Build.Shared
{
    internal static class SpanExtensions
    {
        public static int IndexOf(this ReadOnlySpan<char> aSpan, char aChar, int startIndex)
        {
            var indexInSlice = aSpan.Slice(startIndex).IndexOf(aChar);

            if (indexInSlice == -1)
            {
                return -1;
            }

            return startIndex + indexInSlice;
        }

        public static string ExpensiveConvertToString(this ReadOnlySpan<char> aSpan)
        {
            return aSpan.ToString();
        }
    }
}
