// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.


using System.Numerics;
using System.Runtime.CompilerServices;
using DbSpace;

namespace Bnncmd.Strategy
{
    internal class RedCorrectionParams : EmaDealParams
    {
        public decimal ShortLevel { get; set; }

        public decimal StopLossLevel { get; set; }

        public RedCorrectionParams(decimal shortLevel, decimal stopLossLevel) : base(0, 1, 0, 0)
        {
            ShortLevel = shortLevel;
            StopLossLevel = stopLossLevel;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t\t{StopLossCount}\t\t{MaxDealInterval}";
            var conditions = $"sl\t{ShortLevel}\tst\t{StopLossLevel}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"sl\t{ShortLevel}";
        }
    }


    internal class RedCorrection : BaseStrategy
    {
        #region VariablesAndConstructor

        private decimal _shortLevel = 0.236M; // consider 0.618M
        //private decimal _shortLevel = 0.618M;
        // private decimal _stopLossLevel = 2.618M;
        // private decimal _stopLossLevel = 0.9M;
        private decimal _stopLossLevel = 0.0M;
        private decimal _firstKlineHigh = 0;
        private long _enterLongTime;

        public RedCorrection(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()} - {_shortLevel} - TF{AccountManager.Timeframe}", false); //  {SymbolName} {_emaLength}
            _leftKlines = 7;
            _rightKlines = _leftKlines;
            _dealParams = new ThinKlineParams(0, 0, 0, _rightKlines);
            _isLimit = true;
        }

        public override string GetName() { return $"Red Correction - {SymbolName}"; }
        #endregion

        #region BackTest
        private void InitBacktest(List<Kline>? klines)
        {
            CurrencyAmount = 0;
            _manager.CheckBalance();
            // _lastStopMinTime = -1;
            // _lastMaximum = new Extremum(0, -1);
            // _lastMinimum = new Extremum(0, -1);
            //_currentExtremum = 0;
            // _backtestInited = true;
        }


        private void CheckLong(Kline lastKline)
        {
            if (lastKline.ClosePrice > lastKline.OpenPrice) return;
            _longPrice = (decimal)lastKline.ClosePrice;
            _firstKlineHigh = lastKline.HighPrice;
            _shortPrice = _shortLevel * (_firstKlineHigh - lastKline.LowPrice) + lastKline.LowPrice;
            _stopLossPrice = _longPrice * _stopLossLevel; //  lastKline.LowPrice - _stopLossLevel * (_firstKlineHigh - lastKline.LowPrice);

            if (_shortPrice > 1.0005M * _longPrice) EnterLong(lastKline);
        }


        private void CheckShort(Kline lastKline)
        {
            if (lastKline.HighPrice > _shortPrice) EnterShort(lastKline, false);
            else
            {
                var dealDaysSpan = (lastKline.OpenTime - _enterLongTime) / 1000 / 60 / 60 / 24;
                if (dealDaysSpan > _dealParams.MaxDealInterval) _dealParams.MaxDealInterval = dealDaysSpan;

                if ((_stopLossLevel > 0) && (lastKline.LowPrice < _stopLossPrice))
                {
                    EnterShort(lastKline, true);
                    return;
                }

                if (lastKline.ClosePrice < lastKline.OpenPrice)
                {
                    var newShortPrice = _shortLevel * (_firstKlineHigh - lastKline.LowPrice) + lastKline.LowPrice;
                    if ((_stopLossLevel == 0) && (newShortPrice < _shortPrice)) _shortPrice = newShortPrice;
                    if (AccountManager.LogLevel > 1) Log($"short price changed: {_shortPrice}"); // if (AccountManager.LogLevel > 3) 
                }
            }
        }


        private void ProcessBacktestKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;
            CurrPrice = (decimal)lastKline.ClosePrice;

            // check 
            var extremums = GetExtremums(klines);
            var penultMinimum = extremums.Item1;
            var penultMaximum = extremums.Item2;
            var lastMinimum = extremums.Item3;
            var lastMaximum = extremums.Item4;

            // if (!IsLong && (lastMinimum.Value < penultMinimum.Value)) return; // eth hours ok
            // if (!IsLong && (lastMaximum.Value < penultMaximum.Value)) return; // xrp ok
            // if (!IsLong && (lastMaximum.Value > penultMaximum.Value) && (lastMinimum.Value > penultMinimum.Value)) return;
            if (!IsDealEntered && ((lastMaximum.Value < penultMaximum.Value) || (lastMinimum.Value < penultMinimum.Value))) return; // eth 


            if (AccountManager.LogLevel > 9) Log($"{lastKline}");
            if (IsDealEntered) CheckShort(lastKline);
            else CheckLong(lastKline);
        }


        private void EnterLong(Kline kline)
        {
            Log($"enter long * {kline}; LP: {_longPrice}; SP: {_shortPrice}"); // ; rsi {_rsi:0.###}
            IsDealEntered = true;
            _enterLongTime = kline.OpenTime;
        }


        private void EnterShort(Kline kline, bool isStop)
        {
            Log($"{(isStop ? "stoploss :( " : "enter short :)")} {kline}"); // ; rsi {_rsi:0.###} *
            IsDealEntered = false;

            var balance = (decimal)_manager.Statistics.TotalProfit;
            var currPrice = isStop ? _stopLossPrice : _shortPrice;
            var newBalance = currPrice / _longPrice * balance * (1 - Slippage) * (1 - Slippage);
            Log($"{currPrice:0.###} / {_longPrice} * {balance:0.###} * {1 - Slippage}^2 = {newBalance:0.###}");
            _dealParams.TotalProfit = (double)newBalance;
            _manager.Statistics.DealCount++;
            _dealParams.DealCount = _manager.Statistics.DealCount;
            if (isStop) _dealParams.StopLossCount++;
            _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            Log(string.Empty);
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
            /*var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (RedCorrectionParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.ShortLevel}, {dp.StopLossLevel}, {AccountManager.Timeframe}, {dp.DealCount}, {dp.StopLossCount}, {dp.MaxDealInterval}, 4, {dp.TotalProfit}, 'c'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);*/
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            decimal[] shortLevels = [0.236M, 0.382M, 0.5M, 0.618M, 0.786M, 1, 1.272M];
            decimal[] stopLevels = [-1, 0, 0.95M, 0.9M, 0.8M, 0.7M, 0.6M]; // 

            var deals = new List<BaseDealParams>();
            long paramsSetCount = shortLevels.Length * stopLevels.Length; //  * shortAngles.Length
            long counter = 0;

            foreach (var st in stopLevels)
            {
                _stopLossLevel = st;
                foreach (var sl in shortLevels)
                {
                    _shortLevel = sl;
                    var tkp = new RedCorrectionParams(_shortLevel, _stopLossLevel);
                    _dealParams = tkp;
                    _order = null;
                    InitBacktest(null);
                    _manager.BackTest(); // klines
                    deals.Add(tkp);

                    counter++;
                    BnnUtils.ClearCurrentConsoleLine();
                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                }
            }

            return deals;
        }
        #endregion
    }
}