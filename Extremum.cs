// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Bnncmd
{
    public class Extremum(long time, decimal value)
    {
        public long Time { get; set; } = time;

        public decimal Value { get; set; } = value;

        public override string ToString()
        {
            return $"{BnnUtils.UnitTimeToDateTime(Time)} {Value}";
        }
    }
}
