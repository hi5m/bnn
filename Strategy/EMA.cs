// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Binance.Spot;
using Binance.Spot.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.X509;

namespace Bnncmd.Strategy
{
    internal class EmaDealParams : DealParams
    {
        public int EmaLength { get; set; }
        public int DiffInterval { get; set; }
        public double LongAngle { get; set; }
        public double ShortAngle { get; set; }

        public EmaDealParams(int emaMinsLength, int diffInterval, double longAngle, double shortAngle)
        {
            EmaLength = emaMinsLength;
            DiffInterval = diffInterval;
            LongAngle = longAngle;
            ShortAngle = shortAngle;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 100;
            ConfirmExtrPart = 0.00000;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}";
            var conditions = $"emi\t{EmaLength}\tla\t{LongAngle:0.###}\tsa\t{ShortAngle:0.###}"; // \tdi\t{DiffInterval}
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"eml {EmaLength}   edi {DiffInterval}   la {LongAngle:0.#####}   sa {ShortAngle}"; // \n\r
        }
    }


    internal class EMA : BaseStrategy
    {
        #region VariablesAndContructor
        private int _emaLength = 15; // 9 --- 15
        protected int _emaDiffInterval = 1;
        protected double _longAngle = 0.011; // 0125
        protected double _shortAngle = 0; // = 0125
        protected List<Kline>? _rangeKlines;
        protected decimal _alpha;
        protected bool _isFutures = false;

        public int EmaLength
        {
            get { return _emaLength; }
            set
            {
                _emaLength = value;
                _alpha = 2.0M / (_emaLength + 1);
            }
        }

        protected decimal _currentHighEMA = 0;
        protected decimal _currentLowEMA = 0;
        protected decimal _currentCloseEMA = 0;
        private decimal _previousHighEMA = 0;
        private decimal _previousLowEMA = 0;
        protected decimal _previousCloseEMA = 0;

        protected double _trendHighAngle;
        protected double _trendLowAngle;
        protected double _trendCloseAngle;

        protected int _lastCheckSecond = 60;

        protected bool _byClosePrice = true;
        private readonly bool _isCatchUp = true;

        // RSI variables
        protected decimal _positiveAvg = 0;
        protected decimal _negativeAvg = 0;
        protected decimal _rsi = 0;
        protected int _rsiLength = 14;
        protected int _minutesInTimeframe = 60;


        public EMA(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            _emaLength = AccountManager.Config.GetValue<int>("Strategies:MovingAverage:Length");
            _longAngle = AccountManager.Config.GetValue<double>("Strategies:MovingAverage:LongAngle");
            _shortAngle = AccountManager.Config.GetValue<double>("Strategies:MovingAverage:ShortAngle");

            _isLimit = true;
            _alpha = 2.0M / (_emaLength + 1);

            var newDp = new EmaDealParams(_emaLength, _emaDiffInterval, _longAngle, _shortAngle);
            newDp.CopyFrom(_dealParams);
            _dealParams = newDp;
        }


        public override string GetName() { return "Exponential Moving Average"; }
        #endregion

        protected void CalcRSI(decimal priceChage) // , Kline lastKline
        {
            var positiveChange = priceChage > 0 ? priceChage : 0;
            var negativeChange = priceChage < 0 ? -priceChage : 0;
            var previousRsi = _rsi;
            _positiveAvg = ((_rsiLength - 1) * _positiveAvg + positiveChange) / _rsiLength;
            _negativeAvg = ((_rsiLength - 1) * _negativeAvg + negativeChange) / _rsiLength;
            _rsi = 100 - 100 / (1 + (_positiveAvg / _negativeAvg));
        }


        protected void CalcRSIFromArchive(List<Kline> klines)
        {
            var hourKlines = GroupKlines(klines, 60 * _minutesInTimeframe);
            var firstInd = hourKlines.Count - _rsiLength;
            var lastInd = hourKlines.Count;
            var gainsSum = 0.0M;
            var lossesSum = 0.0M;
            for (var i = firstInd; i < lastInd; i++)
            {
                var priceChange = hourKlines[i].ClosePrice - hourKlines[i].OpenPrice;
                if (priceChange > 0) gainsSum += priceChange;
                else lossesSum -= priceChange;
            }

            _positiveAvg = gainsSum / _rsiLength;
            _negativeAvg = lossesSum / _rsiLength;
            _rsi = 100 - 100 / (1 + (_positiveAvg / _negativeAvg));

            Log($"initial RSI: {_rsi: 0.###}");
        }

        #region Backtest
        private double GetStopValue(List<Kline> klines, bool isLong, double previousValue = -1)
        {
            _rangeKlines = klines;
            CalcTrendAngle(previousValue, isLong);
            decimal stopValue = FormatPrice(_currentCloseEMA * (_alpha + (decimal)_longAngle / 100) / _alpha);
            if (!_byClosePrice)
            {
                if (isLong) stopValue = FormatPrice(_currentHighEMA * (_alpha + (decimal)_longAngle / 100) / _alpha);
                else stopValue = FormatPrice(_currentLowEMA * (_alpha + (decimal)_shortAngle / 100) / _alpha);
            }
            var eaxInfo = _byClosePrice ? $"TA {_trendCloseAngle:0.####}% EMA{_emaLength} {_currentCloseEMA:0.####}" : $"TAH {_trendHighAngle:0.####}% TAL {_trendLowAngle:0.####}% EMAH{_emaLength} {_currentHighEMA:0.####} EMAL{_emaLength} {_currentLowEMA:0.####}";
            if (AccountManager.LogLevel == 10) Console.WriteLine($"{BnnUtils.FormatUnixTime(_rangeKlines[^1].OpenTime)} {eaxInfo} {(isLong ? "long" : "short")} aim {stopValue:0.###}");
            return (double)stopValue;
        }

        protected bool _backtestInited = false;

        private void ProcessKline(List<Kline> klines)
        {
            var lastKline = klines[^1];
            BacktestTime = lastKline.OpenTime;

            var currSecond = BnnUtils.UnitTimeToDateTime(BacktestTime).Second;
            if (currSecond < _lastCheckSecond) // (lastKline.OpenTime % 60000 == 0)
            {
                CalcStopsFromOrderBook(); // recalc EMA
                ChangeOrderStoploss();
            }
            _lastCheckSecond = currSecond;

            if (lastKline.ClosePrice >= lastKline.OpenPrice)
            {
                DetectCurrentPrice(lastKline.OpenPrice + _priceStep, lastKline.OpenPrice);
                if (lastKline.LowPrice < lastKline.OpenPrice) DetectCurrentPrice(lastKline.LowPrice + _priceStep, lastKline.LowPrice);
                if (lastKline.HighPrice > lastKline.ClosePrice) DetectCurrentPrice(lastKline.HighPrice, lastKline.HighPrice - _priceStep);
                if (lastKline.ClosePrice != lastKline.OpenPrice) DetectCurrentPrice(lastKline.ClosePrice, lastKline.ClosePrice - _priceStep);

                // optimistic
                /* DetectCurrentPrice((decimal)lastKline.OpenPrice, (decimal)lastKline.OpenPrice - _priceStep);
                if (lastKline.LowPrice < lastKline.OpenPrice) DetectCurrentPrice((decimal)lastKline.LowPrice, (decimal)lastKline.LowPrice - _priceStep);
                if (lastKline.HighPrice > lastKline.ClosePrice) DetectCurrentPrice((decimal)lastKline.HighPrice + _priceStep, (decimal)lastKline.HighPrice);
                if (lastKline.ClosePrice != lastKline.OpenPrice) DetectCurrentPrice((decimal)lastKline.ClosePrice + _priceStep, (decimal)lastKline.ClosePrice);*/
            }
            else
            {
                // optimistic
                DetectCurrentPrice(lastKline.OpenPrice + _priceStep, lastKline.OpenPrice);
                if (lastKline.HighPrice > lastKline.OpenPrice) DetectCurrentPrice(lastKline.HighPrice + _priceStep, lastKline.HighPrice);
                if (lastKline.LowPrice < lastKline.ClosePrice) DetectCurrentPrice(lastKline.LowPrice, lastKline.LowPrice - _priceStep);
                if (lastKline.ClosePrice != lastKline.OpenPrice) DetectCurrentPrice(lastKline.ClosePrice, lastKline.ClosePrice - _priceStep);
            }
            if (AccountManager.LogLevel > 9) Log($"{CurrPrice}"); // protected override double GetLongValue:  : ema{_emaLength} {_previousCloseEMA:0.###} {_order}

            if (!_backtestInited)
            {
                CurrencyAmount = 0;
                _manager.CheckBalance();
                _previousCloseEMA = (1 - _alpha) * klines[^2].ClosePrice + _alpha * klines[^3].ClosePrice; // a bit reversal
                _previousHighEMA = (1 - _alpha) * klines[^2].HighPrice + _alpha * klines[^3].HighPrice; // a bit reversal
                _previousLowEMA = (1 - _alpha) * klines[^2].LowPrice + _alpha * klines[^3].LowPrice; // a bit reversal
                CalcStopsFromOrderBook();
                _backtestInited = true;
            }

            if (_order == null) InitOrder();
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            // Log($"GetLongValue !!!: ema {_previousCloseEMA} / {_order}"); // protected override double GetLongValue: 
            /* var newLongPrice = GetStopValue(klines, true, previousValue);
            if (newLongPrice < LongPrice || previousValue == -1) return newLongPrice;
            return LongPrice;*/
            ProcessKline(klines);
            return _longPrice; //  double.MaxValue;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            ProcessKline(klines);
            return (double)_shortPrice; //  1000000.0;
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = -1;
            if (IsBacktest()) return int.MaxValue;
            else return (double)_shortPrice;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            //
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] maDiffIntervals = [1];
            /* int[] emaIntervals = [1, 3, 5]; // 7, 11
            double[] longAngles = [0.001, 0.003, 0.007];
            double[] shortAngles = [-0.001, -0.005, -0.009];*/

            int[] emaIntervals = [57, 75, 105]; // 7, 11 240, 720, 1440
            double[] longAngles = [0.025, 0.032, 0.039];
            double[] shortAngles = [-0.025, -0.032, -0.039];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = emaIntervals.Length * maDiffIntervals.Length * longAngles.Length * shortAngles.Length;
            long counter = 0;
            foreach (var el in emaIntervals)
            {
                _emaLength = el;
                foreach (var di in maDiffIntervals)
                {
                    _emaDiffInterval = di;
                    foreach (var la in longAngles)
                    {
                        _longAngle = la;
                        foreach (var sa in shortAngles)
                        {
                            _shortAngle = sa;
                            _alpha = 2.0M / (_emaLength + 1);
                            var dp = new EmaDealParams(el, di, la, sa);
                            _dealParams = dp;
                            _order = null;
                            _backtestInited = false;
                            _manager.BackTest(); // klines

                            // Console.WriteLine($"{_dealParams.TotalProfit} / {CurrPrice} / {CurrencyAmount} / {_manager.Amount} / {_order.TotalQuote}");
                            // _dealParams.TotalProfit = CurrPrice * (double)CurrencyAmount + (double)_manager.Amount;
                            // _dealParams.TotalProfit = (_order == null) ? CurrPrice * (double)CurrencyAmount + (double)_manager.Amount : (double)(_order.Amount * _order.Price);
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


        protected double CalcEMA(int emaLength, int klineIndex, bool isLow = false)
        {
            if (_rangeKlines == null) throw new Exception("Klines are null");
            var prolog = 30; // 70
            var firstKline = _rangeKlines[klineIndex - (emaLength + 2 + prolog)];
            var ema = isLow ? firstKline.LowPrice : firstKline.HighPrice;
            if (_byClosePrice) ema = firstKline.ClosePrice;

            for (var i = klineIndex - emaLength - 1 - prolog; i <= klineIndex; i++)
            {
                if (_byClosePrice) ema = _rangeKlines[i].ClosePrice * _alpha + (1 - _alpha) * ema;
                else ema = (isLow ? _rangeKlines[i].LowPrice : _rangeKlines[i].HighPrice) * _alpha + (1 - _alpha) * ema;
            }
            return (double)ema;
        }


        private void CalcStopsFromArchive()
        {
            UpdateCandlesticks(250);
            var endTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeMilliseconds();
            var startTime = endTime - 24 * 60 * 60L * 1000;
            _rangeKlines = LoadKlinesFromDB(startTime, endTime);
            _longPrice = (decimal)GetStopValue(_rangeKlines, true, -1);
            _shortPrice = (decimal)GetStopValue(_rangeKlines, false, -1);
        }


        protected double CalcTrendAngle(double previousValue = -1, bool isLow = false)
        {
            if (_rangeKlines == null) throw new Exception("Klines are null (CalcTrendAngle)");

            if (_byClosePrice)
            {
                _currentCloseEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1);
                _previousCloseEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1 - _emaDiffInterval);
                _trendCloseAngle = (double)((_currentCloseEMA - _previousCloseEMA) / _previousCloseEMA * 100);
                return _trendCloseAngle;
            }
            else
            {
                _currentHighEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1, false);
                _previousHighEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1 - _emaDiffInterval, false);
                _trendHighAngle = (double)((_currentHighEMA - _previousHighEMA) / _previousHighEMA * 100);
                _currentLowEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1, true);
                _previousLowEMA = (decimal)CalcEMA(_emaLength, _rangeKlines.Count - 1 - _emaDiffInterval, true);
                _trendLowAngle = (double)((_currentLowEMA - _previousLowEMA) / _previousLowEMA * 100);
                return isLow ? _trendLowAngle : _trendHighAngle;
            }
        }
        #endregion

        #region RealTimeRoutines
        public override string GetCurrentInfo()
        {
            return $"{SymbolName.ToUpper()} {CurrPrice} EMA{_emaLength} {(_byClosePrice ? _previousCloseEMA : _previousHighEMA):0.####} {(IsDealEntered ? "short" : "long")} aim {(IsDealEntered ? _shortPrice : _longPrice):0.###} {_order}";
        }


        private void ChangeOrderStoploss()
        {
            if (_order == null) return; //  || (_order.IsTriggered)

            var newPrice = _order.IsBuyer ? FormatPrice(_longPrice) : FormatPrice(_shortPrice);
            if (_order.IsBuyer)
            {
                if (newPrice <= (decimal)CurrPrice && _order.Price > (decimal)CurrPrice) return; // to prevent 'stop price would trigger immediately'
                if (newPrice >= _order.Price)
                {
                    if ((decimal)CurrPrice < _order.Price) return; // raise price only if it jump over previos stop
                    else Log($"{CurrPrice}: price jump over previous stop (previous stop: {_order.Price}, new price: {newPrice})");
                }

                _order.Price = newPrice;
                _order.Amount = FormatQuantity(_order.TotalQuote / _order.Price);
                _manager.SpotAmount = _order.TotalQuote - _order.Amount * _order.Price;
            }
            else
            {
                if (newPrice >= (decimal)CurrPrice && _order.Price < (decimal)CurrPrice) return; // to prevent 'stop price would trigger immediately'
                if (newPrice <= _order.Price)
                {
                    if (_order.Price < (decimal)CurrPrice) return; // lower price only if it jump over previos stop
                    else Log($"{CurrPrice}: price jump over previous stop (previous stop: {_order.Price}, new price: {newPrice})");
                }
                _order.Price = newPrice;
                _order.Amount = FormatQuantity(_order.Amount + CurrencyAmount); // :0.########
            }
            _order.Filled = 0;

            try
            {
                var newId = _manager.ReplaceOrder(_order.LongId, SymbolName, _order.IsBuyer ? Side.BUY : Side.SELL, _order.Amount, _order.Price, _priceStep);
                if (AccountManager.IsTest)
                {
                    var ap = GetActualPrice();
                    if (_order.IsBuyer && ap > _order.Price || !_order.IsBuyer && ap < _order.Price) throw new BnnException(string.Empty, BnnException.StopPriceWouldTrigger);
                    if (AccountManager.LogLevel > 1) Log($"{CurrPrice}: stop loss changed: {_order}");
                }
                else ChangeOrderData(_order.LongId, newId, _order.Amount, _order.Price);
                _order.LongId = newId;
            }
            catch (BnnException ex)
            {
                if (ex.Code == BnnException.UnknownOrderSent)
                {
                    BnnUtils.Log($"{_order} - unknown order sent, seems like a wick triggered the order");
                    // CheckDeal(true);
                };
                if (ex.Code == BnnException.StopPriceWouldTrigger)
                {
                    Log($"{_order} - stop price would trigger immediately: _order = null");
                    if (_order.IsBuyer) _manager.SpotAmount = _order.TotalQuote;
                    else CurrencyAmount += _order.Amount;
                    _order = null;
                }
            }
        }


        private void CheckDeal(bool isWick = false)
        {
            if (_order == null) return;
            if (!isWick) return; //  && (!IsLong || CurrPrice > ShortPrice) && (IsLong || CurrPrice < LongPrice)

            try
            {
                if (_order.Filled != _order.Amount)
                {
                    if (!AccountManager.IsTest)
                    {
                        var status = isWick ? OrderStatus.Filled : _manager.CheckOrder(this, _order.LongId);
                        if (status != OrderStatus.Filled) return;
                    }
                    _order.Filled = _order.Amount;

                    var descr = IsDealEntered ? $"short {CurrPrice} < {_shortPrice:0.###}" : $"long {CurrPrice} > {_longPrice:0.###}";
                    // if (AccountManager.IsTest && !IsBacktest()) Log($"{descr} last ema{_emaLength} {(_byClosePrice ? _previousCloseEMA : _previousHighEMA):0.###}");
                    if (AccountManager.LogLevel > 5) Log($"{descr} last ema{_emaLength} {(_byClosePrice ? _previousCloseEMA : _previousHighEMA):0.###}");

                    ExecuteOrder(_order);
                    /*if (_order.IsBuyer) CurrencyAmount += _order.Amount * (1 - AccountManager.Fee / 2);
                    else _manager.Amount += _order.Amount * _order.Price * (1 - AccountManager.Fee / 2);
                    Log($"{_order}: FILLED ({GetCurrencyName().ToLower()[0]} {CurrencyAmount} {_manager.QuoteCurrencyName.ToLower()[0]} {_manager.Amount:0.#######})");
                    Log(string.Empty); //, false*/

                    // if (!AccountManager.IsTest) _manager.CheckBalance();	
                }

                IsDealEntered = !IsDealEntered;
                var newPrice = IsDealEntered ? (decimal)_shortPrice : (decimal)_longPrice;
                if (IsDealEntered && CurrPrice < _shortPrice || !IsDealEntered && CurrPrice > _longPrice) // something goes wrong
                {
                    var actualPrice = GetActualPrice();
                    newPrice = IsDealEntered ? actualPrice - _priceChangeThreshold : actualPrice + _priceChangeThreshold;
                    Log($"{CurrPrice}: new price was reset a bit {(IsDealEntered ? "lower" : "higher")} that current price (new price: {newPrice}, ema price: {(IsDealEntered ? _shortPrice : _longPrice):0.###}, actual price: {actualPrice})");
                }
                _order = CreateSpotLimitOrder(newPrice, !IsDealEntered);
            }
            catch (Exception ex)
            {
                Log($"error in CheckDeal: {ex}");
                Environment.Exit(1);
            }
        }


        private void InitOrder()
        {
            // if ((IsLong && (ShortPrice >= CurrPrice)) || (!IsLong && (LongPrice <= CurrPrice))) return;
            var newPrice = IsDealEntered ? Math.Min((decimal)_shortPrice, (decimal)CurrPrice - _priceChangeThreshold) : Math.Max((decimal)_longPrice, (decimal)CurrPrice + _priceChangeThreshold);
            // if (IsLong) _order = CreateOrder((decimal)ShortPrice, false);
            // else _order = CreateOrder((decimal)LongPrice, true);
            _order = CreateSpotLimitOrder(newPrice, !IsDealEntered);
        }

        private decimal _klineHighPrice = 0;
        private decimal _klineLowPrice = decimal.MaxValue;

        protected void CalcStopsFromOrderBook()
        {
            // Console.WriteLine($"_previousCloseEMA={_previousCloseEMA}, curr price={CurrPrice}, ");

            _previousHighEMA = _klineHighPrice * _alpha + (1 - _alpha) * _previousHighEMA;
            _previousLowEMA = _klineLowPrice * _alpha + (1 - _alpha) * _previousLowEMA;
            _previousCloseEMA = CurrPrice * _alpha + (1 - _alpha) * _previousCloseEMA;

            if (_byClosePrice)
            {
                _longPrice = _previousCloseEMA * (_alpha + (decimal)_longAngle / 100) / _alpha;
                _shortPrice = _previousCloseEMA * (_alpha + (decimal)_shortAngle / 100) / _alpha;
            }
            else
            {
                _longPrice = _previousHighEMA * (_alpha + (decimal)_longAngle / 100) / _alpha;
                _shortPrice = _previousLowEMA * (_alpha + (decimal)_shortAngle / 100) / _alpha;
            }
            _klineHighPrice = 0;
            _klineLowPrice = decimal.MaxValue;
        }


        private void DetectCurrentPrice(decimal bestAsk, decimal bestBid)
        {
            try
            {
                if (bestAsk < CurrPrice) CurrPrice = bestAsk;
                else CurrPrice = bestBid;
                CurrPrice = FormatPrice(CurrPrice);
                if (CurrPrice > _klineHighPrice) _klineHighPrice = CurrPrice;
                if (CurrPrice < _klineLowPrice) _klineLowPrice = CurrPrice;

                // stop-loss order triggered
                if (_order == null) return; //  || _jumpReported
                if ((_order.IsBuyer && (_order.Price <= bestBid)) || (!_order.IsBuyer && (_order.Price >= bestAsk)))
                {
                    if (_order.Filled == 0) Log($"{CurrPrice}: stop-order triggered"); // : curr price={CurrPrice}, current stop: {_order.Price}
                    _order.Filled = 1;

                    var newPrice = _order.IsBuyer ? bestBid : bestAsk;
                    if ((_order.Price != newPrice) && _isCatchUp) ChangeOrderPrice(_order, newPrice);
                }

                // stop-loss order excuted
                if (!_order.IsTriggered || !AccountManager.IsTest) return;
                if ((_order.IsBuyer && (_order.Price - _priceStep >= bestBid)) || (!_order.IsBuyer && (_order.Price + _priceStep < bestAsk)))
                {
                    Log($"{CurrPrice}: order executed"); // : IsBuyer: {_order.IsBuyer}, curr price={(_order.IsBuyer ? bestBid : bestAsk)}, current stop: {_order.Price}, CurrPrice: {CurrPrice} stop-
                    CheckDeal(true);
                }
            }
            catch (Exception ex)
            {
                Log($"Error in method DetectCurrentPrice: {ex}");
            }
        }


        private void ProcessOrderBook(dynamic ordersData)
        {
            decimal bestAsk = ordersData.asks[0][0];
            decimal bestBid = ordersData.bids[0][0];

            if (DateTime.Now.Second < _lastCheckSecond)
            {
                CalcStopsFromOrderBook(); // recalc EMA
                DetectCurrentPrice(bestAsk, bestBid);
                ChangeOrderStoploss();
            }
            _lastCheckSecond = DateTime.Now.Second;

            DetectCurrentPrice(bestAsk, bestBid);
            if (_order == null) InitOrder();
            // if (AccountManager.IsTest) CheckDeal();
        }


        private Task ProcessUserDataMessage(string message)
        {
            if (_order == null) return Task.CompletedTask;
            dynamic? userData = JsonConvert.DeserializeObject(message.Trim()) ?? throw new Exception("user data message returned no data");
            if (userData.e != "executionReport" || userData.X != OrderStatus.Filled) return Task.CompletedTask;
            if (userData.i == _order.LongId) CheckDeal(true);
            else BnnUtils.Log($"message from user data stream: unknown order {userData.i} was {userData.X} ({userData.s}, {userData.S}, {userData.q} x {userData.p})");
            return Task.CompletedTask;
        }


        public override void Prepare()
        {
            CalcStopsFromArchive();
            IsDealEntered = _manager.SpotAmount / (decimal)_longPrice < _lotStep;
            BnnUtils.Log($"la {_longAngle} sa {_shortAngle}", false); // last
            BnnUtils.Log($"EMA{_emaLength} {(_byClosePrice ? _previousCloseEMA : _previousHighEMA):0.####} {(IsDealEntered ? "short" : "long")} aim {(IsDealEntered ? _shortPrice : _longPrice):0.###}", false); // last
            _priceChangeThreshold = 3 * _priceStep;

            if (AccountManager.IsTest) return;
            LoadOrders();
            foreach (var o in _orders)
            {
                var orderStatus = _manager.CheckOrder(this, o.LongId);
                BnnUtils.Log($"loaded order: {o} - {orderStatus}", false);
                if (new[] { OrderStatus.Canceled, OrderStatus.Filled }.Contains(orderStatus)) continue;
                _order = o;
                IsDealEntered = !_order.IsBuyer;
            }

            _manager.ConnectToUserStream(ProcessUserDataMessage);
        }


        protected virtual void ExitLong(Kline kline, decimal limitPrice)
        {
            IsDealEntered = false;
            var balance = (decimal)_manager.Statistics.TotalProfit;
            if (limitPrice == 0) limitPrice = CurrPrice;

            decimal newBalance;
            if (_isFutures)
            {
                newBalance = _shortPrice / limitPrice * balance * (1 - Slippage) * (1 - Slippage);
                if (AccountManager.LogLevel > 5) Log($"{_shortPrice:0.###} / {limitPrice:0.###} * {balance:0.###} * {1 - Slippage}^2 = {newBalance:0.###} ( {(newBalance - balance) / balance * 100:0.##}% )");
            }
            else
            {
                newBalance = limitPrice / _longPrice * balance * (1 - Slippage) * (1 - Slippage);
                if (AccountManager.LogLevel > 5) Log($"{limitPrice:0.###} / {_longPrice:0.###} * {balance:0.###} * {1 - Slippage}^2 = {newBalance:0.###} ( {(newBalance - balance) / balance * 100:0.##}% )");
            }
            _dealParams.TotalProfit = (double)newBalance;
            _manager.Statistics.DealCount++;
            _dealParams.DealCount = _manager.Statistics.DealCount;
            _manager.Statistics.TotalProfit = _dealParams.TotalProfit;
            if (AccountManager.LogLevel > 5) Log(string.Empty);
        }


        public override async void Start() // async 
        {
            var ws = new MarketDataWebSocket($"{SymbolName.ToLower()}@depth20@100ms"); // 20 --- @100ms
            ws.OnMessageReceived(data =>
            {
                dynamic? ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("depth returned no data");

                try
                {
                    ProcessOrderBook(ordersData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    Environment.Exit(0);
                }
                return Task.CompletedTask;
            }, CancellationToken.None);

            await ws.ConnectAsync(CancellationToken.None);
        }
        #endregion
    }
}