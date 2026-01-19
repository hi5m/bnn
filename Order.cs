// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Bnncmd.Strategy;

namespace Bnncmd
{
    internal class Order
    {
        public static int DefaultInitialQueue = 100000000;

        public long Id { get; set; }
        public decimal Price { get; set; }
        public decimal StopLossPrice { get; set; }
        public decimal Amount { get; set; }
        public decimal Filled { get; set; } = 0;
        public long DateTime { get; set; }
        public bool IsBuyer { get; set; }
        public bool IsFutures { get; set; } = false;
        public decimal BorderPrice { get; set; } = 0;
        public decimal TotalQuote { get; set; } = 0;
        public double InitialQueue { get; set; } = DefaultInitialQueue;
        public double QueueOvercome { get; set; } = 0;
        public bool IsTriggered { get { return Filled == 1; } }
        public bool IsStopLoss { get { return StopLossPrice > 0; } }

        public override string ToString()
        {
            if (IsFutures) return $"{(IsBuyer ? "LONG" : "SHORT")} {Price:0.#######} x {Amount}";
            else return $"{(IsBuyer ? "BUY" : "SELL")} {Price:0.#######} x {Amount} {(IsStopLoss ? "SL" : "")}";
        }

        public string GetQueueInfo()
        {
            return $"{(IsBuyer ? 'B' : 'S')} {QueueOvercome / Exchanger.Thousands:0.###}/{InitialQueue / Exchanger.Thousands:0.###}{Exchanger.ThousandsDescr}"; // 
        }
    }
}