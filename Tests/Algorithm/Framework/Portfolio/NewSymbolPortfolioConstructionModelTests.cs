/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Accord.Statistics;
using NUnit.Framework;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Securities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace QuantConnect.Tests.Algorithm.Framework.Portfolio
{
    [TestFixture]
    public class NewSymbolPortfolioConstructionModelTests
    {
        [Test]
        public void NewSymbolPortfolioConstructionModelDoesNotThrow()
        {
            var algorithm = new QCAlgorithm();
            var timezone = algorithm.TimeZone;
            algorithm.SetDateTime(new DateTime(2018, 8, 7).ConvertToUtc(timezone));
            algorithm.SetPortfolioConstruction(new NewSymbolPortfolioConstructionModel());

            var spySymbol = Symbols.SPY;
            var spy = GetSecurity(algorithm, spySymbol);

            spy.SetMarketPrice(new Tick(algorithm.Time, spySymbol, 1m, 1m));
            algorithm.Securities.Add(spySymbol, spy);

            algorithm.PortfolioConstruction.OnSecuritiesChanged(algorithm, SecurityChanges.Added(spy));

            var insights = new[] {Insight.Price(spySymbol, Time.OneMinute, InsightDirection.Up, .1)};

            Assert.DoesNotThrow(() => algorithm.PortfolioConstruction.CreateTargets(algorithm, insights));

            algorithm.SetDateTime(algorithm.Time.AddDays(1));

            var aaplSymbol = Symbols.AAPL;
            var aapl = GetSecurity(algorithm, aaplSymbol);

            aapl.SetMarketPrice(new Tick(algorithm.Time, aaplSymbol, 1m, 1m));
            algorithm.Securities.Add(aaplSymbol, aapl);

            algorithm.PortfolioConstruction.OnSecuritiesChanged(algorithm, SecurityChanges.Added(aapl));

            insights = new[] { spySymbol, aaplSymbol }
                .Select(x => Insight.Price(x, Time.OneMinute, InsightDirection.Up, .1)).ToArray();

            Assert.DoesNotThrow(() => algorithm.PortfolioConstruction.CreateTargets(algorithm, insights));
        }

        private Security GetSecurity(QCAlgorithm algorithm, Symbol symbol)
        {
            return new Security(
                SecurityExchangeHours.AlwaysOpen(algorithm.TimeZone),
                new SubscriptionDataConfig(
                    typeof(TradeBar),
                    symbol,
                    Resolution.Daily,
                    algorithm.TimeZone,
                    algorithm.TimeZone,
                    true,
                    false,
                    false
                ),
                new Cash(Currencies.USD, 0, 1),
                SymbolProperties.GetDefault(Currencies.USD),
                ErrorCurrencyConverter.Instance,
                RegisteredSecurityDataTypesProvider.Null,
                new SecurityCache()
            );
        }

        private class NewSymbolPortfolioConstructionModel : BlackLittermanOptimizationPortfolioConstructionModel
        {
            private readonly Dictionary<Symbol, ReturnsSymbolData> _symbolDataDict = new Dictionary<Symbol, ReturnsSymbolData>();

            public override IEnumerable<IPortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
            {
                // Updates the ReturnsSymbolData with insights
                foreach (var insight in insights)
                {
                    ReturnsSymbolData symbolData;
                    if (_symbolDataDict.TryGetValue(insight.Symbol, out symbolData))
                    {
                        symbolData.Add(algorithm.Time, .1m);
                    }
                }

                double[,] returns = null;
                Assert.DoesNotThrow(() => returns = _symbolDataDict.FormReturnsMatrix(insights.Select(x => x.Symbol)));

                // Calculate posterior estimate of the mean and uncertainty in the mean
                double[,] Σ;
                var Π = GetEquilibriumReturns(returns, out Σ);

                Assert.IsFalse(double.IsNaN(Π[0]));

                return Enumerable.Empty<PortfolioTarget>();
            }

            public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
            {
                const int period = 2;
                var reference = algorithm.Time.AddDays(-period);

                foreach (var security in changes.AddedSecurities)
                {
                    var symbol = security.Symbol;
                    var symbolData = new ReturnsSymbolData(symbol, 1, period);

                    for (var i = 0; i <= period * 2; i++)
                    {
                        symbolData.Update(reference.AddDays(i), i);
                    }

                    _symbolDataDict[symbol] = symbolData;
                }
            }
        }
    }
}