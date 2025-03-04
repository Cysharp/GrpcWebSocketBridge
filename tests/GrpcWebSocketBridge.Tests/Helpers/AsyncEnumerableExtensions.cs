namespace GrpcWebSocketBridge.Tests.Helpers;

internal static class AsyncEnumerableExtensions
{
    public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in enumerable.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list.ToArray();
    }

    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        await foreach (var item in enumerable.WithCancellation(cancellationToken))
        {
            return item;
        }

        throw new InvalidOperationException("Sequence contains no elements");
    }
}
