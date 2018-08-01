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

#if NETFRAMEWORK || MONO
        //todo remove when full framework will get the StringBuilder span extensions from .net core
        public static void Append(this StringBuilder builder, ReadOnlySpan<char> span)
        {
            builder.EnsureCapacity(builder.Length + span.Length);

            for (var i = 0; i < span.Length; i++)
            {
                builder.Append(span[i]);
            }
        }
#endif

    }
}
