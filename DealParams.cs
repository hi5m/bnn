// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace Bnncmd
{
    internal class BaseDealParams
    {
        public static double InitialBalance = 1000;

        public double TotalProfit { get; set; } = InitialBalance;
        public long MaxDealInterval { get; set; } = 0;
        public int DealCount { get; set; } = 0;
        public int StopLossCount { get; set; } = 0;

        public virtual string GetParamsDesciption() { return string.Empty; }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}"; //  / 1000 / 60 / 60 / 24
            return results;
        }
    }


    internal class DealParams : BaseDealParams
    {
        public DealParams(double dealProfit, double priceThreshold, double stopLossPerc, double confirmExtrPart)
        {
            DealProfit = dealProfit;
            PriceThreshold = priceThreshold;
            StopLossPerc = stopLossPerc;
            ConfirmExtrPart = confirmExtrPart;
        }

        public DealParams() : this(0.011, 0, 0.019, 0) { }

        // public static double InitialBalance = 1000;

        public double DealProfit { get; set; }
        public double PriceThreshold { get; set; }
        public double StopLossPerc { get; set; }
        public double ConfirmExtrPart { get; set; }

        /* public double TotalProfit { get; set; } = InitialBalance;
        public long MaxDealInterval { get; set; } = 0;
        public int DealCount { get; set; } = 0;
        public int StopLossCount { get; set; } = 0; */

        public int ThresholdMultiplier { get; set; } = 0; // ???
        public double ThresholdOffet { get; set; } = 0; // ???

        public void CopyFrom(DealParams dealParams)
        {
            DealProfit = dealParams.DealProfit;
            PriceThreshold = dealParams.PriceThreshold;
            StopLossPerc = dealParams.StopLossPerc;
            ConfirmExtrPart = dealParams.ConfirmExtrPart;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}"; //  / 1000 / 60 / 60 / 24
            var conditions = $"th\t{PriceThreshold}\tsl\t{StopLossPerc}\tce\t{ConfirmExtrPart:0.#####}\tdp\t{DealProfit} --- {ThresholdOffet}"; //
            return results + "\t" + conditions;
        }


        public override string GetParamsDesciption()
        {
            return $"th {PriceThreshold}   sl {StopLossPerc}   ce {ConfirmExtrPart:0.#####}   dp {DealProfit}";
        }
    }


    internal class DummyParams : DealParams
    {
        public override string ToString()
        {
            return "-";
        }


        public override string GetParamsDesciption()
        {
            return "-";
        }
    }
}
