using System.Collections.Generic;

namespace Arcade.BrainrotExchange
{
    public record Stock(string Symbol, string Name, long Price, long Previous, long Min, long Max)
    {
        public long Change => Price - Previous;
    }

    public class PriceHistory
    {
        public const int Length = 30;

        private readonly Dictionary<string, Queue<long>> _prices = new();

        public IEnumerable<long> Of(string symbol) =>
            _prices.TryGetValue(symbol, out var queue) ? queue : System.Array.Empty<long>();

        public void Add(string symbol, long price)
        {
            if (!_prices.TryGetValue(symbol, out var queue)) _prices[symbol] = queue = new Queue<long>();
            queue.Enqueue(price);
            while (queue.Count > Length) queue.Dequeue();
        }
    }
}
