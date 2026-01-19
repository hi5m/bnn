// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Bnncmd
{
    internal class Kline
    {
        public long OpenTime { get; set; }

        public decimal OpenPrice { get; set; }

        public decimal HighPrice { get; set; }

        public decimal LowPrice { get; set; }

        public decimal ClosePrice { get; set; }

        public override string ToString()
        {
            return $"{BnnUtils.FormatUnixTime(OpenTime)} [ {OpenPrice:#0.0000}  {HighPrice:#0.0000}  {LowPrice:#0.0000}  {ClosePrice:#0.0000} ]";
        }
    }
}
