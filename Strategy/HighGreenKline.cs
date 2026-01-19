// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bnncmd;
using Bnncmd.Strategy;
using DbSpace;

namespace Bnncmd.Strategy
{
    internal class HighGreenKlineParams : DealParams
    {
        public int RsiLength { get; set; }
        public int RsiBuyLevel { get; set; }
        public int RsiSellLevel { get; set; }
        public decimal LongKlineHigh { get; set; }

        public HighGreenKlineParams(int rsiLength, int rsiBuyLevel, int rsiSellLevel, decimal longKlineHigh)
        {
            RsiLength = rsiLength;
            RsiBuyLevel = rsiBuyLevel;
            RsiSellLevel = rsiSellLevel;
            LongKlineHigh = longKlineHigh;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 100;
            ConfirmExtrPart = 0.00000;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t{StopLossCount}"; // {MaxDealInterval}\t
            var conditions = $"rln\t{RsiLength}\trbl\t{RsiBuyLevel}\trsl\t{RsiSellLevel}\tlkh\t{LongKlineHigh}";
            // var conditions = $"rln\t{RsiLength}\trlv\t{RsiLevel}\tshl\t{SellFibLevel:0.###}\tstl\t{StopFibLevel:0.###}\tmmk\t{MinMaxKlines:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"rln {RsiLength}   rbl {RsiBuyLevel:0.##}   rsl {RsiSellLevel:0.##}   lkh {LongKlineHigh}"; //    rlv {RsiLevel}   shl {SellFibLevel:0.##}   stl {StopFibLevel}   mmk {MinMaxKlines}
        }
    }

    internal class HighGreenKline : EMA
    {
        #region VariablesAndConstructor
        // private RsiDumpState _state = RsiDumpState.WaitForRsiDump;

        //private readonly int _highTimeframe = 15;
        //private readonly int _lowTimeframe = 1;
        //private readonly int _highTimeframe = 60;
        //private readonly int _lowTimeframe = 5;
        // private readonly int _highTimeframe = 24 * 60;
        //private readonly int _lowTimeframe = 60;

        private int _rsiBuyLevel = 50;
        private int _rsiSellLevel = 50;
        private decimal _longKlineHigh = 0.001M;
        // private decimal _totalPercent = 0.0M;

        /*private readonly int _rsiDumpLength = 14;
        private int _buyRsiLevel = 40;
        private decimal _stopFibLevel = -0.27M; // 0.01M; // 
        private int _minMaxFrames = 2;

        private readonly decimal _lowLongFibLevel = 0M; // = 1; // 

        private DateTime _longTime;
        private readonly decimal _originalSlippage;

        private readonly bool _extremeStop = false;
        private readonly bool _backLong = false;
        private bool _isLowBull;*/

        public HighGreenKline(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()}", false);
            _isLimit = true;
            /* _minutesInTimeframe = _highTimeframe;
            _rsiLength = _rsiDumpLength;
            _dealParams = new RsiDumpParams(_rsiLength, _buyRsiLevel, _sellFibLevel, _stopFibLevel, _minMaxFrames);
            _originalSlippage = Slippage; //  (decimal)Math.Pow((double)Slippage, 0.5);*/
            // Slippage = 0;
            // Log($"_stopSlippage: {_stopSlippage}");
        }

        public override string GetName() { return $"High Green Kline - {SymbolName} - TF{AccountManager.Timeframe}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline> klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            CalcRSIFromArchive(klines);
            /*_state = RsiDumpState.WaitForRsiDump;
            _leftKlines = _minMaxFrames;
            _rightKlines = _minMaxFrames;*/
            _backtestInited = true;
        }

        public override void InitBacktestLong(int klineIndex)
        {
            base.InitBacktestLong(klineIndex);
            // _state = RsiDumpState.WaitForRsiDump;
        }

        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            var penultKline = klines[^2];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = lastKline.ClosePrice;

            if (!_backtestInited)
            {
                InitBacktest(klines);
                return;
            }

            var priceChage = penultKline.ClosePrice - penultKline.OpenPrice;
            CalcRSI(priceChage);

            var lastKlineHigh = (lastKline.ClosePrice - lastKline.OpenPrice) / lastKline.OpenPrice;
            if (!IsDealEntered && (_rsi > _rsiBuyLevel) && (lastKlineHigh > _longKlineHigh)) EnterLong(lastKline, lastKline.ClosePrice);
            if (IsDealEntered && (_rsi < _rsiSellLevel)) ExitLong(lastKline, lastKline.ClosePrice);

            /*var penultKlineHigh = (penultKline.ClosePrice - penultKline.OpenPrice) / penultKline.OpenPrice;
            if ((penultKlineHigh > _longKlineHigh) && (_rsi > _buyRsiLevel))
            {
                _totalPercent += lastKlineHigh * 100;
                if (AccountManager.LogLevel > 9) Log($"{penultKline} - {penultKlineHigh * 100:0.###}% => {lastKlineHigh * 100:0.###}% => {_totalPercent:0.###}");
                EnterLong(lastKline);
                EnterShort(lastKline, lastKline.ClosePrice);
            }*/
        }

        private void EnterLong(Kline kline, decimal limitPrice = 0)
        {
            IsDealEntered = true;
            if (limitPrice == 0) limitPrice = kline.OpenPrice;
            _longPrice = limitPrice;// _longAim;
            Log($"EL: {_longPrice:0.###}; RSI{_rsiLength}: {_rsi: 0.###}");
            // _longTime = BnnUtils.UnitTimeToDateTime(kline.OpenTime);
            // _state = RsiDumpState.FindHighTfMin;
            // _highTimeframeMax = 0; //  decimal.MaxValue;
        }


        protected override void ExitLong(Kline kline, decimal limitPrice = 0)
        {
            /* var isShort = kline.HighPrice > _shortAim;
            var slippage = _state == RsiDumpState.ExtremeStopping ? _originalSlippage * _stopAim : 0;
            if (_state == RsiDumpState.ExtremeStopping) limitPrice = kline.ClosePrice - slippage;
            else limitPrice = isShort ? _shortAim : _stopAim; */

            Log($"ES: {limitPrice}; {100 * (limitPrice - _longPrice) / _longPrice:0.##} %; slippage: {Slippage:0.###}");
            base.ExitLong(kline, limitPrice);
            /* _state = RsiDumpState.WaitForRsiDump;
            if (!isShort)
            {
                _dealParams.StopLossCount++;
                _manager.Statistics.StopLossCount = _dealParams.StopLossCount;
            }*/
        }


        public override string GetCurrentInfo()
        {
            return $"GetCurrentInfo";
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return _longPrice; //  double.MaxValue;
            // return 0;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessBacktestKline(klines);
            return (double)_shortPrice; //  1000000.0;
            //return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            /*var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, Param6, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (RsiDumpParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.RsiLength}, {dp.RsiLevel}, {dp.SellFibLevel}, {dp.StopFibLevel}, {dp.MinMaxKlines}, {dp.DealCount}, {_highTimeframe}, 3, {dp.TotalProfit}, 'u'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);*/
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] rsiLengths = [5, 9, 14];
            int[] buyRsiLevels = [40, 50, 55, 60, 65, 70, 80];
            int[] sellRsiLevels = [40, 50, 55, 60, 65, 70, 80];
            // decimal[] longKlineHighs = [0.001M];
            decimal[] longKlineHighs = [0];// [0.001M, 0.005M, 0.01M, 0.013M, 0.015M, 0.017M, 0.019M, 0.023M, 0.03M];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = rsiLengths.Length * buyRsiLevels.Length * sellRsiLevels.Length * longKlineHighs.Length;
            long counter = 0;
            foreach (var rl in rsiLengths)
            {
                _rsiLength = rl;
                foreach (var bl in buyRsiLevels)
                {
                    _rsiBuyLevel = bl;
                    foreach (var sl in sellRsiLevels)
                    {
                        _rsiSellLevel = sl;
                        foreach (var lkh in longKlineHighs)
                        {
                            _longKlineHigh = lkh;
                            var dp = new HighGreenKlineParams(_rsiLength, _rsiBuyLevel, _rsiSellLevel, _longKlineHigh);
                            _dealParams = dp;
                            _order = null;
                            _backtestInited = false;
                            _manager.BackTest();
                            deals.Add(dp);

                            counter++;
                            BnnUtils.ClearCurrentConsoleLine();
                            Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                        }
                    }
                }
            }
            return deals;
        }
        #endregion

        #region RealTimeRoutines
        #endregion
    }
}
